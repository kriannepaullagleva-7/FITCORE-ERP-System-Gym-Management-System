namespace ERP_domain.entities
{
    /// <summary>
    /// The five kinds of account double-entry bookkeeping recognises.
    ///
    /// Stored as a short string rather than an enum for the same reason
    /// <see cref="PaymentCategories"/> is: the column stays readable in the database and a
    /// report can group on it without a lookup. The set is closed and the service validates
    /// against it before writing.
    /// </summary>
    public static class AccountTypes
    {
        /// <summary>What the gym owns: cash, bank, receivables, inventory, equipment.</summary>
        public const string Asset = "Asset";

        /// <summary>What the gym owes: payables, statutory contributions withheld, loans.</summary>
        public const string Liability = "Liability";

        /// <summary>The owner's stake and accumulated results.</summary>
        public const string Equity = "Equity";

        /// <summary>Money earned: memberships, goods sold, other income.</summary>
        public const string Revenue = "Revenue";

        /// <summary>Money consumed: cost of goods sold, salaries, rent, utilities.</summary>
        public const string Expense = "Expense";

        public static readonly IReadOnlyList<string> All =
            new[] { Asset, Liability, Equity, Revenue, Expense };

        public static bool IsKnown(string? value) =>
            value is not null && All.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

        public static string? Normalise(string? value) =>
            All.FirstOrDefault(t => string.Equals(t, (value ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Which side increases this kind of account.
        ///
        /// Assets and expenses are debit-normal; liabilities, equity and revenue are
        /// credit-normal. Everything that computes a balance goes through here rather than
        /// deciding for itself, which is what stops a report showing revenue as a negative
        /// number because somebody subtracted in the wrong direction.
        /// </summary>
        public static bool IsDebitNormal(string? accountType) =>
            Normalise(accountType) is Asset or Expense;

        /// <summary>
        /// The balance of an account given its total debits and credits.
        ///
        /// Positive always means "more of what this account is for": more cash, more revenue,
        /// more owed. A negative balance is therefore genuinely unusual and worth looking at,
        /// rather than an artefact of which side the account happens to sit on.
        /// </summary>
        public static decimal BalanceOf(string? accountType, decimal debits, decimal credits) =>
            IsDebitNormal(accountType) ? debits - credits : credits - debits;

        /// <summary>
        /// Whether this kind of account is closed off at the end of a year.
        ///
        /// Revenue and expense accounts measure a period and start again from zero; assets,
        /// liabilities and equity carry forward. The balance sheet reads the carrying ones and
        /// the income statement reads the others, so the distinction has to live somewhere
        /// both of them can see.
        /// </summary>
        public static bool IsTemporary(string? accountType) =>
            Normalise(accountType) is Revenue or Expense;
    }

    /// <summary>
    /// Stable identifiers for the accounts the system posts to automatically.
    ///
    /// A sale has to credit *the* sales revenue account, not an account that happens to be
    /// called "Sales" this week. The account code and name belong to the operator and can be
    /// renamed freely; this key belongs to the software and is what the posting rules look up.
    /// An account without a system key is one the operator created for their own purposes and
    /// nothing posts to it automatically.
    /// </summary>
    public static class AccountKeys
    {
        public const string Cash                = "cash";
        public const string Bank                = "bank";
        public const string AccountsReceivable  = "accounts-receivable";
        public const string Inventory           = "inventory";
        public const string Equipment           = "equipment";

        public const string AccountsPayable     = "accounts-payable";
        public const string SalaryPayable       = "salary-payable";
        public const string SssPayable          = "sss-payable";
        public const string PhilHealthPayable   = "philhealth-payable";
        public const string PagIbigPayable      = "pagibig-payable";
        public const string WithholdingTaxPayable = "withholding-tax-payable";

        public const string OwnerEquity         = "owner-equity";
        public const string RetainedEarnings    = "retained-earnings";

        public const string MembershipRevenue   = "membership-revenue";
        public const string ProductRevenue      = "product-revenue";
        public const string OtherRevenue        = "other-revenue";
        public const string SalesDiscounts      = "sales-discounts";
        public const string SalesReturns        = "sales-returns";

        public const string CostOfGoodsSold     = "cost-of-goods-sold";
        public const string SalariesExpense     = "salaries-expense";
        public const string OvertimeExpense     = "overtime-expense";
        public const string EmployerContributions = "employer-contributions";
        public const string InventoryShrinkage  = "inventory-shrinkage";

        /// <summary>The account an operating expense posts to when its category has none of its own.</summary>
        public const string GeneralExpense      = "general-expense";
    }

    /// <summary>
    /// One line of the chart of accounts.
    ///
    /// Every financial consequence of every operational event ends up posted against one of
    /// these, which is what makes "where did the money go?" answerable in one query rather
    /// than by adding up four modules and hoping.
    /// </summary>
    public class Account : IAuditable
    {
        public int AccountId { get; set; }

        /// <summary>Numeric code, unique within the tenant. Conventionally 1000-5999 by type.</summary>
        public string AccountCode { get; set; } = "";

        public string AccountName { get; set; } = "";

        /// <summary>One of <see cref="AccountTypes"/>.</summary>
        public string AccountType { get; set; } = AccountTypes.Asset;

        /// <summary>
        /// A finer grouping used by the statements: "Current Asset", "Cost of Sales",
        /// "Operating Expense". Presentation only - nothing posts on it.
        /// </summary>
        public string AccountSubType { get; set; } = "";

        /// <summary>Optional parent, so the chart can be shown as a tree and rolled up.</summary>
        public int? ParentAccountId { get; set; }

        /// <summary>
        /// The role this account plays in automatic posting, from <see cref="AccountKeys"/>.
        /// Empty for an account the operator added for their own use.
        /// </summary>
        public string SystemKey { get; set; } = "";

        /// <summary>
        /// Seeded with the chart and required by the posting rules, so it cannot be deleted or
        /// have its type changed. It can still be renamed and recoded - those belong to the
        /// operator.
        /// </summary>
        public bool IsSystemAccount { get; set; }

        public bool IsActive { get; set; } = true;

        public string Description { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public Account? ParentAccount { get; set; }

        /// <summary>True when a positive balance means a debit balance.</summary>
        public bool IsDebitNormal => AccountTypes.IsDebitNormal(AccountType);
    }
}
