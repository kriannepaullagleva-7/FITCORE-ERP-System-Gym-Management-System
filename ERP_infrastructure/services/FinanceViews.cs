namespace ERP_infrastructure.services
{
    // ---------------------------------------------------------------------- chart of accounts

    /// <summary>One account with its balance, as the chart of accounts screen shows it.</summary>
    public class AccountView
    {
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string AccountType { get; set; } = "";
        public string AccountSubType { get; set; } = "";
        public string SystemKey { get; set; } = "";
        public bool IsSystemAccount { get; set; }
        public bool IsActive { get; set; }
        public string Description { get; set; } = "";
        public int? ParentAccountId { get; set; }
        public string ParentAccountName { get; set; } = "";

        /// <summary>Total debits and credits posted, and the balance they come to.</summary>
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public decimal Balance { get; set; }

        /// <summary>How many postings the account carries, so a chart shows which are in use.</summary>
        public int EntryCount { get; set; }

        public bool IsDebitNormal { get; set; }
    }

    // ---------------------------------------------------------------------- journal

    public class JournalLineView
    {
        public int JournalEntryLineId { get; set; }
        public int LineNumber { get; set; }
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string AccountType { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public int? MemberId { get; set; }
        public int? SupplierId { get; set; }
        public int? EmployeeId { get; set; }
    }

    public class JournalEntryView
    {
        public int JournalEntryId { get; set; }
        public string EntryNo { get; set; } = "";
        public DateTime EntryDate { get; set; }
        public string Reference { get; set; } = "";
        public string Memo { get; set; } = "";
        public string Source { get; set; } = "";
        public string SourceModule { get; set; } = "";
        public string SourceEntityName { get; set; } = "";
        public string? SourceEntityId { get; set; }
        public string Status { get; set; } = "";
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public int PeriodYear { get; set; }
        public int PeriodMonth { get; set; }
        public string ProcessedBy { get; set; } = "";
        public int? ReversedByEntryId { get; set; }
        public int? ReversesEntryId { get; set; }
        public DateTime CreatedAt { get; set; }

        public List<JournalLineView> Lines { get; set; } = new();

        /// <summary>A one-line description of what the entry did, for a list.</summary>
        public string Summary { get; set; } = "";

        public bool IsBalanced => TotalDebit == TotalCredit;
        public bool IsEditable { get; set; }

        /// <summary>
        /// True once a reversing entry has been written against this one.
        ///
        /// A reversed entry is still posted and still counts - the pair nets to zero - so this
        /// is a fact about the entry rather than a change to its status. The grid shows it as
        /// "Reversed" for the reader's benefit.
        /// </summary>
        public bool IsReversed => ReversedByEntryId.HasValue;

        /// <summary>What the status column shows: the status, unless it has been undone.</summary>
        public string DisplayStatus =>
            IsReversed ? "Reversed"
            : ReversesEntryId.HasValue ? "Reversal"
            : Status;
    }

    /// <summary>One line the caller wants posted. Exactly one of the two amounts is non-zero.</summary>
    public class JournalLineRequest
    {
        public int AccountId { get; set; }
        public string Description { get; set; } = "";
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public int? MemberId { get; set; }
        public int? SupplierId { get; set; }
        public int? EmployeeId { get; set; }
    }

    // ---------------------------------------------------------------------- general ledger

    /// <summary>One posting against one account, with the balance it left behind.</summary>
    public class LedgerRow
    {
        public int JournalEntryId { get; set; }
        public string EntryNo { get; set; } = "";
        public DateTime EntryDate { get; set; }
        public string Reference { get; set; } = "";
        public string Description { get; set; } = "";
        public string Source { get; set; } = "";
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }

        /// <summary>Running balance after this posting, in the account's normal direction.</summary>
        public decimal Balance { get; set; }
    }

    public class GeneralLedgerView
    {
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string AccountType { get; set; } = "";
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }

        /// <summary>The balance carried in from before the period. Zero for a temporary account.</summary>
        public decimal OpeningBalance { get; set; }

        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public decimal ClosingBalance { get; set; }

        public List<LedgerRow> Rows { get; set; } = new();
    }

    // ---------------------------------------------------------------------- statements

    public class TrialBalanceRow
    {
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string AccountType { get; set; } = "";
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }

    public class TrialBalanceView
    {
        public DateTime AsOfUtc { get; set; }
        public List<TrialBalanceRow> Rows { get; set; } = new();
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }

        /// <summary>
        /// The two sides must agree. They always will unless an entry was written outside the
        /// journal service - which is exactly what this figure exists to reveal.
        /// </summary>
        public bool IsBalanced => TotalDebit == TotalCredit;
        public decimal Difference => TotalDebit - TotalCredit;
    }

    /// <summary>One line of a financial statement: an account, or a subtotal.</summary>
    public class StatementLine
    {
        public string Label { get; set; } = "";
        public string AccountCode { get; set; } = "";
        public int? AccountId { get; set; }
        public decimal Amount { get; set; }

        /// <summary>Nesting level, so the screen can indent rather than guess.</summary>
        public int Depth { get; set; }

        /// <summary>True for a total or subtotal, which is drawn differently.</summary>
        public bool IsSubtotal { get; set; }
    }

    public class IncomeStatementView
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }

        public decimal Revenue { get; set; }
        public decimal SalesDiscounts { get; set; }
        public decimal SalesReturns { get; set; }

        /// <summary>Revenue less discounts and returns.</summary>
        public decimal NetRevenue { get; set; }

        public decimal CostOfGoodsSold { get; set; }

        /// <summary>Net revenue less cost of goods sold.</summary>
        public decimal GrossProfit { get; set; }

        public decimal PayrollExpenses { get; set; }
        public decimal OperatingExpenses { get; set; }
        public decimal TotalExpenses { get; set; }

        /// <summary>Gross profit less every expense below the line.</summary>
        public decimal NetIncome { get; set; }

        /// <summary>Gross profit as a share of net revenue.</summary>
        public decimal GrossMarginPercent { get; set; }
        public decimal NetMarginPercent { get; set; }

        public List<StatementLine> RevenueLines { get; set; } = new();
        public List<StatementLine> CostOfSalesLines { get; set; } = new();
        public List<StatementLine> ExpenseLines { get; set; } = new();
    }

    public class BalanceSheetView
    {
        public DateTime AsOfUtc { get; set; }

        public decimal CurrentAssets { get; set; }
        public decimal FixedAssets { get; set; }
        public decimal TotalAssets { get; set; }

        public decimal CurrentLiabilities { get; set; }
        public decimal TotalLiabilities { get; set; }

        public decimal ContributedEquity { get; set; }

        /// <summary>
        /// Profit earned and not drawn out, computed from the revenue and expense accounts
        /// rather than stored. Nothing closes the books in FitCore, so this is what keeps the
        /// sheet balanced without an explicit year-end journal.
        /// </summary>
        public decimal RetainedEarnings { get; set; }
        public decimal TotalEquity { get; set; }

        public decimal LiabilitiesAndEquity { get; set; }

        /// <summary>Assets should equal liabilities plus equity. This says whether they do.</summary>
        public bool IsBalanced => Math.Abs(TotalAssets - LiabilitiesAndEquity) < 0.01m;

        public List<StatementLine> AssetLines { get; set; } = new();
        public List<StatementLine> LiabilityLines { get; set; } = new();
        public List<StatementLine> EquityLines { get; set; } = new();
    }

    public class CashFlowView
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }

        public decimal OpeningCash { get; set; }

        public decimal CashFromCustomers { get; set; }
        public decimal CashToSuppliers { get; set; }
        public decimal CashToEmployees { get; set; }
        public decimal CashForExpenses { get; set; }
        public decimal OtherMovements { get; set; }

        public decimal NetCashFlow { get; set; }
        public decimal ClosingCash { get; set; }

        public List<TrendPoint> ByDay { get; set; } = new();
        public List<StatementLine> Lines { get; set; } = new();
    }

    // ---------------------------------------------------------------------- receivables and payables

    /// <summary>One debt, bucketed by how long it has been outstanding.</summary>
    public class AgedRow
    {
        public string Party { get; set; } = "";

        /// <summary>Null for a walk-in - the balance still belongs to somebody, just not a Member row.</summary>
        public int? PartyId { get; set; }
        public string Document { get; set; } = "";
        public int DocumentId { get; set; }
        public DateTime DocumentDate { get; set; }
        public int DaysOutstanding { get; set; }

        public decimal Total { get; set; }
        public decimal Settled { get; set; }
        public decimal Balance { get; set; }

        /// <summary>Current, 1-30, 31-60, 61-90 or 90+.</summary>
        public string Bucket { get; set; } = "";
    }

    public class AgedBalanceView
    {
        public DateTime AsOfUtc { get; set; }
        public string Title { get; set; } = "";

        public decimal Current { get; set; }
        public decimal Days1To30 { get; set; }
        public decimal Days31To60 { get; set; }
        public decimal Days61To90 { get; set; }
        public decimal Over90Days { get; set; }
        public decimal Total { get; set; }

        public List<AgedRow> Rows { get; set; } = new();

        /// <summary>Which bucket a debt this many days old belongs in.</summary>
        public static string BucketFor(int daysOutstanding) =>
            daysOutstanding <= 0 ? "Current"
            : daysOutstanding <= 30 ? "1-30 days"
            : daysOutstanding <= 60 ? "31-60 days"
            : daysOutstanding <= 90 ? "61-90 days"
            : "Over 90 days";
    }

    // ---------------------------------------------------------------------- budgets

    public class BudgetLineView
    {
        public int BudgetLineId { get; set; }
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string AccountType { get; set; } = "";
        public int Month { get; set; }
        public decimal Amount { get; set; }
        public string Notes { get; set; } = "";
    }

    public class BudgetView
    {
        public int BudgetId { get; set; }
        public string Name { get; set; } = "";
        public int Year { get; set; }
        public string Status { get; set; } = "";
        public string Notes { get; set; } = "";
        public string ApprovedBy { get; set; } = "";
        public DateTime? ApprovedAt { get; set; }
        public decimal TotalBudgeted { get; set; }
        public int LineCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<BudgetLineView> Lines { get; set; } = new();
    }

    public class BudgetVarianceRow
    {
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string AccountType { get; set; } = "";
        public decimal Budgeted { get; set; }
        public decimal Actual { get; set; }

        /// <summary>
        /// Actual less budgeted, signed so that positive is always bad for a cost and good for
        /// revenue - overspending and overachieving are not the same news, and a report that
        /// shows both as "+1,200" is not worth reading.
        /// </summary>
        public decimal Variance { get; set; }
        public decimal VariancePercent { get; set; }
        public bool IsFavourable { get; set; }
    }

    public class BudgetVarianceView
    {
        public int BudgetId { get; set; }
        public string BudgetName { get; set; } = "";
        public int Year { get; set; }

        /// <summary>Null for the whole year.</summary>
        public int? Month { get; set; }

        public decimal TotalBudgeted { get; set; }
        public decimal TotalActual { get; set; }
        public decimal TotalVariance { get; set; }

        public List<BudgetVarianceRow> Rows { get; set; } = new();
    }

    // ---------------------------------------------------------------------- banking

    public class BankAccountView
    {
        public int BankAccountId { get; set; }
        public string AccountName { get; set; } = "";
        public string BankName { get; set; } = "";
        public string AccountNumber { get; set; } = "";
        public string Kind { get; set; } = "";
        public int LedgerAccountId { get; set; }
        public string LedgerAccountName { get; set; } = "";
        public decimal OpeningBalance { get; set; }
        public decimal CurrentBalance { get; set; }

        /// <summary>The same balance as the ledger sees it. A difference wants explaining.</summary>
        public decimal LedgerBalance { get; set; }
        public decimal Difference => CurrentBalance - LedgerBalance;

        public int UnreconciledCount { get; set; }
        public bool IsActive { get; set; }
        public string Notes { get; set; } = "";
    }

    public class BankTransactionView
    {
        public int BankTransactionId { get; set; }
        public int BankAccountId { get; set; }
        public string BankAccountName { get; set; } = "";
        public DateTime TransactionDate { get; set; }
        public string Direction { get; set; } = "";
        public decimal Amount { get; set; }
        public decimal SignedAmount { get; set; }
        public decimal BalanceAfter { get; set; }
        public string Reference { get; set; } = "";
        public string Description { get; set; } = "";
        public bool IsReconciled { get; set; }
        public DateTime? ReconciledAt { get; set; }
        public string ReconciledBy { get; set; } = "";
        public int? JournalEntryId { get; set; }
        public string PerformedBy { get; set; } = "";
    }

    public class ReconciliationView
    {
        public int BankAccountId { get; set; }
        public string BankAccountName { get; set; } = "";
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }

        public decimal StatementBalance { get; set; }
        public decimal LedgerBalance { get; set; }
        public decimal ReconciledTotal { get; set; }
        public decimal UnreconciledTotal { get; set; }
        public decimal Difference { get; set; }

        public int ReconciledCount { get; set; }
        public int UnreconciledCount { get; set; }

        public List<BankTransactionView> Unreconciled { get; set; } = new();
    }

    // ---------------------------------------------------------------------- periods

    public class FinancialPeriodView
    {
        public int FinancialPeriodId { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public string DisplayName { get; set; } = "";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Status { get; set; } = "";
        public DateTime? ClosedAt { get; set; }
        public string ClosedBy { get; set; } = "";
        public string Notes { get; set; } = "";

        /// <summary>What has been posted into the period, so closing it is an informed act.</summary>
        public int EntryCount { get; set; }
        public decimal TotalPosted { get; set; }
    }

    // ---------------------------------------------------------------------- overview

    /// <summary>
    /// The Finance module's own summary: the figures an owner asks for before they ask for a
    /// statement. Every one of them is read from the ledger rather than recomputed from the
    /// operational tables, so it agrees with the income statement by construction.
    /// </summary>
    public class FinanceOverview
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }

        public decimal Revenue { get; set; }
        public decimal CostOfGoodsSold { get; set; }
        public decimal GrossProfit { get; set; }
        public decimal OperatingExpenses { get; set; }
        public decimal PayrollExpenses { get; set; }
        public decimal NetIncome { get; set; }

        public decimal CashOnHand { get; set; }
        public decimal AccountsReceivable { get; set; }
        public decimal AccountsPayable { get; set; }
        public decimal InventoryValue { get; set; }

        public decimal TotalAssets { get; set; }
        public decimal TotalLiabilities { get; set; }
        public decimal TotalEquity { get; set; }

        public int UnpostedCount { get; set; }
        public int OpenPeriods { get; set; }

        public List<TrendPoint> RevenueByMonth { get; set; } = new();
        public List<TrendPoint> ExpensesByMonth { get; set; } = new();
        public List<TrendPoint> NetIncomeByMonth { get; set; } = new();
        public List<CategorySlice> ExpenseBreakdown { get; set; } = new();
        public List<CategorySlice> RevenueBreakdown { get; set; } = new();
    }
}
