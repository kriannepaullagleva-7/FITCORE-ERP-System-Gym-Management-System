using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// The company beside each of its branches, over one period.
    ///
    /// Deliberately unaffected by the branch picker in the topbar: this screen reads across
    /// every branch on purpose, which is what makes a comparison mean anything. That is also why
    /// it is an Admin subfeature - a branch manager is bound to one branch, and a comparison is
    /// by definition everybody else's figures.
    /// </summary>
    internal sealed class BranchPerformancePage : CrudPageBase<BranchSummaryDto>
    {
        private readonly Label _revenue;
        private readonly Label _revenueHint;
        private readonly Label _branches;
        private readonly Label _branchesHint;
        private readonly Label _top;
        private readonly Label _topHint;
        private readonly Label _net;
        private readonly Label _netHint;

        private DateTime _from = DateTime.Today.AddMonths(-1);
        private DateTime _to = DateTime.Today;

        public BranchPerformancePage(FitCoreSession session)
            : base(session, "Branch Performance",
                   "Every branch beside the company total, for the period you choose.",
                   "branch", "Branch name or code")
        {
            AddAction("Change period", ButtonTone.Secondary, ChangePeriodAsync, 126);

            StatsRow.Controls.Add(UiKit.StatCard("Company revenue", out _revenue, out _revenueHint, UiTheme.Primary));
            StatsRow.Controls.Add(UiKit.StatCard("Branches", out _branches, out _branchesHint, UiTheme.Info));
            StatsRow.Controls.Add(UiKit.StatCard("Strongest", out _top, out _topHint, UiTheme.Success));
            StatsRow.Controls.Add(UiKit.StatCard("Company net", out _net, out _netHint, UiTheme.Warning));
        }

        protected override bool SupportsAdd => false;
        protected override bool SupportsEdit => false;
        protected override bool SupportsDelete => false;

        protected override string EmptyHeadline => "No branches to compare";
        protected override string EmptyDetail =>
            "This company has no branches yet. Add them under System Administration → Branches, " +
            "and every record written afterwards will belong to one of them.";

        protected override async Task<List<BranchSummaryDto>?> FetchAsync()
        {
            var comparison = Unwrap(await Session.Branches.CompareAsync(_from, _to));

            if (comparison is null) return null;

            _revenue.Text = comparison.Company.TotalRevenue.ToString("C0");
            _revenueHint.Text = "memberships and sales";

            _branches.Text = comparison.Branches.Count.ToString("N0");
            _branchesHint.Text = $"{comparison.Company.Employees:N0} staff in total";

            _top.Text = comparison.TopBranchByRevenue ?? "—";
            _topHint.Text = comparison.LowestBranchByRevenue is { } lowest
                ? $"lowest: {lowest}"
                : "by revenue in this period";

            _net.Text = comparison.Company.NetPosition.ToString("C0");
            _netHint.Text = "revenue less expenses and pay";

            SetStatus($"Showing {_from:d MMM yyyy} to {_to:d MMM yyyy}.");

            // The consolidated row is listed first, in the same shape as the branches under it,
            // so a reader can check the parts against the whole without changing screens.
            var rows = new List<BranchSummaryDto> { comparison.Company };
            rows.AddRange(comparison.Branches);

            return rows;
        }

        protected override void DefineColumns()
        {
            Column(nameof(BranchSummaryDto.BranchName), "Branch", 150);
            Column(nameof(BranchSummaryDto.Members), "Members", 70);
            Column(nameof(BranchSummaryDto.ActiveSubscriptions), "Active subs", 80);
            Column(nameof(BranchSummaryDto.Employees), "Staff", 55);
            MoneyColumn(nameof(BranchSummaryDto.MembershipRevenue), "Membership", 90);
            MoneyColumn(nameof(BranchSummaryDto.SalesRevenue), "Sales", 90);
            MoneyColumn(nameof(BranchSummaryDto.TotalRevenue), "Revenue", 90);
            MoneyColumn(nameof(BranchSummaryDto.Expenses), "Expenses", 90);
            MoneyColumn(nameof(BranchSummaryDto.PayrollCost), "Payroll", 90);
            MoneyColumn(nameof(BranchSummaryDto.NetPosition), "Net", 90);
            Column(nameof(BranchSummaryDto.ShareOfCompanyRevenue), "Share %", 70);
        }

        protected override bool Matches(BranchSummaryDto b, string term) =>
            b.BranchName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            b.BranchCode.Contains(term, StringComparison.OrdinalIgnoreCase);

        private Task ChangePeriodAsync()
        {
            var fields = new List<FieldSpec>
            {
                new("from", "From", FieldKind.Date) { Required = true, Value = _from },
                new("to", "To", FieldKind.Date) { Required = true, Value = _to }
            };

            var applied = EditDialog.Run(this, "Choose a period",
                "Members, staff and active subscriptions are counted as they stand now; " +
                "revenue, expenses and pay are counted within the period.",
                fields, f =>
                {
                    if (f.First(x => x.Key == "from").Date > f.First(x => x.Key == "to").Date)
                    {
                        return Task.FromResult<string?>("The start date must be on or before the end date.");
                    }

                    _from = f.First(x => x.Key == "from").Date;
                    _to = f.First(x => x.Key == "to").Date;

                    return Task.FromResult<string?>(null);
                }, "Apply");

            return applied ? LoadAsync() : Task.CompletedTask;
        }
    }
}
