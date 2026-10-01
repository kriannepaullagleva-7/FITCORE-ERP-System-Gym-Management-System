namespace ERP_domain.entities
{
    /// <summary>
    /// The operating expenses a gym actually incurs, grouped the way a chart of accounts
    /// groups them.
    ///
    /// Deliberately a broad list rather than "Other, mostly". An expense screen that offers
    /// five categories produces a profit and loss account where most of the money is
    /// unexplained, which is the same as not having one.
    ///
    /// Note what is *not* here: stock bought for resale. Goods acquired to sell are an asset
    /// until they are sold, at which point they become cost of goods sold - they never pass
    /// through an operating expense. Buying stock is a purchase, recorded in Inventory, and
    /// recording it as an expense is what makes a month with a big delivery look like a loss.
    /// </summary>
    public static class ExpenseCategories
    {
        // Premises
        public const string Rent = "Rent";
        public const string Electricity = "Electricity";
        public const string Water = "Water";
        public const string Internet = "Internet";
        public const string Telephone = "Telephone";

        // Running the place
        public const string Maintenance = "Maintenance";
        public const string Repairs = "Repairs";
        public const string Cleaning = "Cleaning";
        public const string Security = "Security";
        public const string Supplies = "Office Supplies";
        public const string Equipment = "Equipment";

        // Growing it
        public const string Marketing = "Marketing";
        public const string Advertising = "Advertising";

        // Getting about
        public const string Transportation = "Transportation";
        public const string Delivery = "Delivery";

        // Obligations
        public const string Licenses = "Licenses & Permits";
        public const string Insurance = "Insurance";
        public const string Taxes = "Taxes & Fees";
        public const string BankCharges = "Bank & Payment Fees";
        public const string Software = "Software Subscriptions";
        public const string Professional = "Professional Fees";

        // People, when paid outside a pay run
        public const string StaffWelfare = "Staff Welfare";
        public const string Training = "Training";

        public const string Other = "Other";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Rent, Electricity, Water, Internet, Telephone,
            Maintenance, Repairs, Cleaning, Security, Supplies, Equipment,
            Marketing, Advertising,
            Transportation, Delivery,
            Licenses, Insurance, Taxes, BankCharges, Software, Professional,
            StaffWelfare, Training,
            Other
        };

        public static bool IsKnown(string? value) =>
            value is not null && All.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

        public static string? Normalise(string? value) =>
            All.FirstOrDefault(c => string.Equals(c, (value ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The broad grouping an income statement shows, so twenty-four categories do not
        /// become twenty-four lines on a one-page report.
        /// </summary>
        public static string GroupOf(string? category) => Normalise(category) switch
        {
            Rent or Electricity or Water or Internet or Telephone => "Premises & Utilities",
            Maintenance or Repairs or Cleaning or Security or Supplies or Equipment => "Operations",
            Marketing or Advertising => "Marketing",
            Transportation or Delivery => "Transport",
            Licenses or Insurance or Taxes or BankCharges or Software or Professional => "Administrative",
            StaffWelfare or Training => "Staff Costs",
            _ => "Other"
        };
    }

    public static class ExpenseStatuses
    {
        /// <summary>Incurred and owed, but not yet paid. Sits in accounts payable.</summary>
        public const string Unpaid = "Unpaid";

        /// <summary>Paid. The money has left a cash or bank account.</summary>
        public const string Paid = "Paid";

        /// <summary>Entered in error and reversed. Kept so the correction is visible.</summary>
        public const string Void = "Void";

        public static readonly IReadOnlyList<string> All = new[] { Unpaid, Paid, Void };

        public static bool IsKnown(string? value) =>
            value is not null && All.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Money going out that is neither payroll nor stock: rent, utilities, repairs, fees.
    ///
    /// An expense posts <c>Dr the expense account / Cr cash</c> when it is paid immediately, or
    /// <c>Dr the expense account / Cr accounts payable</c> when it is incurred and settled
    /// later - which is what makes an unpaid bill show up as something owed rather than
    /// vanishing until the day somebody writes the cheque.
    /// </summary>
    public class Expense : IAuditable, IBranchScoped
    {
        public int ExpenseId { get; set; }

        /// <summary>The branch that incurred this expense. Null on a single-site tenant.</summary>
        public int? BranchId { get; set; }

        /// <summary>One of <see cref="ExpenseCategories"/>.</summary>
        public string Category { get; set; } = ExpenseCategories.Other;

        public string Description { get; set; } = "";

        public decimal Amount { get; set; }

        public DateTime ExpenseDate { get; set; } = DateTime.UtcNow;

        public string PaymentMethod { get; set; } = "Cash";

        public string ReferenceNo { get; set; } = "";

        /// <summary>
        /// Who it was paid to, as free text. A supplier the gym buys stock from is a
        /// <see cref="SupplierId"/> instead; this is for the landlord and the electricity
        /// company, which are not in the supplier list and should not be.
        /// </summary>
        public string PaidTo { get; set; } = "";

        /// <summary>Set when the expense was incurred with a supplier already on record.</summary>
        public int? SupplierId { get; set; }

        /// <summary>
        /// The ledger account this posts to. Resolved from the category when the expense is
        /// created, and overridable afterwards for a tenant that has extended its chart.
        /// </summary>
        public int? AccountId { get; set; }

        /// <summary>Which cash or bank account it was paid from, when one was named.</summary>
        public int? BankAccountId { get; set; }

        /// <summary>One of <see cref="ExpenseStatuses"/>.</summary>
        public string Status { get; set; } = ExpenseStatuses.Paid;

        /// <summary>Optional: the employee who recorded the expense.</summary>
        public int? RecordedByEmployeeId { get; set; }

        /// <summary>The signed-in user who recorded it, captured from the token.</summary>
        public int? RecordedByUserId { get; set; }
        public string RecordedBy { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Stamped by the DbContext whenever this record is changed.</summary>
        public DateTime? UpdatedAt { get; set; }

        public Employee? RecordedByEmployee { get; set; }
        public Supplier? Supplier { get; set; }
        public Account? Account { get; set; }
        public BankAccount? BankAccount { get; set; }
    }
}
