namespace ERP_infrastructure.services
{
    /// <summary>
    /// The ledger: creating, posting and reversing double-entry journal entries, and reading
    /// them back as a general ledger or a trial balance.
    ///
    /// Everything financial goes through here. The operational services do not write journal
    /// lines themselves - they call <see cref="IFinancePostingService"/>, which calls this -
    /// so the rules that make an entry valid are enforced in exactly one place.
    /// </summary>
    public interface IJournalService
    {
        Task<List<JournalEntryView>> GetEntriesAsync(
            DateTime? fromUtc = null,
            DateTime? toUtc = null,
            string? source = null,
            string? status = null,
            int? accountId = null,
            int take = 500);

        Task<JournalEntryView?> GetEntryAsync(int journalEntryId);

        /// <summary>The posting raised for a given source record, if there is one.</summary>
        Task<JournalEntryView?> GetEntryForSourceAsync(string entityName, string entityId);

        /// <summary>
        /// Writes a balanced entry.
        ///
        /// Refuses an entry whose debits and credits differ, one with fewer than two lines, one
        /// touching an inactive account, and one dated into a closed period. Posting is the
        /// default because almost every entry is raised by a completed transaction; a draft is
        /// the exception, for an operator preparing something by hand.
        /// </summary>
        Task<JournalEntryView> CreateEntryAsync(
            DateTime entryDate,
            string memo,
            IEnumerable<JournalLineRequest> lines,
            string source = "Manual",
            string sourceModule = "",
            string sourceEntityName = "",
            string? sourceEntityId = null,
            string reference = "",
            bool post = true);

        /// <summary>Moves a draft into the ledger.</summary>
        Task<JournalEntryView?> PostEntryAsync(int journalEntryId);

        /// <summary>
        /// Undoes a posted entry by writing its mirror image.
        ///
        /// Never deletes and never edits: both entries stay visible, so the ledger records
        /// what was believed at the time as well as the correction. This is the only way to
        /// undo an automatic posting.
        /// </summary>
        Task<JournalEntryView> ReverseEntryAsync(int journalEntryId, string reason);

        /// <summary>Updates a manual draft. Refused for anything posted or automatic.</summary>
        Task<JournalEntryView?> UpdateDraftAsync(
            int journalEntryId,
            DateTime entryDate,
            string memo,
            string reference,
            IEnumerable<JournalLineRequest> lines);

        /// <summary>Discards a manual draft that was never posted.</summary>
        Task<bool> VoidDraftAsync(int journalEntryId);

        /// <summary>Every posting against one account over a period, with a running balance.</summary>
        Task<GeneralLedgerView?> GetLedgerAsync(int accountId, DateTime? fromUtc, DateTime? toUtc);

        /// <summary>Every account's debit and credit totals as at a date.</summary>
        Task<TrialBalanceView> GetTrialBalanceAsync(DateTime? asOfUtc = null);
    }
}
