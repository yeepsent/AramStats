
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Drawing;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AramStats
{
    public partial class Form1 : Form
    {
        // GitHub Releases: 무설치 배포 파일 AramStats.exe (v1.0.0)
        private static readonly string AppVersion =
    System.Reflection.Assembly.GetExecutingAssembly()
        .GetName().Version?.ToString(3) ?? "1.0.0";
        private const string ReleaseApi = "https://api.github.com/repos/yeepsent/AramStats/releases/latest";
        private const string PortableAssetName = "AramStats.exe";
        private bool checkingUpdate;
        private Button btnCheckUpdate;
        private Panel dashboardHost;
        private TabControl mainNavigation;
        private Label lblStatus;
        private Label lblAccount;
        private Label lblCount;
        private TextBox txtResult;
        private Button btnConnect;
        private Button btnMatches;
        private DataGridView dgvMatches;
        private Button btnImport;
        private Button btnUpdateAll;
        private Button btnContributionData;
        private bool updateAllRunning;
        private Button btnDetail;
        private Button btnView;
        private ComboBox cmbSeason;
        private Button btnFriends;
        private Button btnHistory;
        private bool historyRunning = false;
        private bool historyStopRequested = false;
        private NumericUpDown numHistoryDelay;
        private readonly Dictionary<string, string> friends = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, StoredMatch> savedMatches = new Dictionary<string, StoredMatch>();
        private bool showingChampions = false;
        private readonly Dictionary<string, MatchDetailRecord> savedDetails = new Dictionary<string, MatchDetailRecord>();
        private string activePuuid = "";
        private static readonly string DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AramStats");
        private static readonly DateTime Season3Start = new DateTime(2026, 10, 21);
        private Dictionary<int, string> championNames = new Dictionary<int, string>();

        private string puuid = "";
        private string displayName = "";

        public Form1()
        {
            InitializeComponent();
            BuildInterface();
            Shown += async (sender, e) => await CheckForUpdatesAsync(false);
        }

        private void BuildInterface()
        {
            Text = "AramStats v" + AppVersion + " - 증바람 전적 분석기";
            Size = new Size(1280, 800);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1024, 680);
            Controls.Clear();

            mainNavigation = new TabControl { Dock = DockStyle.Fill };
            TabPage statsPage = new TabPage("통계");
            TabPage managementPage = new TabPage("데이터 관리") { Padding = new Padding(14) };
            dashboardHost = new Panel { Dock = DockStyle.Fill };
            statsPage.Controls.Add(dashboardHost);
            var openData = new Button
            {
                Text = "데이터 관리에서 전적 불러오기",
                AutoSize = true,
                Anchor = AnchorStyles.None,
                Padding = new Padding(14, 6, 14, 6)
            };
            openData.Click += (_, _) => mainNavigation.SelectedTab = managementPage;
            var emptyDashboard = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1 };
            emptyDashboard.Controls.Add(openData);
            dashboardHost.Controls.Add(emptyDashboard);

            var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(4) };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 174));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 125));
            managementPage.Controls.Add(content);
            content.Controls.Add(new Label
            {
                Text = "데이터 관리",
                Dock = DockStyle.Fill,
                Font = new Font("맑은 고딕", 18, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);

            var quick = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(8) };
            quick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
            quick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            quick.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            quick.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            quick.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            quick.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            quick.RowCount = 4;
            quick.BackColor = Color.FromArgb(242, 246, 251);
            lblAccount = new Label { Text = "계정: 연결되지 않음", Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            lblCount = new Label { Text = "저장된 경기: 0판", Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            lblStatus = new Label
            {
                Text = "상태: 롤 클라이언트를 실행해 주세요.",
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            quick.Controls.Add(lblAccount, 0, 0);
            quick.Controls.Add(lblCount, 0, 1);
            quick.Controls.Add(lblStatus, 0, 2);
            btnUpdateAll = new Button
            {
                Text = "전적 업데이트",
                Dock = DockStyle.Fill,
                Font = new Font("맑은 고딕", 12, FontStyle.Bold),
                Margin = new Padding(8)
            };
            quick.Controls.Add(btnUpdateAll, 1, 0);
            quick.SetRowSpan(btnUpdateAll, 3);
            btnUpdateAll.Click += async (sender, e) => await UpdateAllAsync();
            quick.Controls.Add(BuildGroupSyncPanel(), 0, 3);
            quick.SetColumnSpan(quick.GetControlFromPosition(0, 3)!, 2);
            content.Controls.Add(quick, 0, 1);

            var advancedToggle = new Button { Text = "▶ 고급 관리 펼치기", Dock = DockStyle.Left, Width = 180 };
            content.Controls.Add(advancedToggle, 0, 2);

            var advanced = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Visible = false };
            content.Controls.Add(advanced, 0, 3);
            var controls = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(3) };
            advanced.Controls.Add(controls);
            advancedToggle.Click += (sender, e) => {
                advanced.Visible = !advanced.Visible;
                advancedToggle.Text = advanced.Visible ? "▼ 고급 관리 접기" : "▶ 고급 관리 펼치기";
            };

            btnConnect = new Button { Text = "계정 다시 연결", Width = 125, Height = 32 };
            btnConnect.Click += async (sender, e) => await ConnectAccountAsync();
            btnMatches = new Button { Text = "경기 목록만 조회", Width = 145, Height = 32, Enabled = false };
            btnMatches.Click += async (sender, e) => await FetchMatchesAsync();
            btnImport = new Button { Text = "JSON 가져오기", Width = 125, Height = 32 };
            btnImport.Click += BtnImport_Click;
            btnDetail = new Button { Text = "선택 경기 10명 조회", Width = 160, Height = 32 };
            btnDetail.Click += BtnDetail_Click;
            btnContributionData = new Button { Text = "기여도 데이터 보충", Width = 155, Height = 32 };
            btnContributionData.Click += async (sender, e) => await FillContributionAsync(btnContributionData);
            btnHistory = new Button { Text = "과거 전체 수집", Width = 130, Height = 32 };
            btnHistory.Click += BtnHistory_Click;
            numHistoryDelay = new NumericUpDown
            {
                Minimum = 200,
                Maximum = 3000,
                Increment = 100,
                Value = 500,
                Width = 78,
                Height = 30
            };
            new ToolTip().SetToolTip(numHistoryDelay, "상세 요청 간격 (밀리초)");
            btnView = new Button { Text = "챔피언 통계 보기", Width = 145, Height = 32 };
            btnView.Click += (sender, e) => { showingChampions = !showingChampions; RefreshView(); };
            cmbSeason = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            cmbSeason.Items.AddRange(new object[] { "전체 시즌", "시즌 2", "시즌 3" });
            cmbSeason.SelectedIndex = 0;
            cmbSeason.SelectedIndexChanged += (sender, e) => RefreshView();
            btnFriends = new Button { Text = "친구 관리", Width = 110, Height = 32 };
            btnFriends.Click += (sender, e) => ShowFriendsDialog();
            var btnDashboard = new Button { Text = "통계로 이동", Width = 120, Height = 32 };
            btnDashboard.Click += (sender, e) => { OpenEmbeddedDashboard(); mainNavigation.SelectedIndex = 0; };
            btnCheckUpdate = new Button { Text = "프로그램 업데이트 확인", Width = 170, Height = 32 };
            btnCheckUpdate.Click += async (sender, e) => await CheckForUpdatesAsync(true);
            var btnCloudExport = new Button { Text = "클라우드 내보내기/업로드", Width = 190, Height = 32 };
            btnCloudExport.Click += async (sender, e) => await ExportOrUploadCloudMatchesAsync(btnCloudExport);
            controls.Controls.AddRange(new Control[] { btnCheckUpdate, btnConnect, btnMatches, btnImport, btnDetail,
                btnContributionData, btnHistory, numHistoryDelay, btnView, cmbSeason, btnFriends, btnDashboard, btnCloudExport });

            dgvMatches = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false
            };
            dgvMatches.Columns.Add("Date", "경기 날짜");
            dgvMatches.Columns.Add("Champion", "챔피언 ID");
            dgvMatches.Columns.Add("Result", "승패");
            dgvMatches.Columns.Add("KDA", "K / D / A");
            dgvMatches.Columns.Add("Mode", "게임 모드");
            dgvMatches.Columns.Add("Queue", "Queue ID");
            dgvMatches.Columns.Add("GameId", "경기 ID");
            advanced.Controls.Add(dgvMatches);
            dgvMatches.BringToFront();
            advanced.Resize += (sender, e) => {
                dgvMatches.Top = controls.Bottom + 8;
                dgvMatches.Height = Math.Max(60, advanced.ClientSize.Height - dgvMatches.Top);
            };
            txtResult = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill
            };
            var logArea = new GroupBox { Text = "작업 결과 / 오류 메시지", Dock = DockStyle.Fill, Padding = new Padding(8) };
            logArea.Controls.Add(txtResult);
            content.Controls.Add(logArea, 0, 4);

            mainNavigation.TabPages.Add(statsPage);
            mainNavigation.TabPages.Add(managementPage);
            Controls.Add(mainNavigation);
            InitializeGroupSync();
            mainNavigation.SelectedTab = statsPage;
            ApplyDashboardTheme(this);
        }

        // GitHub Releases에서 무설치 EXE를 다운로드한다. 기존 실행 파일은 별도 프로세스에서 교체한다.
        private async Task CheckForUpdatesAsync(bool manual)
        {
            if (checkingUpdate) return;
            checkingUpdate = true;
            if (btnCheckUpdate != null) btnCheckUpdate.Enabled = false;
            try
            {
                string runningExe = Environment.ProcessPath ?? Application.ExecutablePath;
                if (!string.Equals(Path.GetFileName(runningExe), PortableAssetName, StringComparison.OrdinalIgnoreCase))
                {
                    if (manual) MessageBox.Show("무설치 업데이트는 AramStats.exe로 실행했을 때 사용할 수 있습니다.", "업데이트 확인");
                    return;
                }
                using HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("AramStats-Updater/1.0");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                using HttpResponseMessage response = await client.GetAsync(ReleaseApi);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    if (manual) MessageBox.Show("아직 배포된 정식 버전이 없습니다.", "업데이트 확인");
                    return;
                }
                response.EnsureSuccessStatusCode();
                using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                JsonElement release = document.RootElement;
                string tag = JsonString(release, "tag_name").Trim().TrimStart('v', 'V');
                if (!Version.TryParse(tag, out Version latest) || !Version.TryParse(AppVersion, out Version current))
                    throw new Exception("릴리스 버전 형식이 올바르지 않습니다. 예: v1.0.1");
                if (latest <= current)
                {
                    if (manual) MessageBox.Show("최신 버전입니다. (v" + AppVersion + ")", "업데이트 확인");
                    return;
                }
                string assetUrl = null;
                string expectedSha256 = null;
                if (release.TryGetProperty("assets", out JsonElement assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement asset in assets.EnumerateArray())
                    {
                        if (JsonString(asset, "name") == PortableAssetName)
                        {
                            assetUrl = JsonString(asset, "browser_download_url");
                            string digest = JsonString(asset, "digest");
                            if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                                expectedSha256 = digest.Substring(7);
                            break;
                        }
                    }
                }
                if (string.IsNullOrWhiteSpace(assetUrl))
                {
                    if (manual) MessageBox.Show("v" + latest + " 릴리스에 " + PortableAssetName +
                        " 파일이 없습니다. 무설치 EXE를 릴리스에 등록해 주세요.", "업데이트 확인");
                    return;
                }
                if (!Uri.TryCreate(assetUrl, UriKind.Absolute, out Uri assetUri) ||
                    assetUri.Scheme != Uri.UriSchemeHttps ||
                    !assetUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                    !assetUri.AbsolutePath.StartsWith("/yeepsent/AramStats/releases/download/", StringComparison.OrdinalIgnoreCase))
                    throw new Exception("공식 GitHub 릴리스 주소가 아닙니다.");
                if (MessageBox.Show("새 버전 v" + latest + "이 있습니다. (현재 v" + AppVersion +
                    ")\r\n새 실행 파일을 다운로드해 교체할까요?\r\n기존 전적 데이터는 유지됩니다.",
                    "AramStats 업데이트", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
                string downloadDir = Path.Combine(Path.GetTempPath(), "AramStats", "Updates", latest.ToString());
                Directory.CreateDirectory(downloadDir);
                string stagedExe = Path.Combine(downloadDir, PortableAssetName);
                string partialPath = stagedExe + ".download";
                using (HttpClient downloadClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
                {
                    downloadClient.DefaultRequestHeaders.UserAgent.ParseAdd("AramStats-Updater/1.0");
                    using HttpResponseMessage fileResponse = await downloadClient.GetAsync(assetUri, HttpCompletionOption.ResponseHeadersRead);
                    fileResponse.EnsureSuccessStatusCode();
                    using Stream input = await fileResponse.Content.ReadAsStreamAsync();
                    using FileStream output = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await input.CopyToAsync(output);
                }
                if (new FileInfo(partialPath).Length < 1024 * 1024)
                    throw new Exception("다운로드한 EXE 파일이 비정상적으로 작습니다.");
                if (!string.IsNullOrWhiteSpace(expectedSha256))
                {
                    using var sha = System.Security.Cryptography.SHA256.Create();
                    using var file = File.OpenRead(partialPath);
                    string actual = Convert.ToHexString(sha.ComputeHash(file));
                    if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
                        throw new Exception("다운로드 파일의 SHA-256 검증에 실패했습니다.");
                }
                File.Move(partialPath, stagedExe, true);
                // 업데이트 도우미는 실행 파일과 독립된 PowerShell 프로세스에서 실행한다.
                // 현재 프로세스가 종료될 때까지 기다린 후 백업 → 교체 → 재실행한다.
                string scriptPath = Path.Combine(downloadDir, "apply-update.ps1");
                string script = @"
param([int]$TargetPid, [string]$Source, [string]$Target)
$ErrorActionPreference = 'Stop'
$backup = $Target + '.previous'
try {
    try { Wait-Process -Id $TargetPid -Timeout 60 -ErrorAction SilentlyContinue } catch {}
    if (Get-Process -Id $TargetPid -ErrorAction SilentlyContinue) { throw '프로그램 종료 대기 시간이 초과됐습니다.' }
    if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Force }
    Move-Item -LiteralPath $Target -Destination $backup -ErrorAction Stop
    try {
        Copy-Item -LiteralPath $Source -Destination $Target -ErrorAction Stop
        Start-Process -FilePath $Target -WorkingDirectory (Split-Path -Parent $Target) -ErrorAction Stop
    } catch {
        if (Test-Path -LiteralPath $Target) { Remove-Item -LiteralPath $Target -Force }
        Move-Item -LiteralPath $backup -Destination $Target -ErrorAction Stop
        Start-Process -FilePath $Target -WorkingDirectory (Split-Path -Parent $Target)
        throw
    }
} catch {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show('업데이트 실패: ' + $_.Exception.Message, 'AramStats') | Out-Null
}
";
                File.WriteAllText(scriptPath, script, new UTF8Encoding(true));
                string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
                var psi = new ProcessStartInfo("powershell.exe")
                {
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + Quote(scriptPath) +
                        " -TargetPid " + Process.GetCurrentProcess().Id + " -Source " + Quote(stagedExe) +
                        " -Target " + Quote(runningExe),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                if (Process.Start(psi) == null) throw new Exception("업데이트 도우미를 실행하지 못했습니다.");
                Application.Exit();
            }
            catch (Exception ex)
            {
                if (manual) MessageBox.Show("업데이트 확인에 실패했습니다.\r\n" + ex.Message,
                    "업데이트 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                checkingUpdate = false;
                if (btnCheckUpdate != null && !btnCheckUpdate.IsDisposed) btnCheckUpdate.Enabled = true;
            }
        }

        private async Task UpdateAllAsync()
        {
            if (updateAllRunning) return;
            updateAllRunning = true;
            btnUpdateAll.Enabled = false;
            btnUpdateAll.Text = "업데이트 중...";
            try
            {
                // 순서를 보장한다. 기존 상세 저장·오류 처리는 각 단계의 로직을 그대로 사용한다.
                if (!await ConnectAccountAsync()) return;
                mainNavigation.SelectedIndex = 1;
                if (!await FetchMatchesAsync()) return;
                if (!await FillContributionAsync(btnContributionData)) return;
                lblStatus.Text = "상태: 전적 업데이트 완료";
                lblCount.Text = "저장된 경기: " + savedMatches.Count + "판 / 상세 " + savedDetails.Count + "판";
                OpenEmbeddedDashboard();
                mainNavigation.SelectedIndex = 0;
                MessageBox.Show("전적 업데이트가 완료됐습니다.", "AramStats");
            }
            finally
            {
                updateAllRunning = false;
                btnUpdateAll.Enabled = true;
                btnUpdateAll.Text = "전적 업데이트";
            }
        }

        private async Task<bool> ConnectAccountAsync()
        {
            btnConnect.Enabled = false;
            btnMatches.Enabled = false;

            try
            {
                lblStatus.Text = "상태: 계정 정보 확인 중...";

                LcuInfo info = await GetLcuInfoAsync();

                if (info == null)
                    throw new Exception("LCU 접속 정보를 찾지 못했습니다.");

                using HttpClient client = CreateClient(info);

                HttpResponseMessage response = await client.GetAsync(
                    "https://127.0.0.1:" + info.Port +
                    "/lol-summoner/v1/current-summoner"
                );

                string body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    throw new Exception("계정 조회 실패: " + (int)response.StatusCode);

                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                puuid = GetJsonValue(root, "puuid");
                displayName = GetJsonValue(root, "displayName");

                string level = GetJsonValue(root, "summonerLevel");

                if (string.IsNullOrWhiteSpace(puuid) ||
                    puuid == "확인 불가")
                {
                    throw new Exception("PUUID를 가져오지 못했습니다.");
                }

                lblAccount.Text =
                    "계정: " + displayName + " | 레벨: " + level;

                lblStatus.Text = "상태: 계정 조회 성공";
                lblCount.Text = "경기 수: 아직 조회하지 않음";

                btnMatches.Enabled = true;
                if (activePuuid != puuid)
                {
                    activePuuid = puuid;
                    savedMatches.Clear();
                    savedDetails.Clear();
                    LoadStoredMatches();
                    LoadStoredDetails();
                    friends.Clear();
                    LoadFriends();
                }
                await LoadChampionNamesAsync();
                RefreshView();
                OpenEmbeddedDashboard();
                mainNavigation.SelectedIndex = 0;

                txtResult.Text = "계정 정보를 정상적으로 가져왔습니다.";
                return true;
            }
            catch (Exception ex)
            {
                lblStatus.Text = "상태: 오류";
                txtResult.Text = ex.Message;

                MessageBox.Show(
                    ex.Message,
                    "오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return false;
            }
            finally
            {
                btnConnect.Enabled = true;
            }
        }

        private async Task<bool> FetchMatchesAsync()
        {
            btnMatches.Enabled = false;
            btnConnect.Enabled = false;

            dgvMatches.Rows.Clear();
            txtResult.Clear();

            try
            {
                lblStatus.Text = "상태: 경기 목록 조회 중...";
                lblCount.Text = "경기 수: 확인 중";

                LcuInfo info = await GetLcuInfoAsync();

                if (info == null)
                    throw new Exception("LCU 접속 정보를 찾지 못했습니다.");

                using HttpClient client = CreateClient(info);
                currentLcuPort = info.Port;

                const int maximumMatches = 5000;

                string encodedPuuid = Uri.EscapeDataString(puuid);

                MatchFetchResult currentResult = await FetchMatchListAsync(
                    client,
                    "/lol-match-history/v1/products/lol/current-summoner/matches",
                    "현재 소환사"
                );

                lblStatus.Text = "상태: PUUID 경기 목록 조회 중...";

                MatchFetchResult puuidResult = await FetchMatchListAsync(
                    client,
                    "/lol-match-history/v1/products/lol/" + encodedPuuid + "/matches",
                    "PUUID"
                );

                MatchFetchResult bestResult =
                    puuidResult.Games.Count >= currentResult.Games.Count
                        ? puuidResult
                        : currentResult;

                int merged = MergeLcuGames(bestResult.Games);
                SaveStoredMatches();
                RefreshView();

                // 2400 모드의 신규 경기 상세 정보만 수집한다. 실패한 경기는 다음 조회 때 재시도한다.
                int detailAdded = await SyncMatchDetailsAsync(client);
                RefreshView();

                lblCount.Text =
                    "현재 소환사 조회: " + currentResult.Games.Count +
                    " | PUUID 조회: " + puuidResult.Games.Count +
                    " | 새로 저장: " + merged + " | 누적: " + savedMatches.Count +
                    " | 10명 상세 신규: " + detailAdded + " | 상세 누적: " + savedDetails.Count;

                lblStatus.Text = "상태: 경기 목록 조회 완료";

                string summary =
                    "[현재 소환사 경기 목록]\r\n" +
                    currentResult.ToSummary() + "\r\n" +
                    "[PUUID 경기 목록]\r\n" +
                    puuidResult.ToSummary() + "\r\n";

                if (bestResult.Games.Count >= maximumMatches)
                {
                    summary +=
                        "안전 제한: 최대 " + maximumMatches +
                        "경기까지만 조회했습니다.\r\n\r\n";
                }

                summary += "[10명 상세 자동 수집]\r\n" +
                    "이번 신규 저장: " + detailAdded + "경기\r\n" +
                    "상세 누적 저장: " + savedDetails.Count + "경기\r\n" +
                    "한 번에 최대 30경기 상세 조회를 시도합니다.\r\n" +
                    "저장 경로: " + DetailStorageFile() + "\r\n";

                txtResult.Text = summary;
                txtResult.SelectionStart = 0;
                txtResult.ScrollToCaret();
                txtResult.BringToFront();

                if (!updateAllRunning) MessageBox.Show("경기 목록 및 상세 자동 수집이 완료됐습니다.", "수집 완료");
                return true;
            }
            catch (Exception ex)
            {
                lblStatus.Text = "상태: 경기 조회 실패";
                txtResult.Text = ex.Message;

                MessageBox.Show(
                    ex.Message,
                    "경기 조회 오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return false;
            }
            finally
            {
                btnMatches.Enabled = !string.IsNullOrEmpty(puuid);
                btnConnect.Enabled = true;
            }
        }

        private async Task<MatchFetchResult> FetchMatchListAsync(
            HttpClient client,
            string endpoint,
            string label)
        {
            const int pageSize = 100;
            const int maximumMatches = 5000;
            const int maximumPages = 100;

            MatchFetchResult result = new MatchFetchResult();
            HashSet<string> gameIds = new HashSet<string>();

            int beginIndex = 0;

            for (int page = 0; page < maximumPages; page++)
            {
                if (beginIndex >= maximumMatches)
                    break;

                int endIndex = Math.Min(
                    beginIndex + pageSize - 1,
                    maximumMatches - 1
                );

                string url =
                    "https://127.0.0.1:" + GetCurrentPort(client) +
                    endpoint +
                    "?begIndex=" + beginIndex +
                    "&endIndex=" + endIndex;

                HttpResponseMessage response = await client.GetAsync(url);
                string body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    result.Error =
                        "HTTP " + (int)response.StatusCode + " " +
                        response.ReasonPhrase + "\r\n" +
                        body;
                    break;
                }

                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                if (!root.TryGetProperty("games", out JsonElement gamesRoot))
                {
                    result.Error = "응답에 games 항목이 없습니다.";
                    break;
                }

                if (gamesRoot.TryGetProperty("gameCount", out JsonElement countElement) &&
                    countElement.TryGetInt32(out int count))
                {
                    result.TotalCount = count;
                }

                if (!gamesRoot.TryGetProperty("games", out JsonElement gamesArray) ||
                    gamesArray.ValueKind != JsonValueKind.Array)
                {
                    result.Error = "경기 데이터 배열을 찾지 못했습니다.";
                    break;
                }

                int pageCount = gamesArray.GetArrayLength();
                int newCount = 0;

                foreach (JsonElement game in gamesArray.EnumerateArray())
                {
                    string gameId = GetJsonValue(game, "gameId");

                    if (gameId == "확인 불가")
                        gameId = game.GetRawText();

                    if (gameIds.Add(gameId))
                    {
                        result.Games.Add(game.Clone());
                        newCount++;
                    }
                }

                result.Pages++;

                lblCount.Text =
                    label + " 조회 중: " + result.Games.Count +
                    " / 서버 전체: " + result.TotalCount;

                lblStatus.Text =
                    "상태: " + label + " 경기 수집 중...";

                Application.DoEvents();

                if (pageCount == 0 || newCount == 0)
                    break;

                if (result.TotalCount > 0 &&
                    result.Games.Count >= result.TotalCount)
                    break;

                int nextBeginIndex = beginIndex + pageCount;

                if (gamesRoot.TryGetProperty("gameIndexEnd", out JsonElement endElement) &&
                    endElement.TryGetInt32(out int returnedEndIndex) &&
                    returnedEndIndex >= beginIndex)
                {
                    nextBeginIndex = returnedEndIndex + 1;
                }

                if (nextBeginIndex <= beginIndex)
                    break;

                beginIndex = nextBeginIndex;
            }

            return result;
        }

        private string GetCurrentPort(HttpClient client)
        {
            return currentLcuPort;
        }

        private string currentLcuPort = "";

        private async Task<string> DiagnosePageAsync(
            HttpClient client,
            string endpoint,
            string query)
        {
            try
            {
                string url = "https://127.0.0.1:" + currentLcuPort +
                    endpoint + "?" + query;

                using HttpResponseMessage response = await client.GetAsync(url);
                string body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return query + " => HTTP " + (int)response.StatusCode + "\\r\\n";

                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                if (!root.TryGetProperty("games", out JsonElement data) ||
                    !data.TryGetProperty("games", out JsonElement games) ||
                    games.ValueKind != JsonValueKind.Array)
                    return query + " => 예상하지 못한 응답 구조\\r\\n";

                string begin = GetJsonValue(data, "gameIndexBegin");
                string end = GetJsonValue(data, "gameIndexEnd");
                string total = GetJsonValue(data, "gameCount");
                string firstId = games.GetArrayLength() > 0
                    ? GetJsonValue(games[0], "gameId") : "없음";
                string lastId = games.GetArrayLength() > 0
                    ? GetJsonValue(games[games.GetArrayLength() - 1], "gameId") : "없음";

                return query + " => " + games.GetArrayLength() +
                    "경기 | 인덱스 " + begin + "~" + end +
                    " | 전체 " + total +
                    " | 첫 ID " + firstId +
                    " | 마지막 ID " + lastId + "\\r\\n";
            }
            catch (Exception ex)
            {
                return query + " => 오류: " + ex.Message + "\\r\\n";
            }
        }

        private async Task<string> TestMatchRangeAsync(
            HttpClient client,
            string endpoint,
            int beginIndex,
            int endIndex)
        {
            string label = beginIndex + "~" + endIndex;

            try
            {
                string url =
                    "https://127.0.0.1:" + currentLcuPort + endpoint +
                    "?begIndex=" + beginIndex + "&endIndex=" + endIndex;

                using HttpResponseMessage response = await client.GetAsync(url);
                string body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return label + ": HTTP " + (int)response.StatusCode + "\r\n";

                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                if (!root.TryGetProperty("games", out JsonElement wrapper) ||
                    !wrapper.TryGetProperty("games", out JsonElement games) ||
                    games.ValueKind != JsonValueKind.Array)
                {
                    return label + ": 경기 배열 없음\r\n";
                }

                int count = games.GetArrayLength();
                string firstId = count > 0 ? GetJsonValue(games[0], "gameId") : "없음";
                string lastId = count > 0 ? GetJsonValue(games[count - 1], "gameId") : "없음";
                string firstDate = count > 0 ? GetJsonValue(games[0], "gameCreationDate") : "없음";
                string lastDate = count > 0 ? GetJsonValue(games[count - 1], "gameCreationDate") : "없음";

                return label + ": " + count + "경기" +
                    " | 첫 ID: " + firstId +
                    " | 마지막 ID: " + lastId +
                    " | 첫 날짜: " + firstDate +
                    " | 마지막 날짜: " + lastDate + "\r\n";
            }
            catch (Exception ex)
            {
                return label + ": 오류 - " + ex.Message + "\r\n";
            }
        }

        private class MatchFetchResult
        {
            public List<JsonElement> Games { get; set; } =
                new List<JsonElement>();

            public int TotalCount { get; set; }

            public int Pages { get; set; }

            public string Error { get; set; } = "";

            public string ToSummary()
            {
                return
                    "서버 전체 경기 수: " + TotalCount + "\r\n" +
                    "가져온 경기 수: " + Games.Count + "\r\n" +
                    "요청 페이지 수: " + Pages + "\r\n" +
                    (string.IsNullOrEmpty(Error)
                        ? ""
                        : "오류: " + Error + "\r\n");
            }
        }

        // 경기 ID를 이용해 LCU에서 10명 참가자 정보를 직접 확인한다.
        // 이 단계에서는 조회 결과를 서버나 로컬 파일에 저장하지 않는다.
        private async void BtnDetail_Click(object sender, EventArgs e)
        {
            if (showingChampions || dgvMatches.SelectedRows.Count == 0 ||
                !dgvMatches.Columns.Contains("GameId"))
            {
                MessageBox.Show("챔피언 통계 화면이 아닌 경기 목록에서 한 경기를 선택해 주세요.");
                return;
            }
            string gid = Convert.ToString(dgvMatches.SelectedRows[0].Cells["GameId"].Value) ?? "";
            if (!Regex.IsMatch(gid, @"^\d+$"))
            {
                MessageBox.Show("선택한 경기의 ID가 올바르지 않습니다.");
                return;
            }
            btnDetail.Enabled = false;
            try
            {
                lblStatus.Text = "상태: 경기 상세 조회 중...";
                LcuInfo info = await GetLcuInfoAsync();
                if (info == null) throw new Exception("롤 클라이언트를 먼저 실행해 주세요.");
                using HttpClient client = CreateClient(info);
                using HttpResponseMessage response = await client.GetAsync(
                    "https://127.0.0.1:" + info.Port + "/lol-match-history/v1/games/" + gid);
                string body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    throw new Exception("경기 상세 API: HTTP " + (int)response.StatusCode +
                        "\r\n오래된 경기는 상세 조회가 제한될 수 있습니다.");
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("game", out JsonElement nested) && nested.ValueKind == JsonValueKind.Object)
                    root = nested;
                if (!root.TryGetProperty("participants", out JsonElement participants) ||
                    participants.ValueKind != JsonValueKind.Array)
                    throw new Exception("응답에 participants 배열이 없습니다. 10명 데이터 확보에 실패했습니다.");

                // LCU 상세 응답은 참가자 정보와 신원 정보를 별도 배열로 반환할 수 있다.
                var identityByParticipantId = new Dictionary<int, JsonElement>();
                int identityCount = 0;
                if (root.TryGetProperty("participantIdentities", out JsonElement identities) &&
                    identities.ValueKind == JsonValueKind.Array)
                {
                    identityCount = identities.GetArrayLength();
                    foreach (JsonElement item in identities.EnumerateArray())
                    {
                        int participantId = JsonInt(item, "participantId", -1);
                        if (participantId >= 0 && item.TryGetProperty("player", out JsonElement identityPlayer) &&
                            identityPlayer.ValueKind == JsonValueKind.Object)
                            identityByParticipantId[participantId] = identityPlayer;
                    }
                }

                var lines = new List<string>();
                lines.Add("경기 ID: " + gid);
                lines.Add("참가자 수: " + participants.GetArrayLength() + "명");
                lines.Add("participantIdentities 수: " + identityCount + "명");
                lines.Add("※ 동일 팀 ID의 참가자들이 같은 팀입니다.");
                lines.Add("");
                int identified = 0;
                foreach (JsonElement player in participants.EnumerateArray())
                {
                    JsonElement stats = player.TryGetProperty("stats", out JsonElement st) ? st : player;
                    JsonElement identity = player.TryGetProperty("player", out JsonElement pl) ? pl : player;
                    int participantId = JsonInt(player, "participantId", -1);
                    if (identityByParticipantId.TryGetValue(participantId, out JsonElement matchedIdentity))
                        identity = matchedIdentity;

                    string p = JsonString(player, "puuid", JsonString(stats, "puuid", JsonString(identity, "puuid")));
                    string name = JsonString(identity, "gameName", JsonString(identity, "summonerName",
                        JsonString(player, "summonerName", "이름 없음")));
                    string tag = JsonString(identity, "tagLine");
                    if (!string.IsNullOrWhiteSpace(tag)) name += "#" + tag;
                    if (!string.IsNullOrWhiteSpace(p)) identified++;
                    string shortPuuid = p.Length > 12 ? p.Substring(0, 12) + "..." : (p.Length == 0 ? "없음" : p);
                    int championId = JsonInt(player, "championId", JsonInt(stats, "championId"));
                    lines.Add("팀 " + JsonString(player, "teamId", JsonString(stats, "teamId", "?")) +
                        " | " + name + " | " + ChampionName(championId) +
                        " | " + JsonInt(stats, "kills") + "/" + JsonInt(stats, "deaths") +
                        "/" + JsonInt(stats, "assists") + " | PUUID " + shortPuuid);
                }
                lines.Add("");
                lines.Add("PUUID 확인: " + identified + "/" + participants.GetArrayLength() + "명");
                if (identified == 0)
                {
                    lines.Add("※ 원본 응답에 식별 필드가 없는지 확인하기 위한 필드 목록:");
                    lines.Add("최상위: " + string.Join(", ", root.EnumerateObject().Select(x => x.Name)));
                    if (participants.GetArrayLength() > 0)
                        lines.Add("참가자 필드: " + string.Join(", ", participants[0].EnumerateObject().Select(x => x.Name)));
                    if (identityCount > 0)
                        lines.Add("신원 필드: " + string.Join(", ", identities[0].EnumerateObject().Select(x => x.Name)));
                }
                lines.Add("※ 전체 PUUID는 화면에 표시하지 않습니다.");
                string report = string.Join("\r\n", lines);
                txtResult.Text = report;
                using Form detail = new Form
                {
                    Text = "경기 상세 참가자 검증 - " + gid,
                    Size = new Size(800, 510),
                    StartPosition = FormStartPosition.CenterParent
                };
                TextBox output = new TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Both,
                    WordWrap = false,
                    Dock = DockStyle.Fill,
                    Font = new Font("Consolas", 10),
                    Text = report
                };
                detail.Controls.Add(output);
                lblStatus.Text = "상태: 경기 상세 조회 성공 (" + participants.GetArrayLength() + "명)";
                detail.ShowDialog(this);
            }
            catch (Exception ex)
            {
                lblStatus.Text = "상태: 경기 상세 조회 실패";
                txtResult.Text = ex.Message;
                MessageBox.Show(ex.Message, "상세 조회 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { btnDetail.Enabled = true; }
        }

        // 상세 정보는 개인 전적 파일과 분리해 저장한다. PUUID는 친구 식별을 위해 원본 그대로 보관한다.
        private class FriendEntry
        {
            public string Puuid { get; set; } = "";
            public string RiotId { get; set; } = "";
        }

        private string FriendsFile()
        {
            string safe = Regex.Replace(activePuuid, @"[^a-zA-Z0-9_-]", "_");
            return Path.Combine(DataDirectory, "friends_" + safe + ".json");
        }

        private void LoadFriends()
        {
            if (string.IsNullOrWhiteSpace(activePuuid) || !File.Exists(FriendsFile())) return;
            try
            {
                var items = JsonSerializer.Deserialize<List<FriendEntry>>(File.ReadAllText(FriendsFile()));
                foreach (FriendEntry item in items ?? new List<FriendEntry>())
                    if (!string.IsNullOrWhiteSpace(item.Puuid) && !string.IsNullOrWhiteSpace(item.RiotId))
                        friends[item.Puuid] = item.RiotId;
            }
            catch (Exception ex) { MessageBox.Show("친구 목록 읽기 실패: " + ex.Message); }
        }

        private void SaveFriends()
        {
            Directory.CreateDirectory(DataDirectory);
            string path = FriendsFile();
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(friends.Select(x => new FriendEntry
            { Puuid = x.Key, RiotId = x.Value }).ToList(), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, true);
        }

        private void ShowFriendsDialog()
        {
            if (string.IsNullOrWhiteSpace(activePuuid))
            {
                MessageBox.Show("먼저 계정 정보를 조회하거나 JSON을 가져와 주세요.");
                return;
            }
            using Form dialog = new Form
            {
                Text = "친구 등록 및 전적 분석",
                Size = new Size(900, 650),
                MinimumSize = new Size(700, 480),
                StartPosition = FormStartPosition.CenterParent
            };
            TextBox input = new TextBox
            {
                Left = 12,
                Top = 12,
                Width = 260,
                PlaceholderText = "라이엇 ID (이름#태그)"
            };
            Button add = new Button { Text = "친구 등록", Left = 280, Top = 10, Width = 105, Height = 28 };
            Button remove = new Button { Text = "선택 삭제", Left = 392, Top = 10, Width = 105, Height = 28 };
            Button analyze = new Button { Text = "선택 친구 분석", Left = 504, Top = 10, Width = 125, Height = 28 };
            Button dashboard = new Button { Text = "개인 / 공동 통계", Left = 636, Top = 10, Width = 165, Height = 28 };
            ListBox friendList = new ListBox
            {
                Left = 12,
                Top = 50,
                Width = 280,
                Height = 520,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
            };
            TextBox output = new TextBox
            {
                Left = 302,
                Top = 50,
                Width = 565,
                Height = 520,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Font = new Font("Consolas", 10),
                WordWrap = false
            };
            Label info = new Label
            {
                Left = 12,
                Top = 580,
                Width = 840,
                Height = 28,
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right,
                Text = "현재 저장된 경기의 참가자 정보에서만 친구를 찾습니다. 동일한 라이엇 ID가 여러 PUUID에 연결되면 등록하지 않습니다."
            };
            void RefreshFriends()
            {
                friendList.Items.Clear();
                foreach (var item in friends.OrderBy(x => x.Value))
                    friendList.Items.Add(new FriendListItem { Puuid = item.Key, RiotId = item.Value });
            }
            add.Click += (sender, e) =>
            {
                string riotId = input.Text.Trim();
                if (!riotId.Contains('#') || riotId.StartsWith("#") || riotId.EndsWith("#"))
                { MessageBox.Show("이름#태그 형식으로 입력해 주세요."); return; }
                var matches = savedDetails.Values.SelectMany(x => x.Participants)
                    .Where(x => string.Equals(x.Name?.Trim(), riotId, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Puuid).Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (matches.Count == 0)
                { MessageBox.Show("저장된 경기에서 이 라이엇 ID를 찾지 못했습니다."); return; }
                if (matches.Count > 1)
                { MessageBox.Show("같은 라이엇 ID에 여러 PUUID가 발견됐습니다. 자동 등록하지 않습니다."); return; }
                if (string.Equals(matches[0], activePuuid, StringComparison.OrdinalIgnoreCase))
                { MessageBox.Show("현재 계정은 친구로 등록할 수 없습니다."); return; }
                friends[matches[0]] = riotId;
                SaveFriends(); RefreshFriends();
                output.Text = "등록 완료: " + DisplayRiotName(riotId) + "\r\n저장된 경기에서 PUUID를 확인했습니다.";
            };
            remove.Click += (sender, e) =>
            {
                if (!(friendList.SelectedItem is FriendListItem selected)) return;
                friends.Remove(selected.Puuid); SaveFriends(); RefreshFriends(); output.Clear();
            };
            analyze.Click += (sender, e) =>
            {
                if (!(friendList.SelectedItem is FriendListItem selected))
                { MessageBox.Show("친구를 선택해 주세요."); return; }
                output.Text = AnalyzeFriend(selected.Puuid, selected.RiotId);
            };
            friendList.DoubleClick += (sender, e) => analyze.PerformClick();
            dashboard.Click += (sender, e) => ShowFriendsDashboard();
            dialog.Controls.AddRange(new Control[] { input, add, remove, analyze, dashboard, friendList, output, info });
            RefreshFriends();
            ApplyDashboardTheme(dialog);
            dialog.ShowDialog(this);
            OpenEmbeddedDashboard();
        }

        // 본인과 등록된 친구의 시즌별 기록을 분석한다. 기존 저장 데이터는 변경하지 않는다.
        private sealed class ChampionStat
        {
            public int ChampionId;
            public int Games;
            public int Wins;
            public int Kills;
            public int Deaths;
            public int Assists;
            public double Rate => Games == 0 ? 0 : 100.0 * Wins / Games;
            public double Score => 100.0 * (Wins + 2.5) / (Games + 5.0);
        }

        private string DetailSeason(string gameId)
        {
            StoredMatch match = savedMatches.Values.FirstOrDefault(x => x.GameId == gameId);
            return match == null ? "미확인" : SeasonOf(match);
        }

        private static string DisplayRiotName(string riotId)
        {
            if (string.IsNullOrWhiteSpace(riotId)) return "이름 없음";
            int hash = riotId.IndexOf('#');
            return hash > 0 ? riotId.Substring(0, hash) : riotId;
        }

        private Dictionary<string, string> DashboardPeople()
        {
            var people = new Dictionary<string, string>(friends.ToDictionary(x => x.Key, x => DisplayRiotName(x.Value)), StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(activePuuid))
                people[activePuuid] = "나 (" + (string.IsNullOrWhiteSpace(displayName) ? "현재 계정" : DisplayRiotName(displayName)) + ")";
            return people;
        }

        private List<ChampionStat> GetChampionStats(string personId, string season)
        {
            return savedDetails.Values
                .Where(g => season == "전체 시즌" || DetailSeason(g.GameId) == season)
                .SelectMany(g => g.Participants.Where(p =>
                    string.Equals(p.Puuid, personId, StringComparison.OrdinalIgnoreCase)))
                .GroupBy(p => p.ChampionId)
                .Select(g => new ChampionStat
                {
                    ChampionId = g.Key,
                    Games = g.Count(),
                    Wins = g.Count(p => p.Win),
                    Kills = g.Sum(p => p.Kills),
                    Deaths = g.Sum(p => p.Deaths),
                    Assists = g.Sum(p => p.Assists)
                }).ToList();
        }

        // 자체 기여도: 동일 팀의 각 지표를 상대 비교한다. 공식 Riot 지표가 아니다.
        // 피해 35%, 킬 관여 15%, 피해 감소 20%, 회복 10%, 보호막 10%, CC 10%.
        // API가 제공하지 않는 지표는 해당 경기에서 제외하고 남은 가중치로 재정규화한다.
        private static Dictionary<string, int> CalculateTeamContributionRanks(MatchDetailRecord game, int teamId)
        {
            var team = game.Participants.Where(p => p.TeamId == teamId).ToList();
            if (team.Count != 5 || team.Any(p => !p.DamageToChampions.HasValue)) return null;
            var scores = team.ToDictionary(p => p.Puuid, p => 0.0, StringComparer.OrdinalIgnoreCase);
            var weights = team.ToDictionary(p => p.Puuid, p => 0.0, StringComparer.OrdinalIgnoreCase);
            void AddMetric(double weight, Func<MatchParticipantRecord, double?> get)
            {
                var values = team.Select(p => new { p.Puuid, Value = get(p) }).ToList();
                if (values.Any(x => !x.Value.HasValue)) return;
                double min = values.Min(x => x.Value.Value), max = values.Max(x => x.Value.Value);
                foreach (var v in values)
                {
                    double normalized = max == min ? 0.5 : (v.Value.Value - min) / (max - min);
                    scores[v.Puuid] += weight * normalized;
                    weights[v.Puuid] += weight;
                }
            }
            AddMetric(0.35, p => p.DamageToChampions);
            int teamKills = team.Sum(p => p.Kills);
            AddMetric(0.15, p => teamKills == 0 ? 0 : (double)(p.Kills + p.Assists) / teamKills);
            AddMetric(0.20, p => p.DamageMitigated);
            AddMetric(0.10, p => p.AllyHealing ?? p.Healing);
            AddMetric(0.10, p => p.AllyShielding);
            AddMetric(0.10, p => p.CrowdControlSeconds);
            var ordered = team.Select(p => new { p.Puuid, Score = weights[p.Puuid] > 0 ? scores[p.Puuid] / weights[p.Puuid] : 0 })
                .OrderByDescending(x => x.Score).ThenBy(x => x.Puuid).ToList();
            var ranks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ordered.Count; i++)
            {
                int rank = i > 0 && Math.Abs(ordered[i].Score - ordered[i - 1].Score) < 1e-9
                    ? ranks[ordered[i - 1].Puuid] : i + 1;
                ranks[ordered[i].Puuid] = rank;
            }
            return ranks;
        }

        private List<(string GameId, int Rank, int ChampionId, bool Win)> RecentContributionRanks(string personId, string season)
        {
            var matchesById = savedMatches.Values.GroupBy(m => m.GameId).ToDictionary(g => g.Key, g => g.First());
            var recent = savedDetails.Values
                .Where(g => matchesById.TryGetValue(g.GameId, out StoredMatch m) && m.QueueId == 2400 &&
                    (season == "전체 시즌" || SeasonOf(m) == season) &&
                    g.Participants.Any(p => string.Equals(p.Puuid, personId, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(g => matchesById[g.GameId].Timestamp).Take(20).ToList();
            var result = new List<(string GameId, int Rank, int ChampionId, bool Win)>();
            foreach (var game in recent)
            {
                var player = game.Participants.First(p => string.Equals(p.Puuid, personId, StringComparison.OrdinalIgnoreCase));
                var ranks = CalculateTeamContributionRanks(game, player.TeamId);
                if (ranks != null && ranks.TryGetValue(player.Puuid, out int rank))
                    result.Add((game.GameId, rank, player.ChampionId, player.Win));
            }
            return result;
        }

        private void OpenEmbeddedDashboard()
        {
            if (dashboardHost == null || string.IsNullOrWhiteSpace(activePuuid)) return;
            ShowFriendsDashboard(dashboardPersonId, dashboardTab, true);
        }

        private void ShowFriendsDashboard(string? initialPerson = null, int initialTab = 1, bool embedded = false)
        {
            if (string.IsNullOrWhiteSpace(activePuuid))
            { MessageBox.Show("먼저 계정 정보를 조회해 주세요."); return; }
            using Form? dialog = embedded ? null : new Form
            {
                Text = "AramStats - 개인 / 공동 / 챔피언 랭킹",
                Size = new Size(1180, 800),
                MinimumSize = new Size(900, 600),
                StartPosition = FormStartPosition.CenterParent
            };
            var people = DashboardPeople();
            TabControl tabs = new TabControl { Dock = DockStyle.Fill };
            TabPage personal = new TabPage("개인 전적");
            TabPage group = new TabPage("멤버 비교");
            TabPage champions = new TabPage("챔피언 통계");
            tabs.TabPages.AddRange(new[] { personal, group, champions });
            var frame = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(12) };
            frame.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            frame.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            var season = new ComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "시즌" };
            season.Items.AddRange(new object[] { "시즌 3", "시즌 2", "전체 시즌" });
            season.SelectedItem = dashboardSeason;
            filters.Controls.Add(new Label { Text = "시즌", AutoSize = true, Margin = new Padding(4, 7, 8, 4) });
            filters.Controls.Add(season);
            frame.Controls.Add(filters, 0, 0);
            frame.Controls.Add(tabs, 0, 1);
            if (embedded)
            {
                while (dashboardHost.Controls.Count > 0)
                    dashboardHost.Controls[0].Dispose();
                dashboardHost.Controls.Add(frame);
            }
            else dialog!.Controls.Add(frame);

            ComboBox MakePerson()
            {
                ComboBox c = new ComboBox { Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
                foreach (var item in people.OrderByDescending(x => x.Key == activePuuid).ThenBy(x => x.Value))
                    c.Items.Add(new FriendListItem { Puuid = item.Key, RiotId = item.Value });
                if (c.Items.Count > 0) c.SelectedIndex = 0;
                return c;
            }
            void AddTop(TabPage page, FlowLayoutPanel top)
            {
                top.Dock = DockStyle.Top; top.Height = 48; top.Padding = new Padding(8);
                page.Controls.Add(top);
            }
            DataGridView MakeGrid(params (string id, string header)[] columns)
            {
                DataGridView grid = NewStatsGrid();
                grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                foreach (var col in columns) grid.Columns.Add(col.id, col.header);
                return grid;
            }
            string Rate(int w, int n) => n == 0 ? "-" : (100.0 * w / n).ToString("F1") + "%";

            // 개인 통계: 현재 계정뿐 아니라 공동 탭에서 선택한 친구의 기록도 열 수 있다.
            ComboBox personalPerson = MakePerson();
            personalPerson.AccessibleName = "분석 대상";
            FlowLayoutPanel personalTop = new FlowLayoutPanel();
            personalTop.Controls.AddRange(new Control[] { new Label { Text = "분석 대상", AutoSize = true, Padding = new Padding(0, 7, 0, 0) }, personalPerson,
                new Label { Text = "챔피언별 상세 기록", AutoSize = true, Padding = new Padding(8, 7, 0, 0) } });
            DataGridView personalGrid = MakeGrid(("Champ", "챔피언"), ("Games", "판수"), ("Wins", "승"),
                ("Loss", "패"), ("Rate", "승률"), ("KDA", "평균 K / D / A"), ("Score", "보정 점수"));
            Label personalNote = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 45,
                Text = "※ 저장된 경기의 참가자 기록 기준입니다. 증강/아이템은 현재 상세 저장 항목에 없습니다.",
                Padding = new Padding(12)
            };
            var personalSummary = new Label { AutoSize = true, Padding = new Padding(12, 7, 0, 0) };
            personalTop.Controls.Add(personalSummary);
            var recent = MakeGrid(("Date", "날짜"), ("Result", "결과"), ("Champion", "챔피언"), ("Kda", "K / D / A"));
            var personalViews = new TabControl { Dock = DockStyle.Fill };
            var recentPage = new TabPage("최근 경기");
            recentPage.Controls.Add(recent);
            var picksPage = new TabPage("챔피언별 기록");
            picksPage.Controls.Add(personalGrid);
            personalViews.TabPages.AddRange(new[] { recentPage, picksPage });
            personal.Controls.Add(personalViews); personal.Controls.Add(personalNote); AddTop(personal, personalTop);
            void RefreshPersonal()
            {
                personalGrid.Rows.Clear();
                if (!(personalPerson.SelectedItem is FriendListItem person)) return;
                if (embedded) dashboardPersonId = person.Puuid;
                var records = GetLocalDashboardMatches(person.Puuid, season.Text);
                int wins = records.Count(x => x.Win);
                var favorite = records.GroupBy(x => x.ChampionId).OrderByDescending(x => x.Count())
                    .ThenBy(x => x.Key).FirstOrDefault();
                personalSummary.Text = $"저장 경기 {records.Count}판 · 승률 {Rate(wins, records.Count)} · 최다 { (favorite == null ? "—" : ChampionName(favorite.Key))}";
                recent.Rows.Clear();
                foreach (var match in records.Take(20))
                    recent.Rows.Add(match.Date?.ToString("yyyy-MM-dd HH:mm") ?? "—", match.Win ? "승리" : "패배",
                        ChampionName(match.ChampionId), match.Kda);
                foreach (ChampionStat st in GetChampionStats(person.Puuid, season.Text)
                    .OrderByDescending(x => x.Games).ThenByDescending(x => x.Rate))
                    personalGrid.Rows.Add(ChampionName(st.ChampionId), st.Games, st.Wins, st.Games - st.Wins,
                        Rate(st.Wins, st.Games),
                        (st.Kills / (double)st.Games).ToString("F1") + " / " +
                        (st.Deaths / (double)st.Games).ToString("F1") + " / " +
                        (st.Assists / (double)st.Games).ToString("F1"), st.Score.ToString("F1"));
            }
            personalPerson.SelectedIndexChanged += (s, e) => RefreshPersonal();

            // 공동 통계: 챔피언별 3판 이상을 TOP3에 집계한다.
            FlowLayoutPanel groupTop = new FlowLayoutPanel();
            groupTop.Controls.AddRange(new Control[] {
                new Label { Text = "사람 더블클릭: 개인 통계 / 기여도 칸 더블클릭: 경기별 순위 / TOP3: 3판 이상",
                    AutoSize = true, Padding = new Padding(15, 7, 0, 0) } });
            DataGridView groupGrid = MakeGrid(("Name", "플레이어"), ("Games", "확인된 경기"), ("WinRate", "승률"),
                ("Favorite", "최애픽 TOP 3 (판수)"), ("Best", "베스트픽 TOP 3 (승률)"),
                ("Worst", "워스트픽 TOP 3 (승률)"), ("Contribution", "최근 20판 평균 기여도 순위"));
            group.Controls.Add(groupGrid); AddTop(group, groupTop);
            var compare = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, Padding = new Padding(8) };
            var first = MakePerson();
            var second = MakePerson();
            first.AccessibleName = "기준 멤버";
            second.AccessibleName = "비교 멤버";
            if (second.Items.Count > 1) second.SelectedIndex = 1;
            var comparison = new Label { AutoSize = true, Margin = new Padding(8, 7, 0, 0) };
            compare.Controls.AddRange(new Control[] { first, second, comparison });
            group.Controls.Add(compare);
            void RefreshComparison()
            {
                if (first.SelectedItem is not FriendListItem left || second.SelectedItem is not FriendListItem right)
                    return;
                string Summary(FriendListItem member)
                {
                    var stats = GetChampionStats(member.Puuid, season.Text);
                    int games = stats.Sum(x => x.Games);
                    return $"{games}판 · {Rate(stats.Sum(x => x.Wins), games)}";
                }
                comparison.Text = left.Puuid == right.Puuid ? "서로 다른 멤버를 선택해 주세요."
                    : Summary(left) + "  /  " + Summary(right);
            }
            first.SelectedIndexChanged += (_, _) => RefreshComparison();
            second.SelectedIndexChanged += (_, _) => RefreshComparison();
            void RefreshGroup()
            {
                groupGrid.Rows.Clear();
                foreach (var person in people.OrderBy(x => x.Value))
                {
                    var stats = GetChampionStats(person.Key, season.Text);
                    var qualified = stats.Where(x => x.Games >= 3).ToList();
                    string ListStats(IEnumerable<ChampionStat> list, bool winRate) =>
                        string.Join("  /  ", list.Take(3).Select(x => ChampionName(x.ChampionId) + " " +
                            (winRate ? Rate(x.Wins, x.Games) : x.Games + "판")));
                    string favorite = ListStats(qualified.OrderByDescending(x => x.Games).ThenByDescending(x => x.Rate), false);
                    string best = ListStats(qualified.OrderByDescending(x => x.Rate).ThenByDescending(x => x.Games), true);
                    string worst = ListStats(qualified.OrderBy(x => x.Rate).ThenByDescending(x => x.Games), true);
                    var contribution = RecentContributionRanks(person.Key, season.Text);
                    string contributionText = contribution.Count == 0 ? "데이터 없음" :
                        contribution.Average(x => x.Rank).ToString("F2") + "위 (" + contribution.Count + "/20판)";
                    int idx = groupGrid.Rows.Add(person.Value, stats.Sum(x => x.Games), Rate(stats.Sum(x => x.Wins), stats.Sum(x => x.Games)),
                        favorite.Length == 0 ? "3판 이상 없음" : favorite,
                        best.Length == 0 ? "3판 이상 없음" : best,
                        worst.Length == 0 ? "3판 이상 없음" : worst, contributionText);
                    groupGrid.Rows[idx].Tag = person.Key;
                }
            }
            groupGrid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                if (e.ColumnIndex == groupGrid.Columns["Contribution"].Index)
                {
                    string selectedId = groupGrid.Rows[e.RowIndex].Tag as string;
                    var results = RecentContributionRanks(selectedId, season.Text);
                    string details = results.Count == 0 ? "기여도 지표가 수집된 경기가 없습니다." :
                        string.Join("\r\n", results.Select((x, i) => (i + 1) + ". " +
                            ChampionName(x.ChampionId) + " | 팀 내 " + x.Rank + "위 | " + (x.Win ? "승" : "패") + " | 경기 " + x.GameId));
                    MessageBox.Show(details + "\r\n\r\n※ 최근 최대 20경기 중 지표가 있는 경기만 평균에 포함합니다.",
                        "최근 기여도 순위", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                string id = groupGrid.Rows[e.RowIndex].Tag as string;
                for (int i = 0; i < personalPerson.Items.Count; i++)
                    if ((personalPerson.Items[i] as FriendListItem)?.Puuid == id)
                    { personalPerson.SelectedIndex = i; break; }
                tabs.SelectedTab = personal;
            };

            // 챔피언별 전체 플레이 횟수 내림차순, 각 챔피언의 친구 랭킹을 한 행에 표시.
            FlowLayoutPanel rankTop = new FlowLayoutPanel();
            var rankSearch = new TextBox { Width = 140, PlaceholderText = "챔피언 검색", AccessibleName = "챔피언 검색" };
            var rankMinimum = new NumericUpDown { Minimum = 1, Maximum = 10000, Width = 65, AccessibleName = "최소 판수" };
            var rankOrder = new ComboBox { Width = 95, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "정렬" };
            rankOrder.Items.AddRange(new object[] { "판수순", "승률순" });
            rankOrder.SelectedIndex = 0;
            rankTop.Controls.AddRange(new Control[] { rankSearch,
                new Label { Text = "최소 판수", AutoSize = true, Margin = new Padding(6, 7, 2, 0) }, rankMinimum, rankOrder });
            rankTop.Controls.AddRange(new Control[] {
                new Label { Text = "챔피언별 총 판수순 | 랭킹은 보정 승률 기준 (1판부터 표시, 1~4판은 표본 부족)",
                    AutoSize = true, Padding = new Padding(12, 7, 0, 0) }
            });
            DataGridView rankGrid = MakeGrid(("Champion", "챔피언"), ("Games", "전체 판수"), ("Rate", "전체 승률"),
                ("First", "1위"), ("Second", "2위"), ("Third", "3위"), ("Others", "그 외 순위"));
            rankGrid.Columns["Champion"].FillWeight = 85;
            rankGrid.Columns["Games"].FillWeight = 55;
            rankGrid.Columns["Others"].FillWeight = 170;
            champions.Controls.Add(rankGrid); AddTop(champions, rankTop);
            void RefreshRankChampions()
            {
                rankGrid.Rows.Clear();
                var all = people.SelectMany(person => GetChampionStats(person.Key, season.Text)
                    .Select(st => new { Id = person.Key, Name = person.Value, Stat = st })).ToList();
                var championsByUsage = all.GroupBy(x => x.Stat.ChampionId)
                    .Select(g => new { ChampionId = g.Key, Total = g.Sum(x => x.Stat.Games),
                        Wins = g.Sum(x => x.Stat.Wins), Entries = g.ToList() })
                    .Where(x => x.Total >= rankMinimum.Value && ChampionName(x.ChampionId)
                        .Contains(rankSearch.Text.Trim(), StringComparison.CurrentCultureIgnoreCase))
                    .OrderByDescending(x => rankOrder.SelectedIndex == 1 ? (double)x.Wins / x.Total : x.Total)
                    .ThenByDescending(x => x.Total).ThenBy(x => ChampionName(x.ChampionId));
                foreach (var champ in championsByUsage)
                {
                    var ranked = champ.Entries
                        .OrderByDescending(x => x.Stat.Score)
                        .ThenByDescending(x => x.Stat.Games)
                        .ThenByDescending(x => x.Stat.Rate).ToList();
                    string RankLabel(int i) => i >= ranked.Count ? "-" :
                        ranked[i].Name + " (" + ranked[i].Stat.Games + "판, " +
                        Rate(ranked[i].Stat.Wins, ranked[i].Stat.Games) +
                        (ranked[i].Stat.Games < 5 ? ", 표본 부족" : "") + ")";
                    string others = string.Join(" / ", ranked.Skip(3).Select((x, i) =>
                        (i + 4) + "위 " + x.Name + " (" + x.Stat.Games + "판)"));
                    rankGrid.Rows.Add(ChampionName(champ.ChampionId), champ.Total, Rate(champ.Wins, champ.Total),
                        RankLabel(0), RankLabel(1), RankLabel(2), others);
                }
            }
            rankSearch.TextChanged += (_, _) => RefreshRankChampions();
            rankMinimum.ValueChanged += (_, _) => RefreshRankChampions();
            rankOrder.SelectedIndexChanged += (_, _) => RefreshRankChampions();
            season.SelectedIndexChanged += (_, _) =>
            {
                if (embedded) dashboardSeason = season.Text;
                RefreshPersonal(); RefreshGroup(); RefreshComparison(); RefreshRankChampions();
            };
            RefreshPersonal(); RefreshGroup(); RefreshComparison(); RefreshRankChampions();
            if (!string.IsNullOrWhiteSpace(initialPerson))
                for (int i = 0; i < personalPerson.Items.Count; i++)
                    if ((personalPerson.Items[i] as FriendListItem)?.Puuid == initialPerson)
                    { personalPerson.SelectedIndex = i; break; }
            tabs.SelectedIndex = Math.Max(0, Math.Min(2, initialTab));
            if (embedded) tabs.SelectedIndexChanged += (_, _) => dashboardTab = tabs.SelectedIndex;
            ApplyDashboardTheme(frame);
            if (dialog != null)
            {
                ApplyDashboardTheme(dialog);
                dialog.ShowDialog(this);
            }
        }

        private static DataGridView NewStatsGrid()
        {
            return new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
        }

        private class FriendListItem
        {
            public string Puuid { get; set; } = "";
            public string RiotId { get; set; } = "";
            public override string ToString() => DisplayRiotName(RiotId);
        }

        private string AnalyzeFriend(string friendPuuid, string riotId)
        {
            int same = 0, wins = 0, opposite = 0;
            var combinations = new Dictionary<string, (int games, int wins)>();
            foreach (MatchDetailRecord game in savedDetails.Values)
            {
                MatchParticipantRecord me = game.Participants.FirstOrDefault(x =>
                    string.Equals(x.Puuid, activePuuid, StringComparison.OrdinalIgnoreCase));
                MatchParticipantRecord friend = game.Participants.FirstOrDefault(x =>
                    string.Equals(x.Puuid, friendPuuid, StringComparison.OrdinalIgnoreCase));
                if (me == null || friend == null) continue;
                if (me.TeamId != friend.TeamId) { opposite++; continue; }
                same++;
                if (me.Win) wins++;
                string combo = ChampionName(me.ChampionId) + " + " + ChampionName(friend.ChampionId);
                combinations.TryGetValue(combo, out var stat);
                combinations[combo] = (stat.games + 1, stat.wins + (me.Win ? 1 : 0));
            }
            var lines = new List<string>
            {
                "친구: " + DisplayRiotName(riotId),
                "기준: 저장된 상세 경기 " + savedDetails.Count + "경기 (현재 계정 기준)",
                "같은 팀: " + same + "경기 / " + wins + "승 " + (same - wins) + "패",
                "함께한 승률: " + (same == 0 ? "-" : (100.0 * wins / same).ToString("F1") + "%"),
                "상대 팀으로 만남: " + opposite + "경기", "", "[챔피언 조합별 기록]"
            };
            if (combinations.Count == 0) lines.Add("같은 팀으로 플레이한 기록이 없습니다.");
            foreach (var pair in combinations.OrderByDescending(x => x.Value.games))
                lines.Add(pair.Key + " | " + pair.Value.games + "경기 | " + pair.Value.wins + "승 | " +
                    (100.0 * pair.Value.wins / pair.Value.games).ToString("F1") + "%");
            lines.Add("");
            lines.Add("※ 수집된 상세 기록만 집계하며, 누락된 과거 경기는 포함되지 않습니다.");
            return string.Join("\r\n", lines);
        }

        private class MatchParticipantRecord
        {
            public int ParticipantId { get; set; }
            public string Puuid { get; set; } = "";
            public string Name { get; set; } = "";
            public int TeamId { get; set; }
            public int ChampionId { get; set; }
            public bool Win { get; set; }
            public int Kills { get; set; }
            public int Deaths { get; set; }
            public int Assists { get; set; }
            public int? DamageToChampions { get; set; }
            public int? DamageTaken { get; set; }
            public int? DamageMitigated { get; set; }
            public int? Healing { get; set; }
            public int? AllyHealing { get; set; }
            public int? AllyShielding { get; set; }
            public double? CrowdControlSeconds { get; set; }
            // 원본 API가 해당 항목을 제공하지 않으면 null로 유지한다.
        }

        private class MatchDetailRecord
        {
            public string GameId { get; set; } = "";
            public List<MatchParticipantRecord> Participants { get; set; } = new List<MatchParticipantRecord>();
        }

        private string DetailStorageFile()
        {
            string safe = Regex.Replace(activePuuid, @"[^a-zA-Z0-9_-]", "_");
            return Path.Combine(DataDirectory, "details_" + safe + ".json");
        }

        private void LoadStoredDetails()
        {
            if (string.IsNullOrWhiteSpace(activePuuid)) return;
            string path = DetailStorageFile();
            if (!File.Exists(path)) return;
            try
            {
                var records = JsonSerializer.Deserialize<List<MatchDetailRecord>>(File.ReadAllText(path));
                foreach (var record in records ?? new List<MatchDetailRecord>())
                {
                    if (!string.IsNullOrWhiteSpace(record.GameId) &&
                        record.Participants != null && record.Participants.Count == 10 &&
                        record.Participants.All(x => !string.IsNullOrWhiteSpace(x.Puuid)))
                        savedDetails[record.GameId] = record;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("저장된 경기 상세 정보를 읽지 못했습니다: " + ex.Message);
            }
        }

        private void SaveStoredDetails()
        {
            if (string.IsNullOrWhiteSpace(activePuuid)) return;
            Directory.CreateDirectory(DataDirectory);
            string path = DetailStorageFile();
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(savedDetails.Values.ToList()));
            File.Move(temp, path, true);
        }

        private static MatchDetailRecord ParseMatchDetail(JsonElement root, string gameId)
        {
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("game", out JsonElement nested) && nested.ValueKind == JsonValueKind.Object)
                root = nested;
            if (!root.TryGetProperty("participants", out JsonElement participants) ||
                participants.ValueKind != JsonValueKind.Array || participants.GetArrayLength() != 10)
                return null;

            var identities = new Dictionary<int, JsonElement>();
            if (root.TryGetProperty("participantIdentities", out JsonElement ids) && ids.ValueKind == JsonValueKind.Array)
                foreach (JsonElement item in ids.EnumerateArray())
                    if (item.TryGetProperty("player", out JsonElement identity) && identity.ValueKind == JsonValueKind.Object)
                        identities[JsonInt(item, "participantId", -1)] = identity;

            var detail = new MatchDetailRecord { GameId = gameId };
            foreach (JsonElement participant in participants.EnumerateArray())
            {
                int pid = JsonInt(participant, "participantId", -1);
                JsonElement stats = participant.TryGetProperty("stats", out JsonElement st) ? st : participant;
                JsonElement identity = participant.TryGetProperty("player", out JsonElement pl) ? pl : participant;
                if (identities.TryGetValue(pid, out JsonElement matched)) identity = matched;
                string id = JsonString(participant, "puuid", JsonString(stats, "puuid", JsonString(identity, "puuid")));
                if (string.IsNullOrWhiteSpace(id)) return null;
                string name = JsonString(identity, "gameName", JsonString(identity, "summonerName"));
                string tag = JsonString(identity, "tagLine");
                if (!string.IsNullOrWhiteSpace(tag)) name += "#" + tag;
                detail.Participants.Add(new MatchParticipantRecord
                {
                    ParticipantId = pid,
                    Puuid = id,
                    Name = name,
                    TeamId = JsonInt(participant, "teamId", JsonInt(stats, "teamId")),
                    ChampionId = JsonInt(participant, "championId", JsonInt(stats, "championId")),
                    Win = JsonBool(stats, "win"),
                    Kills = JsonInt(stats, "kills"),
                    Deaths = JsonInt(stats, "deaths"),
                    Assists = JsonInt(stats, "assists"),
                    DamageToChampions = JsonNullableInt(stats, "totalDamageDealtToChampions"),
                    DamageTaken = JsonNullableInt(stats, "totalDamageTaken"),
                    DamageMitigated = JsonNullableInt(stats, "damageSelfMitigated"),
                    Healing = JsonNullableInt(stats, "totalHeal"),
                    AllyHealing = JsonNullableInt(stats, "totalHealsOnTeammates"),
                    AllyShielding = JsonNullableInt(stats, "totalDamageShieldedOnTeammates"),
                    CrowdControlSeconds = JsonNullableDouble(stats, "timeCCingOthers")
                });
            }
            return detail.Participants.Count == 10 &&
                detail.Participants.Select(x => x.Puuid).Distinct().Count() == 10 ? detail : null;
        }

        private static int? JsonNullableInt(JsonElement obj, string name)
        {
            if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out JsonElement value))
                return null;
            return value.TryGetInt32(out int result) ? result : (int?)null;
        }

        private static double? JsonNullableDouble(JsonElement obj, string name)
        {
            if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out JsonElement value))
                return null;
            return value.TryGetDouble(out double result) ? result : (double?)null;
        }

        private static bool HasContributionData(MatchDetailRecord record)
        {
            return record.Participants != null && record.Participants.Count == 10 &&
                record.Participants.All(p => p.DamageToChampions.HasValue && p.DamageTaken.HasValue);
        }

        // 수집된 상세 기록 중 본인/친구의 최근 20경기만 대상으로 추가 지표를 보충한다.
        // 원본에 항목이 없다면 0으로 가정하지 않고 미수집 상태로 남긴다.
        private async Task<bool> FillContributionAsync(Button button)
        {
            if (string.IsNullOrWhiteSpace(activePuuid))
            {
                MessageBox.Show("먼저 계정 정보를 조회해 주세요.");
                return false;
            }
            button.Enabled = false;
            try
            {
                LcuInfo info = await GetLcuInfoAsync();
                if (info == null) throw new Exception("롤 클라이언트를 먼저 실행해 주세요.");
                using HttpClient client = CreateClient(info);
                var people = DashboardPeople();
                var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string personId in people.Keys)
                {
                    var recent = savedDetails.Values
                        .Where(g => g.Participants.Any(p => string.Equals(p.Puuid, personId, StringComparison.OrdinalIgnoreCase)))
                        .Where(g => savedMatches.Values.Any(m => m.GameId == g.GameId && m.QueueId == 2400))
                        .OrderByDescending(g => savedMatches.Values.First(m => m.GameId == g.GameId).Timestamp)
                        .Take(20);
                    foreach (var game in recent)
                        if (!HasContributionData(game)) targets.Add(game.GameId);
                }
                int attempted = 0, updated = 0, missing = 0, failed = 0;
                foreach (string gameId in targets)
                {
                    attempted++;
                    lblStatus.Text = $"상태: 기여도 데이터 보충 {attempted}/{targets.Count}";
                    using HttpResponseMessage response = await client.GetAsync(
                        "https://127.0.0.1:" + info.Port + "/lol-match-history/v1/games/" + Uri.EscapeDataString(gameId));
                    if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                    {
                        failed++;
                        break;
                    }
                    if (!response.IsSuccessStatusCode) { failed++; continue; }
                    using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    MatchDetailRecord fresh = ParseMatchDetail(doc.RootElement, gameId);
                    if (fresh == null) { failed++; continue; }
                    if (!HasContributionData(fresh)) { missing++; continue; }
                    savedDetails[gameId] = fresh;
                    SaveStoredDetails();
                    updated++;
                    await Task.Delay((int)numHistoryDelay.Value);
                }
                txtResult.Text = $"[기여도 데이터 보충]\r\n대상: {targets.Count}경기\r\n조회: {attempted}\r\n지표 저장: {updated}\r\nAPI 지표 누락: {missing}\r\n조회 실패: {failed}\r\n" +
                    "저장 항목: 챔피언 피해량, 받은 피해량, 피해 감소, 회복, 아군 회복/보호막, CC 시간\r\n" +
                    "※ API에서 제공하지 않는 값은 null로 유지합니다. 기존 KDA 기록은 유지됩니다.";
                lblStatus.Text = "상태: 기여도 데이터 보충 완료";
                OpenEmbeddedDashboard();
                if (!updateAllRunning) MessageBox.Show(txtResult.Text, "기여도 수집 결과");
                return true;
            }
            catch (Exception ex)
            {
                lblStatus.Text = "상태: 기여도 데이터 보충 오류";
                MessageBox.Show(ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            finally { button.Enabled = true; }
        }

        private async Task<int> SyncMatchDetailsAsync(HttpClient client)
        {
            // LCU가 반환한 경기만 대상으로 한다. 기존에 수동으로 가져온 오래된 JSON 기록도
            // 상세 조회를 시도하되, 서버가 거부하면 다음 실행에서 다시 시도할 수 있게 남긴다.
            var pending = savedMatches.Values
                .Where(x => x.QueueId == 2400 && (x.Duration == 0 || x.Duration >= 480))
                .OrderByDescending(x => x.Timestamp)
                .Where(x => !savedDetails.ContainsKey(x.GameId))
                .Take(30) // 한 번에 최대 30경기: 과도한 LCU 요청 방지
                .ToList();
            int added = 0, failed = 0;
            foreach (StoredMatch match in pending)
            {
                lblStatus.Text = "상태: 10명 상세 자동 수집 " + (added + failed + 1) + "/" + pending.Count;
                try
                {
                    using HttpResponseMessage response = await client.GetAsync(
                        "https://127.0.0.1:" + currentLcuPort + "/lol-match-history/v1/games/" +
                        Uri.EscapeDataString(match.GameId));
                    if (!response.IsSuccessStatusCode) { failed++; continue; }
                    using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    MatchDetailRecord detail = ParseMatchDetail(doc.RootElement, match.GameId);
                    if (detail == null) { failed++; continue; }
                    savedDetails[match.GameId] = detail;
                    added++;
                    SaveStoredDetails(); // 한 경기마다 저장: 중단되어도 수집 결과 유지
                }
                catch (HttpRequestException) { failed++; break; }
                catch (TaskCanceledException) { failed++; break; }
                catch (JsonException) { failed++; }
                Application.DoEvents();
            }
            txtResult.Text = "10명 상세 자동 수집: 신규 " + added + "경기 / 실패·미지원 " + failed +
                "경기 / 누적 " + savedDetails.Count + "경기";
            return added;
        }

        // JSON으로 가져온 과거 경기 ID를 사용해 상세 API를 직접 조회한다.
        // 목록 API의 최근 100경기 제한을 우회한다고 보장하지 않는다.
        private string HistoryCursorFile()
        {
            string safe = Regex.Replace(activePuuid, @"[^a-zA-Z0-9_-]", "_");
            return Path.Combine(DataDirectory, "history_cursor_" + safe + ".txt");
        }

        private async void BtnHistory_Click(object sender, EventArgs e)
        {
            if (historyRunning)
            {
                historyStopRequested = true;
                btnHistory.Enabled = false;
                lblStatus.Text = "상태: 현재 요청 완료 후 중단 중...";
                return;
            }
            if (string.IsNullOrWhiteSpace(activePuuid) || savedMatches.Count == 0)
            {
                MessageBox.Show("계정 정보 조회 후 과거 JSON을 가져와 주세요.");
                return;
            }

            historyRunning = true;
            historyStopRequested = false;
            btnHistory.Text = "수집 중단";
            btnMatches.Enabled = false;
            btnConnect.Enabled = false;
            btnImport.Enabled = false;
            numHistoryDelay.Enabled = false;
            int delayMs = (int)numHistoryDelay.Value;
            try
            {
                LcuInfo info = await GetLcuInfoAsync();
                if (info == null) throw new Exception("롤 클라이언트를 먼저 실행해 주세요.");
                using HttpClient client = CreateClient(info);
                List<StoredMatch> candidates = savedMatches.Values
                    .Where(x => x.QueueId == 2400 && (x.Duration == 0 || x.Duration >= 480))
                    .GroupBy(x => x.GameId).Select(x => x.First())
                    .OrderBy(x => x.Timestamp).ToList();
                int cursor = 0;
                string cursorFile = HistoryCursorFile();
                if (File.Exists(cursorFile))
                    int.TryParse(File.ReadAllText(cursorFile), out cursor);
                if (cursor < 0 || cursor > candidates.Count) cursor = 0;
                int attempted = 0, added = 0, unsupported = 0, skipped = 0;
                bool disconnected = false;
                while (cursor < candidates.Count && !historyStopRequested)
                {
                    StoredMatch match = candidates[cursor];
                    if (savedDetails.ContainsKey(match.GameId))
                    {
                        skipped++;
                        cursor++;
                        SaveHistoryCursor(cursorFile, cursor);
                        continue;
                    }
                    attempted++;
                    lblStatus.Text = "상태: 과거 전체 수집 " + cursor + "/" + candidates.Count +
                        " | 신규 " + added + " | 실패 " + unsupported;
                    try
                    {
                        using HttpResponseMessage response = await client.GetAsync(
                            "https://127.0.0.1:" + info.Port + "/lol-match-history/v1/games/" +
                            Uri.EscapeDataString(match.GameId));
                        if (response.IsSuccessStatusCode)
                        {
                            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                            MatchDetailRecord detail = ParseMatchDetail(doc.RootElement, match.GameId);
                            if (detail != null)
                            {
                                savedDetails[match.GameId] = detail;
                                SaveStoredDetails();
                                added++;
                            }
                            else unsupported++;
                        }
                        else if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                        {
                            disconnected = true; // 재시도할 수 있도록 현재 경기의 커서를 유지
                            break;
                        }
                        else unsupported++;
                    }
                    catch (HttpRequestException) { disconnected = true; break; }
                    catch (TaskCanceledException) { disconnected = true; break; }
                    catch (JsonException) { unsupported++; }

                    cursor++;
                    SaveHistoryCursor(cursorFile, cursor);
                    txtResult.Text = "[과거 경기 전체 자동 수집]\r\n" +
                        "진행 위치: " + cursor + "/" + candidates.Count + "\r\n" +
                        "이번 조회: " + attempted + "경기\r\n" +
                        "신규 저장: " + added + "경기\r\n" +
                        "기존 저장 건너뜀: " + skipped + "경기\r\n" +
                        "실패/정보 부족: " + unsupported + "경기\r\n" +
                        "상세 누적: " + savedDetails.Count + "경기\r\n" +
                        "요청 간격: " + delayMs + "ms";
                    if (!historyStopRequested && cursor < candidates.Count)
                        await Task.Delay(delayMs);
                }
                string reason = historyStopRequested ? "사용자가 중단했습니다. 다시 누르면 이어서 수집합니다."
                    : disconnected ? "연결 오류 또는 서버 제한으로 중단했습니다. 잠시 후 다시 시도하세요."
                    : "저장된 경기 ID 전체 검사 완료.";
                txtResult.AppendText("\r\n" + reason);
                lblStatus.Text = "상태: " + reason;
            }
            catch (Exception ex)
            {
                lblStatus.Text = "상태: 과거 상세 수집 실패";
                txtResult.Text = ex.Message;
                MessageBox.Show(ex.Message, "과거 상세 조회 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                historyRunning = false;
                historyStopRequested = false;
                btnHistory.Text = "과거 전체 수집";
                btnHistory.Enabled = true;
                btnMatches.Enabled = !string.IsNullOrWhiteSpace(puuid);
                btnConnect.Enabled = true;
                btnImport.Enabled = true;
                numHistoryDelay.Enabled = true;
            }
        }

        private static void SaveHistoryCursor(string path, int cursor)
        {
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(path, cursor.ToString(CultureInfo.InvariantCulture));
        }

        private class StoredMatch
        {
            public string GameId { get; set; } = "";
            public string Puuid { get; set; } = "";
            public long Timestamp { get; set; }
            public int Duration { get; set; }
            public int ChampionId { get; set; }
            public bool Win { get; set; }
            public int Kills { get; set; }
            public int Deaths { get; set; }
            public int Assists { get; set; }
            public int QueueId { get; set; }
        }

        private static string JsonString(JsonElement obj, string name, string fallback = "")
        {
            return obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out JsonElement v)
                ? v.ToString() : fallback;
        }

        private static int JsonInt(JsonElement obj, string name, int fallback = 0)
        {
            return int.TryParse(JsonString(obj, name), out int value) ? value : fallback;
        }

        private static long JsonLong(JsonElement obj, string name)
        {
            return long.TryParse(JsonString(obj, name), out long value) ? value : 0;
        }

        private static bool JsonBool(JsonElement obj, string name)
        {
            return bool.TryParse(JsonString(obj, name), out bool value) && value;
        }

        private static string MatchKey(StoredMatch game)
        {
            return game.Puuid + ":" + game.GameId;
        }

        private static DateTime MatchDate(StoredMatch game)
        {
            try
            {
                return game.Timestamp > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(game.Timestamp).LocalDateTime
                    : DateTime.MinValue;
            }
            catch { return DateTime.MinValue; }
        }

        private static string SeasonOf(StoredMatch game)
        {
            DateTime date = MatchDate(game);
            return date >= Season3Start ? "시즌 3" : "시즌 2";
        }

        private string StorageFile()
        {
            string safe = Regex.Replace(activePuuid, @"[^a-zA-Z0-9_-]", "_");
            return Path.Combine(DataDirectory, "matches_" + safe + ".json");
        }

        private void LoadStoredMatches()
        {
            if (string.IsNullOrWhiteSpace(activePuuid)) return;
            string path = StorageFile();
            if (!File.Exists(path)) return;
            try
            {
                var records = JsonSerializer.Deserialize<List<StoredMatch>>(File.ReadAllText(path));
                foreach (StoredMatch game in records ?? new List<StoredMatch>())
                {
                    if (game.Puuid == activePuuid && !string.IsNullOrWhiteSpace(game.GameId))
                        savedMatches[MatchKey(game)] = game;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("저장된 전적을 읽지 못했습니다: " + ex.Message);
            }
        }

        private void SaveStoredMatches()
        {
            if (string.IsNullOrWhiteSpace(activePuuid)) return;
            Directory.CreateDirectory(DataDirectory);
            string path = StorageFile();
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(savedMatches.Values.ToList(),
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, true);
        }

        private int MergeLcuGames(List<JsonElement> games)
        {
            int added = 0;
            foreach (JsonElement g in games)
            {
                string gid = JsonString(g, "gameId");
                if (string.IsNullOrWhiteSpace(gid)) continue;
                JsonElement player = default;
                if (g.TryGetProperty("participants", out JsonElement participants) &&
                    participants.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement participant in participants.EnumerateArray())
                    {
                        if (JsonString(participant, "puuid") == activePuuid ||
                            (participant.TryGetProperty("player", out JsonElement pl) &&
                             JsonString(pl, "puuid") == activePuuid))
                        { player = participant; break; }
                    }
                    if (player.ValueKind == JsonValueKind.Undefined && participants.GetArrayLength() > 0)
                        player = participants[0]; // LCU 목록이 본인만 포함하는 경우
                }
                if (player.ValueKind != JsonValueKind.Object) continue;
                JsonElement stats = player.TryGetProperty("stats", out JsonElement st) ? st : player;
                long timestamp = JsonLong(g, "gameCreation");
                if (timestamp == 0)
                {
                    string date = JsonString(g, "gameCreationDate");
                    if (DateTimeOffset.TryParse(date, out DateTimeOffset dt))
                        timestamp = dt.ToUnixTimeMilliseconds();
                }
                if (timestamp > 0 && timestamp < 100000000000L) timestamp *= 1000;
                StoredMatch record = new StoredMatch
                {
                    GameId = gid,
                    Puuid = activePuuid,
                    Timestamp = timestamp,
                    Duration = JsonInt(g, "gameDuration"),
                    ChampionId = JsonInt(player, "championId"),
                    Win = JsonBool(stats, "win"),
                    Kills = JsonInt(stats, "kills"),
                    Deaths = JsonInt(stats, "deaths"),
                    Assists = JsonInt(stats, "assists"),
                    QueueId = JsonInt(g, "queueId")
                };
                string key = MatchKey(record);
                if (!savedMatches.ContainsKey(key)) { savedMatches[key] = record; added++; }
            }
            return added;
        }

        private async void BtnImport_Click(object sender, EventArgs e)
        {
            using OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "칼바람 분석기 JSON (*.json)|*.json|모든 파일 (*.*)|*.*",
                Title = "mayhem_companion_aug.json 선택"
            };
            if (dialog.ShowDialog() != DialogResult.OK) return;
            btnImport.Enabled = false;
            try
            {
                string json = await File.ReadAllTextAsync(dialog.FileName);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                if (!root.TryGetProperty("games", out JsonElement games) ||
                    games.ValueKind != JsonValueKind.Array)
                    throw new Exception("games 배열이 없는 파일입니다. mayhem_companion_aug.json을 선택해 주세요.");

                List<StoredMatch> imported = new List<StoredMatch>();
                int skippedShort = 0;
                foreach (JsonElement g in games.EnumerateArray())
                {
                    string gid = JsonString(g, "gid");
                    string owner = JsonString(g, "puuid");
                    if (string.IsNullOrWhiteSpace(gid) || string.IsNullOrWhiteSpace(owner)) continue;
                    int duration = JsonInt(g, "dur");
                    if (duration > 0 && duration < 480) { skippedShort++; continue; }
                    imported.Add(new StoredMatch
                    {
                        GameId = gid,
                        Puuid = owner,
                        Timestamp = JsonLong(g, "t"),
                        Duration = duration,
                        ChampionId = JsonInt(g, "championId"),
                        Win = JsonBool(g, "win"),
                        Kills = JsonInt(g, "k"),
                        Deaths = JsonInt(g, "d"),
                        Assists = JsonInt(g, "a"),
                        QueueId = JsonInt(g, "queueId")
                    });
                }
                var owners = imported.Select(g => g.Puuid).Distinct().ToList();
                if (owners.Count != 1)
                    throw new Exception("JSON에 계정이 없거나 여러 계정이 섞여 있습니다. 자동 병합하지 않습니다.");
                string importedPuuid = owners[0];
                if (!string.IsNullOrWhiteSpace(puuid) && puuid != importedPuuid)
                    throw new Exception("현재 로그인 계정과 JSON의 PUUID가 다릅니다. 다른 계정의 기록은 합치지 않습니다.");
                if (activePuuid != importedPuuid)
                {
                    activePuuid = importedPuuid;
                    savedMatches.Clear();
                    savedDetails.Clear();
                    LoadStoredMatches();
                    LoadStoredDetails();
                    friends.Clear();
                    LoadFriends();
                }
                int added = 0;
                foreach (StoredMatch g in imported)
                {
                    string key = MatchKey(g);
                    if (!savedMatches.ContainsKey(key)) { savedMatches[key] = g; added++; }
                }
                SaveStoredMatches();
                await LoadChampionNamesAsync();
                RefreshView();
                OpenEmbeddedDashboard();
                lblStatus.Text = "상태: JSON 가져오기 완료";
                txtResult.Text = "읽은 기록: " + games.GetArrayLength() + "경기\r\n" +
                    "8분 미만 제외: " + skippedShort + "경기\r\n" +
                    "신규 추가: " + added + "경기\r\n" +
                    "현재 누적: " + savedMatches.Count + "경기\r\n" +
                    "저장 경로: " + StorageFile();
            }
            catch (Exception ex)
            {
                lblStatus.Text = "상태: JSON 가져오기 실패";
                txtResult.Text = ex.Message;
                MessageBox.Show(ex.Message, "JSON 가져오기 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { btnImport.Enabled = true; }
        }

        private async Task LoadChampionNamesAsync()
        {
            if (championNames.Count > 0) return;
            try
            {
                using HttpClient client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(8);
                string json = await client.GetStringAsync(
                    "https://ddragon.leagueoflegends.com/cdn/16.23.1/data/ko_KR/champion.json");
                using JsonDocument doc = JsonDocument.Parse(json);
                foreach (JsonProperty p in doc.RootElement.GetProperty("data").EnumerateObject())
                {
                    int id = JsonInt(p.Value, "key");
                    if (id > 0) championNames[id] = JsonString(p.Value, "name");
                }
                if (groupRoster is not null) RenderGroupStats();
            }
            catch { /* 오프라인이면 챔피언 ID로 표시 */ }
        }

        private string ChampionName(int id)
        {
            return championNames.TryGetValue(id, out string name) ? name : "챔프#" + id;
        }

        private void RefreshView()
        {
            if (dgvMatches == null || cmbSeason == null || btnView == null) return;
            string selected = cmbSeason.SelectedItem?.ToString() ?? "전체 시즌";
            List<StoredMatch> matches = savedMatches.Values
                .Where(g => g.QueueId == 2400 && (g.Duration == 0 || g.Duration >= 480))
                .Where(g => selected == "전체 시즌" || SeasonOf(g) == selected)
                .OrderByDescending(g => g.Timestamp).ToList();
            dgvMatches.Rows.Clear();
            dgvMatches.Columns.Clear();
            if (showingChampions)
            {
                btnView.Text = "경기 목록 보기";
                dgvMatches.Columns.Add("Champion", "챔피언");
                dgvMatches.Columns.Add("Games", "경기 수");
                dgvMatches.Columns.Add("Wins", "승리");
                dgvMatches.Columns.Add("Rate", "승률");
                dgvMatches.Columns.Add("KDA", "평균 K / D / A");
                foreach (var group in matches.GroupBy(g => g.ChampionId).OrderByDescending(g => g.Count()))
                {
                    int count = group.Count();
                    dgvMatches.Rows.Add(ChampionName(group.Key), count,
                        group.Count(g => g.Win),
                        (100.0 * group.Count(g => g.Win) / count).ToString("F1") + "%",
                        group.Average(g => g.Kills).ToString("F1") + " / " +
                        group.Average(g => g.Deaths).ToString("F1") + " / " +
                        group.Average(g => g.Assists).ToString("F1"));
                }
            }
            else
            {
                btnView.Text = "챔피언 통계 보기";
                dgvMatches.Columns.Add("Date", "경기 날짜");
                dgvMatches.Columns.Add("Champion", "챔피언");
                dgvMatches.Columns.Add("Result", "승패");
                dgvMatches.Columns.Add("KDA", "K / D / A");
                dgvMatches.Columns.Add("Mode", "시즌");
                dgvMatches.Columns.Add("Queue", "Queue ID");
                dgvMatches.Columns.Add("GameId", "경기 ID");
                foreach (StoredMatch g in matches)
                    dgvMatches.Rows.Add(MatchDate(g) == DateTime.MinValue ? "날짜 없음" : MatchDate(g).ToString("yyyy-MM-dd HH:mm"),
                        ChampionName(g.ChampionId), g.Win ? "승리" : "패배",
                        g.Kills + " / " + g.Deaths + " / " + g.Assists,
                        SeasonOf(g), g.QueueId, g.GameId);
            }
            int wins = matches.Count(g => g.Win);
            lblCount.Text = "경기 수: " + matches.Count + " | " + wins + "승 " +
                (matches.Count - wins) + "패 | 승률 " +
                (matches.Count == 0 ? "-" : (100.0 * wins / matches.Count).ToString("F2") + "%");
        }

        private void DisplayGames(List<JsonElement> games)
        {
            dgvMatches.Rows.Clear();

            foreach (JsonElement game in games)
            {
                string date = GetJsonValue(game, "gameCreationDate");
                string gameId = GetJsonValue(game, "gameId");
                string mode = GetJsonValue(game, "gameMode");
                string queue = GetJsonValue(game, "queueId");

                string champion = "확인 불가";
                string result = "확인 불가";
                string kda = "확인 불가";

                if (game.TryGetProperty("participants", out JsonElement participants) &&
                    participants.ValueKind == JsonValueKind.Array &&
                    participants.GetArrayLength() > 0)
                {
                    JsonElement player = participants[0];

                    champion = GetJsonValue(player, "championId");

                    if (player.TryGetProperty("stats", out JsonElement stats))
                    {
                        string win = GetJsonValue(stats, "win");

                        result = win == "True" || win == "true"
                            ? "승리"
                            : win == "False" || win == "false"
                                ? "패배"
                                : "확인 불가";

                        string kills = GetJsonValue(stats, "kills");
                        string deaths = GetJsonValue(stats, "deaths");
                        string assists = GetJsonValue(stats, "assists");

                        kda = kills + " / " + deaths + " / " + assists;
                    }
                }

                if (DateTime.TryParse(date, out DateTime parsedDate))
                    date = parsedDate.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

                dgvMatches.Rows.Add(
                    date,
                    champion,
                    result,
                    kda,
                    mode,
                    queue,
                    gameId
                );
            }
        }

        private HttpClient CreateClient(LcuInfo info)
        {
            HttpClientHandler handler = new HttpClientHandler();

            handler.ServerCertificateCustomValidationCallback =
                (message, certificate, chain, errors) =>
                {
                    return message?.RequestUri?.Host == "127.0.0.1";
                };

            HttpClient client = new HttpClient(handler);

            client.Timeout = TimeSpan.FromSeconds(30);

            string authText = Convert.ToBase64String(
                Encoding.UTF8.GetBytes("riot:" + info.Token)
            );

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", authText);

            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json")
            );

            return client;
        }

        private async Task<LcuInfo> GetLcuInfoAsync()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            string script =
                "(Get-CimInstance Win32_Process -Filter " +
                "\"Name = 'LeagueClientUx.exe'\").CommandLine";

            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(script);

            using Process process = new Process
            {
                StartInfo = startInfo
            };

            process.Start();

            Task<string> outputTask =
                process.StandardOutput.ReadToEndAsync();

            Task<string> errorTask =
                process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            string output = await outputTask;
            string error = await errorTask;

            if (process.ExitCode != 0)
                throw new Exception("프로세스 조회 실패: " + error);

            Match portMatch = Regex.Match(output, @"--app-port=(\d+)");
            Match tokenMatch = Regex.Match(
                output,
                @"--remoting-auth-token=([^\s""]+)"
            );

            if (!portMatch.Success || !tokenMatch.Success)
                return null;

            return new LcuInfo
            {
                Port = portMatch.Groups[1].Value,
                Token = tokenMatch.Groups[1].Value
            };
        }

        private string GetJsonValue(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out JsonElement value))
                return value.ToString();

            return "확인 불가";
        }

        private class LcuInfo
        {
            public string Port { get; set; }
            public string Token { get; set; }
        }
    }
}
