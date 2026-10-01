namespace ERP_Project1.Api
{
    // Client-side copies of the Finance contracts, duplicated rather than shared for the same
    // reason every other DTO here is: the desktop keeps zero project references and cannot
    // reach EF Core. The server is authoritative for every figure below.

    // ---------------------------------------------------------------------- chart of accounts

    public static class AccountTypes
    {
        public const string Asset = "Asset";
        public const string Liability = "Liability";
        public const string Equity = "Equity";
        public const string Revenue = "Revenue";
        public const string Expense = "Expense";

        public static readonly string[] All = { Asset, Liability, Equity, Revenue, Expense };
    }

    public class AccountDto
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
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public decimal Balance { get; set; }
        public int EntryCount { get; set; }
        public bool IsDebitNormal { get; set; }

        /// <summary>"1000 · Cash on Hand", for a picker.</summary>
        public string Display => $"{AccountCode} · {AccountName}";
    }

    public class CreateAccountDto
    {
        public string AccountCode { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string AccountType { get; set; } = AccountTypes.Asset;
        public string AccountSubType { get; set; } = "";
        public string Description { get; set; } = "";
        public int? ParentAccountId { get; set; }
    }

    public class UpdateAccountDto : CreateAccountDto
    {
        public bool IsActive { get; set; } = true;
    }

    // ---------------------------------------------------------------------- journal

    public class JournalLineDto
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

    public class JournalEntryDto
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
        public List<JournalLineDto> Lines { get; set; } = new();
        public string Summary { get; set; } = "";
        public bool IsEditable { get; set; }

        /// <summary>
        /// True once a reversing entry has been written against this one. A reversed entry is
        /// still posted and still counts - the pair nets to zero - so this is a fact about the
        /// entry rather than a change to its status.
        /// </summary>
        public bool IsReversed { get; set; }

        /// <summary>What the status column shows: the status, unless it has been undone.</summary>
        public string DisplayStatus { get; set; } = "";
    }

    public class CreateJournalEntryDto
    {
        public DateTime EntryDate { get; set; } = DateTime.UtcNow;
        public string Memo { get; set; } = "";
        public string Reference { get; set; } = "";
        public bool Post { get; set; } = true;
        public List<JournalLineDto> Lines { get; set; } = new();
    }

    public class UpdateJournalEntryDto
    {
        public DateTime EntryDate { get; set; }
        public string Memo { get; set; } = "";
        public string Reference { get; set; } = "";
        public List<JournalLineDto> Lines { get; set; } = new();
    }

    public class ReasonDto
    {
        public string Reason { get; set; } = "";
    }

    // ---------------------------------------------------------------------- ledger

    public class LedgerRowDto
    {
        public int JournalEntryId { get; set; }
        public string EntryNo { get; set; } = "";
        public DateTime EntryDate { get; set; }
        public string Reference { get; set; } = "";
        public string Description { get; set; } = "";
        public string Source { get; set; } = "";
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal Balance { get; set; }
    }

    public class GeneralLedgerDto
    {
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string AccountType { get; set; } = "";
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }
        public decimal OpeningBalance { get; set; }
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public decimal ClosingBalance { get; set; }
        public List<LedgerRowDto> Rows { get; set; } = new();
    }

    // ---------------------------------------------------------------------- statements

    public class TrialBalanceRowDto
    {
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string AccountType { get; set; } = "";
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }

    public class TrialBalanceDto
    {
        public DateTime AsOfUtc { get; set; }
        public List<TrialBalanceRowDto> Rows { get; set; } = new();
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public bool IsBalanced { get; set; }
        public decimal Difference { get; set; }
    }

    public class StatementLineDto
    {
        public string Label { get; set; } = "";
        public string AccountCode { get; set; } = "";
        public int? AccountId { get; set; }
        public decimal Amount { get; set; }
        public int Depth { get; set; }
        public bool IsSubtotal { get; set; }
    }

    public class IncomeStatementDto
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }
        public decimal Revenue { get; set; }
        public decimal SalesDiscounts { get; set; }
        public decimal SalesReturns { get; set; }
        public decimal NetRevenue { get; set; }
        public decimal CostOfGoodsSold { get; set; }
        public decimal GrossProfit { get; set; }
        public decimal PayrollExpenses { get; set; }
        public decimal OperatingExpenses { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal NetIncome { get; set; }
        public decimal GrossMarginPercent { get; set; }
        public decimal NetMarginPercent { get; set; }
        public List<StatementLineDto> RevenueLines { get; set; } = new();
        public List<StatementLineDto> CostOfSalesLines { get; set; } = new();
        public List<StatementLineDto> ExpenseLines { get; set; } = new();
    }

    public class BalanceSheetDto
    {
        public DateTime AsOfUtc { get; set; }
        public decimal CurrentAssets { get; set; }
        public decimal FixedAssets { get; set; }
        public decimal TotalAssets { get; set; }
        public decimal CurrentLiabilities { get; set; }
        public decimal TotalLiabilities { get; set; }
        public decimal ContributedEquity { get; set; }
        public decimal RetainedEarnings { get; set; }
        public decimal TotalEquity { get; set; }
        public decimal LiabilitiesAndEquity { get; set; }
        public bool IsBalanced { get; set; }
        public List<StatementLineDto> AssetLines { get; set; } = new();
        public List<StatementLineDto> LiabilityLines { get; set; } = new();
        public List<StatementLineDto> EquityLines { get; set; } = new();
    }

    public class CashFlowDto
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
        public List<TrendPointDto> ByDay { get; set; } = new();
        public List<StatementLineDto> Lines { get; set; } = new();
    }

    // ---------------------------------------------------------------------- ageing

    public class AgedRowDto
    {
        public string Party { get; set; } = "";
        public int? PartyId { get; set; }
        public string Document { get; set; } = "";
        public int DocumentId { get; set; }
        public DateTime DocumentDate { get; set; }
        public int DaysOutstanding { get; set; }
        public decimal Total { get; set; }
        public decimal Settled { get; set; }
        public decimal Balance { get; set; }
        public string Bucket { get; set; } = "";
    }

    public class AgedBalanceDto
    {
        public DateTime AsOfUtc { get; set; }
        public string Title { get; set; } = "";
        public decimal Current { get; set; }
        public decimal Days1To30 { get; set; }
        public decimal Days31To60 { get; set; }
        public decimal Days61To90 { get; set; }
        public decimal Over90Days { get; set; }
        public decimal Total { get; set; }
        public List<AgedRowDto> Rows { get; set; } = new();
    }

    // ---------------------------------------------------------------------- periods

    public class FinancialPeriodDto
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
        public int EntryCount { get; set; }
        public decimal TotalPosted { get; set; }
    }

    public class ClosePeriodDto
    {
        public string Notes { get; set; } = "";
    }

    // ---------------------------------------------------------------------- budgets

    public class BudgetLineDto
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

    public class BudgetDto
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
        public List<BudgetLineDto> Lines { get; set; } = new();
    }

    public class CreateBudgetDto
    {
        public string Name { get; set; } = "";
        public int Year { get; set; } = DateTime.UtcNow.Year;
        public string Notes { get; set; } = "";
    }

    public class SetBudgetLineDto
    {
        public int AccountId { get; set; }
        public int Month { get; set; }
        public decimal Amount { get; set; }
        public string Notes { get; set; } = "";
    }

    public class SpreadBudgetDto
    {
        public int AccountId { get; set; }
        public decimal AnnualAmount { get; set; }
    }

    public class BudgetVarianceRowDto
    {
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string AccountType { get; set; } = "";
        public decimal Budgeted { get; set; }
        public decimal Actual { get; set; }
        public decimal Variance { get; set; }
        public decimal VariancePercent { get; set; }
        public bool IsFavourable { get; set; }
    }

    public class BudgetVarianceDto
    {
        public int BudgetId { get; set; }
        public string BudgetName { get; set; } = "";
        public int Year { get; set; }
        public int? Month { get; set; }
        public decimal TotalBudgeted { get; set; }
        public decimal TotalActual { get; set; }
        public decimal TotalVariance { get; set; }
        public List<BudgetVarianceRowDto> Rows { get; set; } = new();
    }

    // ---------------------------------------------------------------------- banking

    public static class CashAccountKinds
    {
        public const string Cash = "Cash";
        public const string Bank = "Bank";
        public const string EWallet = "E-Wallet";

        public static readonly string[] All = { Cash, Bank, EWallet };
    }

    public class BankAccountDto
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
        public decimal LedgerBalance { get; set; }
        public decimal Difference { get; set; }
        public int UnreconciledCount { get; set; }
        public bool IsActive { get; set; }
        public string Notes { get; set; } = "";
    }

    public class CreateBankAccountDto
    {
        public string AccountName { get; set; } = "";
        public string BankName { get; set; } = "";
        public string AccountNumber { get; set; } = "";
        public string Kind { get; set; } = CashAccountKinds.Bank;
        public decimal OpeningBalance { get; set; }
        public string Notes { get; set; } = "";
    }

    public class UpdateBankAccountDto : CreateBankAccountDto
    {
        public bool IsActive { get; set; } = true;
    }

    public class BankTransactionDto
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

    public class RecordBankTransactionDto
    {
        public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
        public string Direction { get; set; } = "In";
        public decimal Amount { get; set; }
        public string Reference { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public class SetReconciledDto
    {
        public bool IsReconciled { get; set; } = true;
    }

    public class ReconcileDto
    {
        public List<int> BankTransactionIds { get; set; } = new();
    }

    public class ReconciliationDto
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
        public List<BankTransactionDto> Unreconciled { get; set; } = new();
    }

    // ---------------------------------------------------------------------- overview

    public class FinanceOverviewDto
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
        public List<TrendPointDto> RevenueByMonth { get; set; } = new();
        public List<TrendPointDto> ExpensesByMonth { get; set; } = new();
        public List<TrendPointDto> NetIncomeByMonth { get; set; } = new();
        public List<CategorySliceDto> ExpenseBreakdown { get; set; } = new();
        public List<CategorySliceDto> RevenueBreakdown { get; set; } = new();
    }

    public class PostOutstandingResultDto
    {
        public int Posted { get; set; }
        public int Remaining { get; set; }
    }
}
