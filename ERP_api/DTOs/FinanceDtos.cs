using System.ComponentModel.DataAnnotations;
using ERP_domain.entities;

namespace ERP_api.DTOs
{
    // ---------------------------------------------------------------------- chart of accounts

    public class CreateAccountDto
    {
        [Required(ErrorMessage = "An account code is required.")]
        [StringLength(20)]
        public string AccountCode { get; set; } = "";

        [Required(ErrorMessage = "An account name is required.")]
        [StringLength(150)]
        public string AccountName { get; set; } = "";

        /// <summary>Asset, Liability, Equity, Revenue or Expense.</summary>
        [Required(ErrorMessage = "An account type is required.")]
        [StringLength(20)]
        public string AccountType { get; set; } = AccountTypes.Asset;

        [StringLength(60)]
        public string AccountSubType { get; set; } = "";

        [StringLength(300)]
        public string Description { get; set; } = "";

        public int? ParentAccountId { get; set; }
    }

    public class UpdateAccountDto : CreateAccountDto
    {
        public bool IsActive { get; set; } = true;
    }

    // ---------------------------------------------------------------------- journal

    /// <summary>
    /// One line of a journal entry. Exactly one of the two amounts carries a value; the server
    /// refuses a line that tries to debit and credit the same account at once.
    /// </summary>
    public class JournalLineDto
    {
        [Range(1, int.MaxValue, ErrorMessage = "Choose an account for every line.")]
        public int AccountId { get; set; }

        [StringLength(300)]
        public string Description { get; set; } = "";

        [Range(0, 999999999)]
        public decimal Debit { get; set; }

        [Range(0, 999999999)]
        public decimal Credit { get; set; }

        public int? MemberId { get; set; }
        public int? SupplierId { get; set; }
        public int? EmployeeId { get; set; }
    }

    public class CreateJournalEntryDto
    {
        public DateTime EntryDate { get; set; }

        [Required(ErrorMessage = "Say what this entry is for.")]
        [StringLength(300)]
        public string Memo { get; set; } = "";

        [StringLength(60)]
        public string Reference { get; set; } = "";

        /// <summary>
        /// False writes a draft, which is excluded from every balance until it is posted.
        /// Posting is the default because almost every entry records something that has
        /// already happened.
        /// </summary>
        public bool Post { get; set; } = true;

        [MinLength(2, ErrorMessage = "An entry needs at least two lines.")]
        public List<JournalLineDto> Lines { get; set; } = new();
    }

    public class UpdateJournalEntryDto
    {
        public DateTime EntryDate { get; set; }

        [Required]
        [StringLength(300)]
        public string Memo { get; set; } = "";

        [StringLength(60)]
        public string Reference { get; set; } = "";

        [MinLength(2)]
        public List<JournalLineDto> Lines { get; set; } = new();
    }

    public class ReverseJournalEntryDto
    {
        /// <summary>
        /// Required. A reversal without a stated reason leaves the ledger showing two entries
        /// that cancel out and nothing saying why.
        /// </summary>
        [Required(ErrorMessage = "Say why this entry is being reversed.")]
        [StringLength(300)]
        public string Reason { get; set; } = "";
    }

    // ---------------------------------------------------------------------- periods

    public class ClosePeriodDto
    {
        [StringLength(300)]
        public string Notes { get; set; } = "";
    }

    public class ReopenPeriodDto
    {
        [Required(ErrorMessage = "Say why the period is being reopened.")]
        [StringLength(300)]
        public string Reason { get; set; } = "";
    }

    // ---------------------------------------------------------------------- budgets

    public class CreateBudgetDto
    {
        [Required(ErrorMessage = "A budget needs a name.")]
        [StringLength(150)]
        public string Name { get; set; } = "";

        [Range(2000, 2100)]
        public int Year { get; set; } = DateTime.UtcNow.Year;

        [StringLength(300)]
        public string Notes { get; set; } = "";
    }

    public class UpdateBudgetDto : CreateBudgetDto { }

    public class BudgetLineDto
    {
        [Range(1, int.MaxValue)]
        public int AccountId { get; set; }

        [Range(1, 12, ErrorMessage = "The month must be between 1 and 12.")]
        public int Month { get; set; }

        [Range(0, 999999999, ErrorMessage = "A budgeted amount cannot be negative.")]
        public decimal Amount { get; set; }

        [StringLength(200)]
        public string Notes { get; set; } = "";
    }

    /// <summary>Spreads one annual figure evenly across the twelve months.</summary>
    public class SpreadBudgetDto
    {
        [Range(1, int.MaxValue)]
        public int AccountId { get; set; }

        [Range(0, 999999999)]
        public decimal AnnualAmount { get; set; }
    }

    // ---------------------------------------------------------------------- banking

    public class CreateBankAccountDto
    {
        [Required(ErrorMessage = "The account needs a name.")]
        [StringLength(150)]
        public string AccountName { get; set; } = "";

        [StringLength(150)]
        public string BankName { get; set; } = "";

        /// <summary>
        /// For a real account this should be the last four digits. FitCore never needs the
        /// whole number, and asking for one invites it into a database that does not need it.
        /// </summary>
        [StringLength(60)]
        public string AccountNumber { get; set; } = "";

        /// <summary>Cash, Bank or E-Wallet.</summary>
        [StringLength(20)]
        public string Kind { get; set; } = CashAccountKinds.Bank;

        public decimal OpeningBalance { get; set; }

        [StringLength(300)]
        public string Notes { get; set; } = "";
    }

    public class UpdateBankAccountDto : CreateBankAccountDto
    {
        public bool IsActive { get; set; } = true;
    }

    public class RecordBankTransactionDto
    {
        public DateTime TransactionDate { get; set; }

        /// <summary>In or Out. The amount itself is always positive.</summary>
        [Required]
        [StringLength(10)]
        public string Direction { get; set; } = BankTransactionDirections.In;

        [Range(0.01, 999999999, ErrorMessage = "The amount must be greater than zero.")]
        public decimal Amount { get; set; }

        [StringLength(60)]
        public string Reference { get; set; } = "";

        [StringLength(300)]
        public string Description { get; set; } = "";
    }

    public class ReconcileDto
    {
        public List<int> BankTransactionIds { get; set; } = new();
    }

    public class SetReconciledDto
    {
        public bool IsReconciled { get; set; } = true;
    }
}
