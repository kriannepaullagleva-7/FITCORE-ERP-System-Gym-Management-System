namespace ERP_domain.entities
{
    /// <summary>One account as the standard chart defines it, before a tenant edits anything.</summary>
    public sealed record ChartAccountSeed(
        string Code,
        string Name,
        string Type,
        string SubType,
        string SystemKey,
        string Description);

    /// <summary>
    /// The chart of accounts a FitCore tenant starts with.
    ///
    /// Seeded rather than left to the operator because the posting rules need somewhere to
    /// post: a sale has to credit product revenue and debit cost of goods sold on the day the
    /// gym opens, not once somebody has read a bookkeeping primer. Every account here is
    /// marked as a system account, which means it can be renamed and recoded but not deleted
    /// or retyped - the operator owns how it reads, the software owns what it means.
    ///
    /// Codes follow the usual convention: 1000s assets, 2000s liabilities, 3000s equity,
    /// 4000s revenue, 5000s expenses. The gaps are deliberate, so a tenant can insert their own
    /// accounts between them without renumbering.
    /// </summary>
    public static class ChartOfAccounts
    {
        // Sub-types, which are what the statements group on.
        public const string CurrentAsset = "Current Asset";
        public const string FixedAsset = "Fixed Asset";
        public const string CurrentLiability = "Current Liability";
        public const string OwnersEquity = "Owner's Equity";
        public const string OperatingRevenue = "Operating Revenue";
        public const string ContraRevenue = "Contra Revenue";
        public const string CostOfSales = "Cost of Sales";
        public const string PayrollExpense = "Payroll Expense";
        public const string OperatingExpense = "Operating Expense";

        /// <summary>
        /// The system key for the account an expense category posts to.
        ///
        /// Kept as a derived key rather than a column on the category list so adding a category
        /// and adding its account stay one change. An expense whose category has no account of
        /// its own falls back to <see cref="AccountKeys.GeneralExpense"/>, which is why a
        /// tenant that never touches the chart still produces a complete profit and loss.
        /// </summary>
        public static string ExpenseAccountKey(string category) =>
            "expense." + (category ?? "").Trim().ToLowerInvariant()
                .Replace(" & ", "-").Replace(' ', '-');

        /// <summary>
        /// Every account a fresh tenant gets.
        ///
        /// Note what cost of goods sold is not: an operating expense. Stock bought for resale
        /// is an asset (1200 Inventory) until the moment it is sold, and only then becomes
        /// 5000 Cost of Goods Sold. Keeping it in its own sub-type is what lets the income
        /// statement show gross profit - revenue less cost of sales - before operating expenses
        /// are taken off at all.
        /// </summary>
        public static readonly IReadOnlyList<ChartAccountSeed> Standard = new[]
        {
            // ---------------------------------------------------------------- assets
            new ChartAccountSeed("1000", "Cash on Hand", AccountTypes.Asset, CurrentAsset,
                AccountKeys.Cash, "Money in the till and petty cash."),
            new ChartAccountSeed("1010", "Cash in Bank", AccountTypes.Asset, CurrentAsset,
                AccountKeys.Bank, "Balances held in bank and e-wallet accounts."),
            new ChartAccountSeed("1100", "Accounts Receivable", AccountTypes.Asset, CurrentAsset,
                AccountKeys.AccountsReceivable, "Money owed by members and customers."),
            new ChartAccountSeed("1200", "Inventory", AccountTypes.Asset, CurrentAsset,
                AccountKeys.Inventory, "Stock on hand, valued at weighted average cost."),
            new ChartAccountSeed("1500", "Gym Equipment", AccountTypes.Asset, FixedAsset,
                AccountKeys.Equipment, "Machines, weights and fixtures."),

            // ---------------------------------------------------------------- liabilities
            new ChartAccountSeed("2000", "Accounts Payable", AccountTypes.Liability, CurrentLiability,
                AccountKeys.AccountsPayable, "Money owed to suppliers and for unpaid expenses."),
            new ChartAccountSeed("2100", "Salaries Payable", AccountTypes.Liability, CurrentLiability,
                AccountKeys.SalaryPayable, "Net pay approved but not yet paid out."),
            new ChartAccountSeed("2110", "SSS Payable", AccountTypes.Liability, CurrentLiability,
                AccountKeys.SssPayable, "Employee and employer SSS contributions awaiting remittance."),
            new ChartAccountSeed("2120", "PhilHealth Payable", AccountTypes.Liability, CurrentLiability,
                AccountKeys.PhilHealthPayable, "PhilHealth contributions awaiting remittance."),
            new ChartAccountSeed("2130", "Pag-IBIG Payable", AccountTypes.Liability, CurrentLiability,
                AccountKeys.PagIbigPayable, "Pag-IBIG contributions awaiting remittance."),
            new ChartAccountSeed("2140", "Withholding Tax Payable", AccountTypes.Liability, CurrentLiability,
                AccountKeys.WithholdingTaxPayable, "Tax withheld from pay, awaiting remittance."),

            // ---------------------------------------------------------------- equity
            new ChartAccountSeed("3000", "Owner's Equity", AccountTypes.Equity, OwnersEquity,
                AccountKeys.OwnerEquity, "The owner's stake in the business."),
            new ChartAccountSeed("3100", "Retained Earnings", AccountTypes.Equity, OwnersEquity,
                AccountKeys.RetainedEarnings, "Accumulated profit not drawn out."),

            // ---------------------------------------------------------------- revenue
            new ChartAccountSeed("4000", "Membership Revenue", AccountTypes.Revenue, OperatingRevenue,
                AccountKeys.MembershipRevenue, "Subscriptions and membership dues."),
            new ChartAccountSeed("4100", "Product Sales", AccountTypes.Revenue, OperatingRevenue,
                AccountKeys.ProductRevenue, "Goods sold over the counter."),
            new ChartAccountSeed("4200", "Other Income", AccountTypes.Revenue, OperatingRevenue,
                AccountKeys.OtherRevenue, "Anything earned outside memberships and goods."),

            // Contra-revenue: debited rather than credited, so its balance is negative and
            // simply summing every revenue account gives net revenue without a special case.
            new ChartAccountSeed("4900", "Sales Discounts", AccountTypes.Revenue, ContraRevenue,
                AccountKeys.SalesDiscounts, "Discounts given at the till, deducted from revenue."),
            new ChartAccountSeed("4910", "Sales Returns", AccountTypes.Revenue, ContraRevenue,
                AccountKeys.SalesReturns, "Goods returned, deducted from revenue."),

            // ---------------------------------------------------------------- cost of sales
            new ChartAccountSeed("5000", "Cost of Goods Sold", AccountTypes.Expense, CostOfSales,
                AccountKeys.CostOfGoodsSold, "What the goods sold cost the gym to acquire."),

            // ---------------------------------------------------------------- payroll
            new ChartAccountSeed("5100", "Salaries and Wages", AccountTypes.Expense, PayrollExpense,
                AccountKeys.SalariesExpense, "Basic pay and regular hours."),
            new ChartAccountSeed("5110", "Overtime", AccountTypes.Expense, PayrollExpense,
                AccountKeys.OvertimeExpense, "Pay for hours beyond the standard shift."),
            new ChartAccountSeed("5120", "Employer Contributions", AccountTypes.Expense, PayrollExpense,
                AccountKeys.EmployerContributions,
                "The gym's own SSS, PhilHealth and Pag-IBIG share - a cost above gross pay."),

            // ---------------------------------------------------------------- operating
            Operating("5200", ExpenseCategories.Rent, "Premises rent."),
            Operating("5210", ExpenseCategories.Electricity, "Power."),
            Operating("5220", ExpenseCategories.Water, "Water and sewerage."),
            Operating("5230", ExpenseCategories.Internet, "Connectivity."),
            Operating("5240", ExpenseCategories.Telephone, "Telephone lines and mobile."),

            Operating("5300", ExpenseCategories.Maintenance, "Routine upkeep of equipment and premises."),
            Operating("5310", ExpenseCategories.Repairs, "Putting something right that has broken."),
            Operating("5320", ExpenseCategories.Cleaning, "Cleaning services and materials."),
            Operating("5330", ExpenseCategories.Security, "Guards, alarms and monitoring."),
            Operating("5340", ExpenseCategories.Supplies, "Consumables used running the office."),
            Operating("5350", ExpenseCategories.Equipment,
                "Small equipment expensed rather than capitalised."),

            Operating("5400", ExpenseCategories.Marketing, "Campaigns, promotions and collateral."),
            Operating("5410", ExpenseCategories.Advertising, "Paid placement."),

            Operating("5500", ExpenseCategories.Transportation, "Travel on the gym's business."),
            Operating("5510", ExpenseCategories.Delivery, "Getting goods to or from the gym."),

            Operating("5600", ExpenseCategories.Licenses, "Business permits and registrations."),
            Operating("5610", ExpenseCategories.Insurance, "Premiums."),
            Operating("5620", ExpenseCategories.Taxes, "Local taxes and statutory fees."),
            Operating("5630", ExpenseCategories.BankCharges, "Bank charges and card processing fees."),
            Operating("5640", ExpenseCategories.Software, "Software and online subscriptions."),
            Operating("5650", ExpenseCategories.Professional, "Accountants, lawyers and consultants."),

            Operating("5700", ExpenseCategories.StaffWelfare, "Staff meals, uniforms and welfare."),
            Operating("5710", ExpenseCategories.Training, "Courses and certification."),

            new ChartAccountSeed("5800", "Inventory Shrinkage", AccountTypes.Expense, OperatingExpense,
                AccountKeys.InventoryShrinkage,
                "Stock written off after a count, breakage or loss."),

            new ChartAccountSeed("5900", "General Expense", AccountTypes.Expense, OperatingExpense,
                AccountKeys.GeneralExpense,
                "Where an expense posts when its category has no account of its own.")
        };

        /// <summary>An operating expense account named after the category that posts to it.</summary>
        private static ChartAccountSeed Operating(string code, string category, string description) =>
            new(code, category, AccountTypes.Expense, OperatingExpense,
                ExpenseAccountKey(category), description);

        public static ChartAccountSeed? FindByKey(string? systemKey) =>
            Standard.FirstOrDefault(a =>
                string.Equals(a.SystemKey, systemKey, StringComparison.OrdinalIgnoreCase));
    }
}
