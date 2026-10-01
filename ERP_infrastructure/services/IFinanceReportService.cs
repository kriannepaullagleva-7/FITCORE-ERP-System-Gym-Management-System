namespace ERP_infrastructure.services
{
    /// <summary>
    /// The financial statements, every one of them derived from the general ledger rather than
    /// recomputed from the operational tables.
    ///
    /// That is the point of keeping a ledger at all. An income statement assembled by adding up
    /// the sales table, the payments table and the expenses table will disagree with the
    /// balance sheet the first time somebody cancels something, because the three tables were
    /// summed under three slightly different rules. Read from one set of postings, the
    /// statements agree by construction - and when they do not, the trial balance says so.
    /// </summary>
    public interface IFinanceReportService
    {
        /// <summary>The headline figures for the Finance module's own overview screen.</summary>
        Task<FinanceOverview> GetOverviewAsync(DateTime? fromUtc = null, DateTime? toUtc = null);

        /// <summary>
        /// Revenue, cost of sales, gross profit, expenses and net income for a period.
        ///
        /// Cost of goods sold is shown above the line rather than among the operating
        /// expenses, so gross profit - what the gym makes on what it sells, before the cost of
        /// running the place - is visible on its own.
        /// </summary>
        Task<IncomeStatementView> GetIncomeStatementAsync(DateTime? fromUtc, DateTime? toUtc);

        /// <summary>
        /// Assets, liabilities and equity at a moment.
        ///
        /// Retained earnings are computed from the revenue and expense accounts rather than
        /// stored, because FitCore never closes the books with a year-end journal. That keeps
        /// the sheet balanced without asking an operator to perform a step they have no reason
        /// to know about.
        /// </summary>
        Task<BalanceSheetView> GetBalanceSheetAsync(DateTime? asOfUtc);

        /// <summary>
        /// Where the money actually moved, taken from the postings against cash and bank.
        ///
        /// Deliberately the direct method: a gym owner wants to know what came in from members
        /// and what went out to suppliers and staff, not a reconciliation of net income to
        /// working capital.
        /// </summary>
        Task<CashFlowView> GetCashFlowAsync(DateTime? fromUtc, DateTime? toUtc);

        /// <summary>What members and customers owe, aged by how long it has been outstanding.</summary>
        Task<AgedBalanceView> GetReceivablesAsync(DateTime? asOfUtc);

        /// <summary>What the gym owes suppliers, aged the same way.</summary>
        Task<AgedBalanceView> GetPayablesAsync(DateTime? asOfUtc);

        /// <summary>A budget against what was actually posted, by account.</summary>
        Task<BudgetVarianceView?> GetBudgetVarianceAsync(int budgetId, int? month);
    }
}
