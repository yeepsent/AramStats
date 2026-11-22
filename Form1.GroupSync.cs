using System.Net;
using System.Text.Json;

namespace AramStats;

public partial class Form1
{
    private readonly Dictionary<string, GroupSyncClient.Game> groupGames = new(StringComparer.Ordinal);
    private readonly HashSet<string> submittedGroupGames = new(StringComparer.Ordinal);
    private readonly HashSet<string> rejectedGroupGames = new(StringComparer.Ordinal);
    private GroupSyncClient? groupClient;
    private GroupSyncClient.Roster? groupRoster;
    private long groupCursor;
    private bool groupSyncRunning;
    private Button groupLoginButton = null!;
    private Label groupStatus = null!;

    private sealed record GroupSyncSnapshot(string Message, bool IsRunning, bool CanSync, DateTimeOffset? LastSuccess);
    private GroupSyncSnapshot GroupSyncState { get; set; } = new("공동 통계 계정으로 로그인해 주세요.", false, false, null);
    private event EventHandler? GroupSyncStateChanged;

    private void PublishGroupSyncState(string message, bool running = false, bool success = false, bool reset = false)
    {
        GroupSyncState = new GroupSyncSnapshot(message, running, groupClient is not null && !running,
            reset ? null : success ? DateTimeOffset.UtcNow : GroupSyncState.LastSuccess);
        GroupSyncStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private Control BuildGroupSyncPanel()
    {
        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));

        groupStatus = new Label { Dock = DockStyle.Fill, AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft, AccessibleName = "클라우드 동기화 상태" };
        groupLoginButton = new Button { Text = "로그인", Dock = DockStyle.Fill };
        var sync = new Button { Text = "지금 동기화", Dock = DockStyle.Fill };
        bar.Controls.Add(groupStatus, 0, 0);
        bar.Controls.Add(groupLoginButton, 1, 0);
        bar.Controls.Add(sync, 2, 0);
        var statusTip = new ToolTip();
        void RenderState()
        {
            var state = GroupSyncState;
            var last = state.LastSuccess?.ToLocalTime().ToString("MM-dd HH:mm") ?? "—";
            groupStatus.Text = $"클라우드 · 마지막 성공 {last} · {state.Message}";
            statusTip.SetToolTip(groupStatus, groupStatus.Text);
            sync.Enabled = state.CanSync;
            sync.Text = state.IsRunning ? "동기화 중…" : "지금 동기화";
        }
        EventHandler onChanged = (_, _) => RenderState();
        GroupSyncStateChanged += onChanged;
        bar.Disposed += (_, _) =>
        {
            GroupSyncStateChanged -= onChanged;
            statusTip.Dispose();
        };
        groupLoginButton.Click += async (_, _) => await ToggleGroupLoginAsync();
        sync.Click += async (_, _) => await SyncGroupAsync();
        RenderState();
        return bar;
    }
    private DataGridView groupMemberGrid = null!;
    private DataGridView groupChampionGrid = null!;
    private DataGridView groupPairGrid = null!;

    private void InitializeGroupSync()
    {
        var tab = new TabPage("공동 통계");
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1, Padding = new Padding(10) };
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var views = new TabControl { Dock = DockStyle.Fill };
        groupMemberGrid = AddGroupGrid(views, "멤버", "멤버", "경기", "승리", "승률");
        groupChampionGrid = AddGroupGrid(views, "멤버별 챔피언", "멤버", "챔피언", "경기", "승리", "승률", "킬", "데스", "어시스트");
        groupPairGrid = AddGroupGrid(views, "같은 팀 조합", "멤버 1", "멤버 2", "경기", "승리", "승률");
        content.Controls.Add(views, 0, 0);
        tab.Controls.Add(content);
        mainNavigation.TabPages.Add(tab);

        var timer = new System.Windows.Forms.Timer { Interval = 60000 };
        timer.Tick += async (_, _) => await SyncGroupAsync();
        timer.Start();
        Disposed += (_, _) =>
        {
            timer.Dispose();
            groupClient?.Dispose();
        };
    }

    private static DataGridView AddGroupGrid(TabControl views, string title, params string[] columns)
    {
        var page = new TabPage(title);
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        foreach (var column in columns)
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = column, ValueType = typeof(string), SortMode = DataGridViewColumnSortMode.Automatic
            });
        page.Controls.Add(grid);
        views.TabPages.Add(page);
        return grid;
    }

    private async Task ToggleGroupLoginAsync()
    {
        if (groupClient is not null)
        {
            ClearGroupSession("공동 통계에서 로그아웃했습니다.");
            return;
        }

        var url = Environment.GetEnvironmentVariable("ARAMSTATS_SUPABASE_URL")?.Trim();
        var key = Environment.GetEnvironmentVariable("ARAMSTATS_SUPABASE_PUBLISHABLE_KEY")?.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var project) || project.Scheme != Uri.UriSchemeHttps
            || project.AbsolutePath != "/" || project.Query.Length != 0 || project.Fragment.Length != 0
            || project.UserInfo.Length != 0 || key is null || !key.StartsWith("sb_publishable_", StringComparison.Ordinal))
        {
            PublishGroupSyncState("Supabase 프로젝트 URL과 publishable key를 설정해 주세요.");
            return;
        }

        using var dialog = new Form
        {
            Text = "공동 통계 로그인", ClientSize = new Size(365, 135),
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false, MaximizeBox = false
        };
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var email = new TextBox { Dock = DockStyle.Fill, AccessibleName = "이메일" };
        var password = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true, AccessibleName = "비밀번호" };
        var login = new Button { Text = "로그인", DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right };
        fields.Controls.Add(new Label { Text = "이메일", AutoSize = true }, 0, 0);
        fields.Controls.Add(email, 1, 0);
        fields.Controls.Add(new Label { Text = "비밀번호", AutoSize = true }, 0, 1);
        fields.Controls.Add(password, 1, 1);
        fields.Controls.Add(login, 1, 2);
        dialog.Controls.Add(fields);
        dialog.AcceptButton = login;
        ApplyDashboardTheme(dialog);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        var client = new GroupSyncClient(project, key);
        groupLoginButton.Enabled = false;
        PublishGroupSyncState("로그인 중…");
        try
        {
            var signingIn = client.SignInAsync(email.Text.Trim(), password.Text);
            password.Clear();
            await signingIn;
            if (IsDisposed || Disposing)
            {
                client.Dispose();
                return;
            }
            groupClient = client;
            groupLoginButton.Text = "로그아웃";
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            client.Dispose();
            if (!IsDisposed && !Disposing)
                PublishGroupSyncState("공동 통계 로그인 실패. 계정 정보와 서버 연결을 확인해 주세요.");
            return;
        }
        finally
        {
            if (!IsDisposed && !Disposing)
                groupLoginButton.Enabled = true;
        }
        await SyncGroupAsync();
    }

    private void ClearGroupSession(string status)
    {
        groupClient?.Dispose();
        groupClient = null;
        groupRoster = null;
        groupCursor = 0;
        groupGames.Clear();
        submittedGroupGames.Clear();
        rejectedGroupGames.Clear();
        groupLoginButton.Text = "로그인";
        PublishGroupSyncState(status, reset: true);
        RenderGroupStats();
    }

    private async Task SyncGroupAsync()
    {
        var client = groupClient;
        if (client is null || groupSyncRunning)
            return;

        groupSyncRunning = true;
        groupLoginButton.Enabled = false;
        PublishGroupSyncState("공동 전적 동기화 중…", running: true);
        try
        {
            var roster = await client.GetRosterAsync();
            if (IsDisposed || Disposing) return;
            if (groupRoster?.GroupId != roster.GroupId || groupRoster?.OwnPuuid != roster.OwnPuuid)
            {
                submittedGroupGames.Clear();
                rejectedGroupGames.Clear();
            }
            bool rosterChanged = groupRoster?.GroupId != roster.GroupId ||
                !groupRoster.Members.SequenceEqual(roster.Members);
            if (groupRoster?.GroupId != roster.GroupId ||
                !groupRoster.Members.Select(m => m.Id).SequenceEqual(roster.Members.Select(m => m.Id)))
            {
                groupCursor = 0;
                groupGames.Clear();
            }
            groupRoster = roster;
            if (rosterChanged) RenderGroupStats();

            bool accountMatches = string.Equals(activePuuid, roster.OwnPuuid, StringComparison.Ordinal);
            int uploadedCandidates = accountMatches ? await UploadGroupMatchesAsync(client, roster) : 0;
            if (IsDisposed || Disposing) return;

            // A bounded pass keeps large backfills responsive. The timer resumes with the same cursor.
            var moreRemote = false;
            var nextCursor = groupCursor;
            var incoming = new List<GroupSyncClient.Game>();
            for (int page = 0; page < 10; page++)
            {
                var games = await client.DownloadAsync(roster.GroupId, nextCursor);
                if (IsDisposed || Disposing) return;
                foreach (var game in games)
                {
                    if (game.Cursor <= nextCursor)
                        throw new JsonException("공동 전적 페이지의 순서가 올바르지 않습니다.");
                    incoming.Add(game);
                    nextCursor = game.Cursor;
                }
                moreRemote = games.Length == 200;
                if (!moreRemote) break;
            }
            foreach (var game in incoming)
                groupGames[game.GameId] = game;
            groupCursor = nextCursor;
            if (incoming.Count > 0) RenderGroupStats();

            var name = roster.GroupName;
            var waiting = moreRemote || uploadedCandidates > 100 ? " · 다음 동기화에서 계속" : "";
            var accountHint = accountMatches ? "" : " · 등록된 롤 계정 연결 시 업로드";
            var rejected = rejectedGroupGames.Count > 0 ? $" · 충돌 또는 거부 {rejectedGroupGames.Count}판" : "";
            PublishGroupSyncState($"{name} · 멤버 {roster.Members.Length}명 · 멤버 제출 전적 {groupGames.Count}판{waiting}{accountHint}{rejected}", running: true, success: true);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            if (!IsDisposed && !Disposing)
                ClearGroupSession("공동 통계 접근 권한이나 로그인 세션을 확인해 주세요.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException
                                                    or ObjectDisposedException)
        {
            if (!IsDisposed && !Disposing)
                PublishGroupSyncState("동기화에 실패했습니다. 지금 동기화로 다시 시도할 수 있습니다.", running: true);
        }
        finally
        {
            groupSyncRunning = false;
            if (!IsDisposed && !Disposing)
            {
                groupLoginButton.Enabled = true;
                if (GroupSyncState.IsRunning) PublishGroupSyncState(GroupSyncState.Message);
            }
        }
    }

    private async Task<int> UploadGroupMatchesAsync(GroupSyncClient client, GroupSyncClient.Roster roster)
    {
        var latestAllowed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 86400000;
        var matches = savedMatches.Values
            .Where(match => match.QueueId == 2400 && (match.Duration == 0 || match.Duration >= 480)
                && !submittedGroupGames.Contains(match.GameId) && !rejectedGroupGames.Contains(match.GameId)
                && match.Timestamp >= 1500000000000L && match.Timestamp <= latestAllowed
                && match.GameId is not null && System.Text.RegularExpressions.Regex.IsMatch(match.GameId, @"\A[1-9][0-9]{0,19}\z")
                && savedDetails.TryGetValue(match.GameId, out var detail) && IsValidCloudDetail(detail)
                && detail.Participants.Any(person => person.Puuid == roster.OwnPuuid))
            .DistinctBy(match => match.GameId)
            .OrderByDescending(match => match.Timestamp)
            .Take(101)
            .Select(match => new
            {
                match.GameId, match.QueueId, match.Timestamp,
                Participants = savedDetails[match.GameId].Participants.Select(person => new
                {
                    person.Puuid, person.TeamId, person.ChampionId, person.Win, person.Kills, person.Deaths, person.Assists
                }).ToArray()
            }).ToArray();

        foreach (var batch in matches.Take(100).Chunk(25))
        {
            if (IsDisposed || Disposing || activePuuid != roster.OwnPuuid)
                break;
            var results = await client.UploadAsync(roster.GroupId, batch);
            if (!results.Select(result => result.GameId).SequenceEqual(batch.Select(match => match.GameId)))
                throw new JsonException("공동 전적 업로드 응답의 경기 ID가 올바르지 않습니다.");
            foreach (var result in results)
            {
                switch (result.Status)
                {
                    case "inserted":
                    case "duplicate":
                        submittedGroupGames.Add(result.GameId);
                        break;
                    case "conflict":
                    case "invalid":
                        rejectedGroupGames.Add(result.GameId);
                        break;
                    default:
                        throw new JsonException("공동 전적 업로드 상태가 올바르지 않습니다.");
                }
            }
        }
        return matches.Length;
    }

    private void RenderGroupStats()
    {
        groupMemberGrid.Rows.Clear();
        groupChampionGrid.Rows.Clear();
        groupPairGrid.Rows.Clear();
        if (groupRoster is not { } roster) return;

        var names = roster.Members.ToDictionary(member => member.Id,
            member => member.Id == roster.OwnId ? "나" : member.DisplayName);
        var people = groupGames.Values.SelectMany(game => game.Members)
            .Where(person => names.ContainsKey(person.MemberId)).ToLookup(person => person.MemberId);

        foreach (var member in roster.Members.OrderByDescending(member => member.Id == roster.OwnId)
                     .ThenBy(member => member.DisplayName))
        {
            var matches = people[member.Id];
            var name = names[member.Id];
            groupMemberGrid.Rows.Add(name, matches.Count(), matches.Count(person => person.Win), WinRate(matches));
            foreach (var champion in matches.GroupBy(person => person.ChampionId)
                         .OrderByDescending(group => group.Count()).ThenBy(group => group.Key))
            {
                groupChampionGrid.Rows.Add(name, ChampionName(champion.Key), champion.Count(),
                    champion.Count(person => person.Win), WinRate(champion),
                    champion.Sum(person => (long)person.Kills), champion.Sum(person => (long)person.Deaths),
                    champion.Sum(person => (long)person.Assists));
            }
        }

        var pairs = new Dictionary<(Guid First, Guid Second), (int Games, int Wins)>();
        foreach (var game in groupGames.Values)
        {
            foreach (var team in game.Members.Where(member => names.ContainsKey(member.MemberId)).GroupBy(member => member.TeamId))
            {
                var teammates = team.OrderBy(member => member.MemberId).ToArray();
                for (int first = 0; first < teammates.Length; first++)
                {
                    for (int second = first + 1; second < teammates.Length; second++)
                    {
                        var pair = (teammates[first].MemberId, teammates[second].MemberId);
                        var totals = pairs.GetValueOrDefault(pair);
                        pairs[pair] = (totals.Games + 1, totals.Wins + (teammates[first].Win ? 1 : 0));
                    }
                }
            }
        }
        foreach (var (pair, totals) in pairs.OrderByDescending(pair => pair.Value.Games))
            groupPairGrid.Rows.Add(names[pair.First], names[pair.Second], totals.Games, totals.Wins,
                (totals.Wins / (double)totals.Games).ToString("P1"));
    }

    private static string WinRate(IEnumerable<GroupSyncClient.GroupParticipant> games)
    {
        int count = games.Count();
        return count == 0 ? "—" : (games.Count(person => person.Win) / (double)count).ToString("P1");
    }
}
