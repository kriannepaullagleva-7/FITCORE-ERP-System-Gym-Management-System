namespace ERP_domain.entities
{
    /// <summary>
    /// Money going out that is not payroll: rent, utilities, equipment, supplies and so on.
    /// </summary>
    public class Expense : IAuditable
    {
        public int ExpenseId { get; set; }

        /// <summary>Rent, Utilities, Equipment, Supplies, Maintenance, Marketing or Other.</summary>
        public string Category { get; set; } = "Other";

        public string Description { get; set; } = "";

        public decimal Amount { get; set; }

        public DateTime ExpenseDate { get; set; } = DateTime.UtcNow;

        public string PaymentMethod { get; set; } = "Cash";

        public string ReferenceNo { get; set; } = "";

        /// <summary>Optional: the employee who recorded the expense.</summary>
        public int? RecordedByEmployeeId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Stamped by the DbContext whenever this record is changed.</summary>
        public DateTime? UpdatedAt { get; set; }

        public Employee? RecordedByEmployee { get; set; }
    }
}
