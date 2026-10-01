using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// Whether this tenant's records still agree with each other.
    ///
    /// A diagnostic, not a tool. There is no repair button, because the server offers no repair
    /// endpoint: an automatic fix for a problem nobody has understood yet is how one bad row
    /// becomes a thousand. The two findings that do have a supported recovery - payments and
    /// sales that never reached the ledger - say so in their own detail text and point at the
    /// Finance catch-up sweep, which already exists and is idempotent.
    ///
    /// The clean result is the one to design for, because it is the one an owner will see
    /// almost every time. "Nothing to report" has to look like an answer rather than like a
    /// screen that failed to load, which is why the count of checks that ran is on the strip
    /// even when every one of them passed.
    /// </summary>
    internal sealed class DataIntegrityPage : CrudPageBase<IntegrityCheckDto>
    {
        private readonly ComboBox _show;
        private readonly Label _summary;

        private IntegrityReportDto? _report;

        public DataIntegrityPage(FitCoreSession session)
            : base(session, "Data Integrity",
                   "Whether this tenant's records still agree with each other. Read-only.",
                   "check", "Area, check or finding")
        {
            AddAction("Run checks", ButtonTone.Primary, () => LoadAsync(), 110);
            AddAction("View detail", ButtonTone.Secondary, () => ShowDetailAsync(), 112);

            FilterBar.Controls.Add(UiKit.FilterLabel("Show"));

            _show = UiKit.Select(150);
            _show.DisplayMember = "Value";
            _show.ValueMember = "Key";
            _show.DataSource = new List<KeyValuePair<string, string>>
            {
                new("issues", "Issues only"),
                new("all", "Every check"),
                new("passed", "Passed only")
            };
            _show.SelectedIndexChanged += (_, _) => Rebind();
            FilterBar.Controls.Add(_show);

            _summary = new Label
            {
                AutoSize = true,
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                Margin = new Padding(16, 10, 0, 0),
                UseMnemonic = false
            };
            FilterBar.Controls.Add(_summary);

            // Severity is a property of the row rather than of the grid, so it is painted on
            // bind - which also covers a re-bind from the search box.
            Grid.DataBindingComplete += (_, _) => PaintSeverity();
        }

        protected override bool SupportsAdd => false;
        protected override bool SupportsEdit => false;
        protected override bool SupportsDelete => false;

        protected override string EmptyHeadline =>
            _report is null ? "No checks run yet" : "Nothing to report";

        protected override string EmptyDetail =>
            _report is null
                ? "Run the checks to sweep this tenant's records for inconsistencies."
                : $"All {_report.ChecksRun} checks passed. Orphaned rows, unbalanced journal " +
                  "entries, stock that disagrees with its movement ledger and payslips that " +
                  "disagree with themselves were all looked for, and none were found.";

        protected override async Task<List<IntegrityCheckDto>?> FetchAsync()
        {
            var report = Unwrap(await Session.Integrity.RunAsync());
            if (report is null) return null;

            _report = report;

            return RowsFor(report);
        }

        /// <summary>The rows the current filter admits, newest concern first.</summary>
        private List<IntegrityCheckDto> RowsFor(IntegrityReportDto report)
        {
            var rows = (_show.SelectedValue?.ToString()) switch
            {
                "all" => report.Checks,
                "passed" => report.Checks.Where(c => c.Passed).ToList(),
                _ => report.Checks.Where(c => !c.Passed).ToList()
            };

            // Critical findings first, then by how many rows are involved. A clean sweep in
            // "issues only" mode is an empty list, which is the correct and welcome answer.
            return rows
                .OrderBy(c => c.Passed)
                .ThenBy(c => c.Severity == "Critical" ? 0 : c.Severity == "Warning" ? 1 : 2)
                .ThenByDescending(c => c.IssueCount)
                .ThenBy(c => c.Area, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>Re-applies the filter without asking the server to sweep again.</summary>
        private void Rebind()
        {
            if (_report is null) return;

            Items = RowsFor(_report);
            ApplyFilter();
            AfterLoad();
        }

        protected override void DefineColumns()
        {
            Column(nameof(IntegrityCheckDto.Area), "Area", 90);
            Column(nameof(IntegrityCheckDto.Check), "What was checked", 300);
            StatusColumn(nameof(IntegrityCheckDto.Severity), "Severity", 80);
            Column(nameof(IntegrityCheckDto.IssueCount), "Issues", 60, rightAlign: true);
            Column(nameof(IntegrityCheckDto.Detail), "What it means", 320);
        }

        protected override bool Matches(IntegrityCheckDto c, string term) =>
            (c.Area ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (c.Check ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (c.Detail ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (c.Severity ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override void AfterLoad()
        {
            if (_report is null) return;

            var report = _report;

            _summary.Text = report.IsClean
                ? $"{report.ChecksRun:N0} checks ran, all passed — no inconsistencies found"
                : $"{report.ChecksRun:N0} checks ran, {report.ChecksPassed:N0} passed      " +
                  $"{report.IssuesFound:N0} issue(s) across " +
                  $"{report.ChecksRun - report.ChecksPassed:N0} check(s)" +
                  (report.CriticalCount == 0 ? "" : $"      {report.CriticalCount:N0} critical");

            _summary.ForeColor = report.IsClean
                ? UiTheme.Success
                : report.CriticalCount > 0 ? UiTheme.Danger : UiTheme.Warning;

            SetStatus(report.IsClean
                ? $"Checked {UiKit.Date(report.CheckedAtUtc)}: this tenant's records are consistent."
                : $"Checked {UiKit.Date(report.CheckedAtUtc)}: " +
                  $"{UiKit.Plural(report.IssuesFound, "issue")} found.");
        }

        /// <summary>Greys a passing row back, so a failure is what the eye lands on.</summary>
        private void PaintSeverity()
        {
            foreach (DataGridViewRow row in Grid.Rows)
            {
                if (row.DataBoundItem is not IntegrityCheckDto check) continue;

                row.DefaultCellStyle.ForeColor = check.Passed
                    ? UiTheme.TextMuted
                    : check.Severity == "Critical" ? UiTheme.Danger : UiTheme.TextPrimary;
            }
        }

        /// <summary>
        /// The offending record ids behind a finding, so it can actually be chased down. The
        /// server caps the list deliberately - a check that is wrong about ten thousand rows
        /// should say how many rather than list them.
        /// </summary>
        private Task ShowDetailAsync()
        {
            var check = Selected;

            if (check is null)
            {
                ShowError("Select a check to see what it found.");
                return Task.CompletedTask;
            }

            if (check.Passed)
            {
                UiKit.Info($"{check.Check}.\r\n\r\nThis check passed - nothing was found.",
                    $"{check.Area} · {check.Key}");
                return Task.CompletedTask;
            }

            var examples = check.Examples.Count == 0
                ? "(no record ids were captured)"
                : string.Join(", ", check.Examples) +
                  (check.IssueCount > check.Examples.Count
                      ? $"  … and {check.IssueCount - check.Examples.Count:N0} more"
                      : "");

            UiKit.Info(
                $"{check.Check}.\r\n\r\n" +
                $"{check.Severity}: {UiKit.Plural(check.IssueCount, "record")} affected.\r\n\r\n" +
                $"{check.Detail}\r\n\r\nRecords: {examples}",
                $"{check.Area} · {check.Key}");

            return Task.CompletedTask;
        }
    }
}
