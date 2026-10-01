namespace ERP_domain.entities
{
    public static class CashAccountKinds
    {
        /// <summary>The till, or petty cash. No statement to reconcile against.</summary>
        public const string Cash = "Cash";

        /// <summary>A bank account with a statement.</summary>
        public const string Bank = "Bank";

        /// <summary>GCash, Maya and the like: reconciled the same way a bank is.</summary>
        public const string EWallet = "E-Wallet";

        public static readonly IReadOnlyList<string> All = new[] { Cash, Bank, EWallet };

        public static bool IsKnown(string? value) =>
            value is not null && All.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    public static class BankTransactionDirections
    {
        public const string In = "In";
        public const string Out = "Out";

        public static readonly IReadOnlyList<string> All = new[] { In, Out };

        public static bool IsKnown(string? value) =>
            value is not null && All.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Somewhere the gym's money physically sits: the till, a bank account, an e-wallet.
    ///
    /// Each one is backed by a ledger account, so the balance shown here and the balance the
    /// trial balance reports are the same number arrived at two ways. When they disagree,
    /// reconciliation is what finds out why - which is the entire point of keeping both.
    /// </summary>
    public class BankAccount : IAuditable
    {
        public int BankAccountId { get; set; }

        public string AccountName { get; set; } = "";

        /// <summary>Blank for cash on hand.</summary>
        public string BankName { get; set; } = "";

        /// <summary>
        /// Stored as the operator typed it, which for a real account should be the last four
        /// digits. FitCore never needs the full number and asking for one invites it into a
        /// database that does not need to hold it.
        /// </summary>
        public string AccountNumber { get; set; } = "";

        /// <summary>One of <see cref="CashAccountKinds"/>.</summary>
        public string Kind { get; set; } = CashAccountKinds.Bank;

        /// <summary>The ledger account this one is the operational face of.</summary>
        public int LedgerAccountId { get; set; }

        public decimal OpeningBalance { get; set; }

        /// <summary>
        /// Running balance, maintained by the service as transactions are recorded rather than
        /// summed on read - a cash screen is opened constantly and a sum over the whole history
        /// every time is the wrong shape of query.
        /// </summary>
        public decimal CurrentBalance { get; set; }

        public bool IsActive { get; set; } = true;

        public string Notes { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public Account LedgerAccount { get; set; } = null!;
        public ICollection<BankTransaction> Transactions { get; set; } = new List<BankTransaction>();
    }

    /// <summary>
    /// One movement through a cash or bank account.
    ///
    /// These come from two directions: recorded by hand from a statement, or raised
    /// automatically when a payment is taken or a supplier is paid. Reconciliation is the act
    /// of agreeing that a statement line and a ledger posting are the same event.
    /// </summary>
    public class BankTransaction : IAuditable
    {
        public int BankTransactionId { get; set; }

        public int BankAccountId { get; set; }

        public DateTime TransactionDate { get; set; } = DateTime.UtcNow;

        /// <summary>One of <see cref="BankTransactionDirections"/>.</summary>
        public string Direction { get; set; } = BankTransactionDirections.In;

        /// <summary>Always a positive magnitude; <see cref="Direction"/> carries the sign.</summary>
        public decimal Amount { get; set; }

        /// <summary>Balance immediately after this movement, so a statement reads as one.</summary>
        public decimal BalanceAfter { get; set; }

        public string Reference { get; set; } = "";
        public string Description { get; set; } = "";

        /// <summary>True once this line has been agreed against the ledger.</summary>
        public bool IsReconciled { get; set; }

        public DateTime? ReconciledAt { get; set; }
        public int? ReconciledByUserId { get; set; }
        public string ReconciledBy { get; set; } = "";

        /// <summary>The posting this movement corresponds to, when it has one.</summary>
        public int? JournalEntryId { get; set; }

        public int? PerformedByUserId { get; set; }
        public string PerformedBy { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public BankAccount BankAccount { get; set; } = null!;

        /// <summary>The effect on the account balance, signed.</summary>
        public decimal SignedAmount =>
            string.Equals(Direction, BankTransactionDirections.Out, StringComparison.OrdinalIgnoreCase)
                ? -Amount
                : Amount;
    }
}
