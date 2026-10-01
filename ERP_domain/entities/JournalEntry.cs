namespace ERP_domain.entities
{
    /// <summary>What caused a journal entry to exist.</summary>
    public static class JournalSources
    {
        /// <summary>Typed in by an operator on the Journal Entries screen.</summary>
        public const string Manual = "Manual";

        /// <summary>Raised automatically by another module completing a transaction.</summary>
        public const string Sale = "Sale";
        public const string SaleReturn = "SaleReturn";
        public const string Payment = "Payment";
        public const string Purchase = "Purchase";
        public const string SupplierPayment = "SupplierPayment";
        public const string StockAdjustment = "StockAdjustment";
        public const string Payroll = "Payroll";
        public const string Expense = "Expense";
        public const string BankTransaction = "BankTransaction";

        /// <summary>The opening balances a tenant starts its books with.</summary>
        public const string Opening = "Opening";

        /// <summary>A reversal of another entry, raised by the system.</summary>
        public const string Reversal = "Reversal";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Manual, Sale, SaleReturn, Payment, Purchase, SupplierPayment,
            StockAdjustment, Payroll, Expense, BankTransaction, Opening, Reversal
        };

        /// <summary>
        /// Whether an entry from this source may be edited by hand.
        ///
        /// Only a manual entry may. An automatic one is the financial record of something that
        /// happened elsewhere - a sale, a pay run - and editing it would make the ledger
        /// disagree with the operational record it describes. Correcting one means reversing
        /// it, which leaves both entries visible.
        /// </summary>
        public static bool IsEditable(string? source) =>
            string.Equals(source, Manual, StringComparison.OrdinalIgnoreCase);
    }

    public static class JournalStatuses
    {
        /// <summary>Written but not yet part of the ledger. Excluded from every balance.</summary>
        public const string Draft = "Draft";

        /// <summary>In the ledger. Counts towards balances and appears on the statements.</summary>
        public const string Posted = "Posted";

        /// <summary>A draft abandoned before it was ever posted.</summary>
        public const string Void = "Void";

        public static readonly IReadOnlyList<string> All = new[] { Draft, Posted, Void };

        /// <summary>
        /// Only a posted entry moves an account balance.
        ///
        /// Note what is deliberately absent: there is no "Reversed" status. An entry that has
        /// been reversed stays <see cref="Posted"/>, because it is still in the ledger - that
        /// is the entire difference between reversing and deleting. The reversing entry is
        /// posted too, and the two net to zero. Marking the original as no longer counting
        /// would leave only the reversal in the balances, which is not "undone" but "undone
        /// twice", and every account it touched would end up wrong by the amount of the
        /// original entry.
        ///
        /// Whether an entry has been reversed is carried by
        /// <see cref="JournalEntry.ReversedByEntryId"/> instead, which is a fact about the
        /// entry rather than a change to its standing.
        /// </summary>
        public static bool AffectsBalances(string? status) =>
            string.Equals(status, Posted, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// One double-entry posting: a date, a reason, and two or more lines that balance.
    ///
    /// Everything financial in FitCore ends up here. A sale raises one, so does a payment, a
    /// stock receipt, a pay run and an expense - which is what makes the income statement and
    /// the balance sheet derivable from a single table rather than assembled by adding up
    /// every operational module separately and hoping none of them was missed.
    ///
    /// An entry is immutable once posted. A mistake is corrected by reversing it, never by
    /// editing it, so the trail shows what was believed at the time as well as what is
    /// believed now.
    /// </summary>
    public class JournalEntry : IAuditable
    {
        public int JournalEntryId { get; set; }

        /// <summary>Human-readable running number, unique within the tenant. JE-000001.</summary>
        public string EntryNo { get; set; } = "";

        public DateTime EntryDate { get; set; } = DateTime.UtcNow;

        /// <summary>A short external reference: an invoice number, a receipt, an OR.</summary>
        public string Reference { get; set; } = "";

        /// <summary>Why this entry exists, in a sentence.</summary>
        public string Memo { get; set; } = "";

        /// <summary>One of <see cref="JournalSources"/>.</summary>
        public string Source { get; set; } = JournalSources.Manual;

        /// <summary>The module that raised it, so the ledger filters the way the sidebar does.</summary>
        public string SourceModule { get; set; } = "";

        /// <summary>The record it describes, for example "Sale".</summary>
        public string SourceEntityName { get; set; } = "";

        /// <summary>
        /// That record's key, as text so a non-integer key still fits. Together with
        /// <see cref="SourceEntityName"/> this is what lets a sale show its own posting and
        /// stops the same sale being posted twice.
        /// </summary>
        public string? SourceEntityId { get; set; }

        /// <summary>One of <see cref="JournalStatuses"/>.</summary>
        public string Status { get; set; } = JournalStatuses.Posted;

        public DateTime? PostedAt { get; set; }

        /// <summary>Set on the original when it has been reversed.</summary>
        public int? ReversedByEntryId { get; set; }

        /// <summary>Set on the reversing entry, naming what it undoes.</summary>
        public int? ReversesEntryId { get; set; }

        /// <summary>
        /// The totals, stored rather than recomputed on read.
        ///
        /// A ledger listing shows thousands of entries and summing their lines for each one is
        /// a query per row. They are written by the service from the lines it has just
        /// validated, never taken from the client.
        /// </summary>
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }

        /// <summary>
        /// The financial period this entry falls in, denormalised from the date.
        ///
        /// Held on the row so closing a period is a single indexed check rather than a date
        /// range comparison on every insert, and so a report by period cannot disagree with
        /// the period the entry was actually allowed into.
        /// </summary>
        public int PeriodYear { get; set; }
        public int PeriodMonth { get; set; }

        /// <summary>The signed-in user who posted it, captured from the token.</summary>
        public int? ProcessedByUserId { get; set; }
        public string ProcessedBy { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public ICollection<JournalEntryLine> Lines { get; set; } = new List<JournalEntryLine>();

        /// <summary>True when the two sides agree, which every posted entry must.</summary>
        public bool IsBalanced => TotalDebit == TotalCredit;
    }

    /// <summary>
    /// One side of one account's involvement in an entry.
    ///
    /// A line is either a debit or a credit, never both: the unused side is zero. Storing them
    /// as two columns rather than one signed amount keeps the ledger readable as accountants
    /// expect it and makes a trial balance a pair of sums rather than a pair of filtered sums.
    /// </summary>
    public class JournalEntryLine
    {
        public int JournalEntryLineId { get; set; }

        public int JournalEntryId { get; set; }

        /// <summary>Position within the entry, so the lines read back in the order written.</summary>
        public int LineNumber { get; set; }

        public int AccountId { get; set; }

        public string Description { get; set; } = "";

        public decimal Debit { get; set; }
        public decimal Credit { get; set; }

        /// <summary>
        /// Optional subsidiary references, so a receivables or payables balance can be aged by
        /// who owes it without joining back through the source record. Deliberately not
        /// foreign keys to anything that can be deleted out from under the ledger.
        /// </summary>
        public int? MemberId { get; set; }
        public int? SupplierId { get; set; }
        public int? EmployeeId { get; set; }

        public JournalEntry Entry { get; set; } = null!;
        public Account Account { get; set; } = null!;

        /// <summary>The signed effect on a debit-normal account. Negative on the credit side.</summary>
        public decimal SignedAmount => Debit - Credit;
    }
}
