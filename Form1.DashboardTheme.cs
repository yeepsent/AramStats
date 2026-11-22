namespace AramStats;

public partial class Form1
{
    private string dashboardSeason = "시즌 3";
    private string? dashboardPersonId;
    private int dashboardTab;
    private static readonly Color DashboardBackground = Color.FromArgb(16, 19, 26);
    private static readonly Color DashboardSurface = Color.FromArgb(25, 30, 41);
    private static readonly Color DashboardText = Color.FromArgb(237, 241, 248);
    private static readonly Color DashboardSelection = Color.FromArgb(55, 47, 82);

    private sealed record DashboardMatch(DateTime? Date, int ChampionId, bool Win, string Kda);

    private List<DashboardMatch> GetLocalDashboardMatches(string personId, string season)
    {
        var matches = savedMatches.Values.Where(match => match.QueueId == 2400)
            .GroupBy(match => match.GameId)
            .ToDictionary(group => group.Key, group => group.First());
        if (string.Equals(personId, activePuuid, StringComparison.OrdinalIgnoreCase))
            return matches.Values.Where(match => season == "전체 시즌" || SeasonOf(match) == season)
                .OrderByDescending(match => MatchDate(match))
                .Select(match => new DashboardMatch(MatchDate(match), match.ChampionId, match.Win,
                    $"{match.Kills} / {match.Deaths} / {match.Assists}")).ToList();

        return savedDetails.Values.Where(detail => season == "전체 시즌" || DetailSeason(detail.GameId) == season)
            .SelectMany(detail => detail.Participants
                .Where(person => string.Equals(person.Puuid, personId, StringComparison.OrdinalIgnoreCase))
                .Select(person => new DashboardMatch(
                    matches.TryGetValue(detail.GameId, out var match) ? MatchDate(match) : null,
                    person.ChampionId, person.Win, $"{person.Kills} / {person.Deaths} / {person.Assists}")))
            .OrderByDescending(match => match.Date).ToList();
    }

    private static void ApplyDashboardTheme(Control control)
    {
        if (SystemInformation.HighContrast) return;
        control.BackColor = control is Form or TabPage ? DashboardBackground : DashboardSurface;
        control.ForeColor = DashboardText;

        if (control is Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(65, 74, 93);
        }
        if (control is DataGridView grid)
        {
            grid.BackgroundColor = DashboardSurface;
            grid.GridColor = Color.FromArgb(48, 56, 74);
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(32, 38, 54);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = DashboardText;
            grid.DefaultCellStyle.BackColor = DashboardSurface;
            grid.DefaultCellStyle.ForeColor = DashboardText;
            grid.DefaultCellStyle.SelectionBackColor = DashboardSelection;
            grid.DefaultCellStyle.SelectionForeColor = DashboardText;
            grid.RowTemplate.Height = 32;
            foreach (DataGridViewRow row in grid.Rows)
                row.Height = 32;
        }
        if (control is TabControl tabs)
        {
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.DrawItem -= DrawDashboardTab;
            tabs.DrawItem += DrawDashboardTab;
        }
        foreach (Control child in control.Controls)
            ApplyDashboardTheme(child);
    }

    private static void DrawDashboardTab(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabs || e.Index < 0 || e.Index >= tabs.TabCount) return;
        bool selected = e.Index == tabs.SelectedIndex;
        using var brush = new SolidBrush(selected ? DashboardSelection : DashboardSurface);
        e.Graphics.FillRectangle(brush, e.Bounds);
        TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, e.Bounds,
            DashboardText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        if (selected && tabs.Focused) e.DrawFocusRectangle();
    }
}
