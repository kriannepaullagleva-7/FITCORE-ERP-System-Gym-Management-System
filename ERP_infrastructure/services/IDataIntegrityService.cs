namespace ERP_infrastructure.services
{
    /// <summary>How much a finding matters.</summary>
    public static class IntegritySeverities
    {
        /// <summary>The books or the stock ledger disagree with themselves. Someone must look.</summary>
        public const string Critical = "Critical";

        /// <summary>Real, but recoverable by a documented action such as the catch-up sweep.</summary>
        public const string Warning = "Warning";

        /// <summary>Worth knowing; nothing is wrong.</summary>
        public const string Info = "Info";
    }

    /// <summary>One thing that was checked, and what was found.</summary>
    public class IntegrityCheckResult
    {
        /// <summary>Stable key, so a caller can act on a specific check without matching prose.</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>The module the check belongs to, for grouping.</summary>
        public string Area { get; set; } = string.Empty;

        /// <summary>What was checked, stated as the property that should hold.</summary>
        public string Check { get; set; } = string.Empty;

        public int IssueCount { get; set; }

        public bool Passed => IssueCount == 0;

        public string Severity { get; set; } = IntegritySeverities.Warning;

        /// <summary>What it means, and what to do. Empty when nothing was found.</summary>
        public string Detail { get; set; } = string.Empty;

        /// <summary>
        /// A few offending record ids, so the finding can be chased down. Deliberately capped:
        /// this is a diagnostic, not an export, and a check that is wrong about ten thousand
        /// rows should say so rather than list them.
        /// </summary>
        public List<string> Examples { get; set; } = new();
    }

    /// <summary>The whole sweep.</summary>
    public class IntegrityReport
    {
        public DateTime CheckedAtUtc { get; set; } = DateTime.UtcNow;

        public List<IntegrityCheckResult> Checks { get; set; } = new();

        public int ChecksRun => Checks.Count;
        public int ChecksPassed => Checks.Count(c => c.Passed);
        public int IssuesFound => Checks.Sum(c => c.IssueCount);

        public int CriticalCount =>
            Checks.Count(c => !c.Passed && c.Severity == IntegritySeverities.Critical);

        public bool IsClean => IssuesFound == 0;
    }

    /// <summary>
    /// Reads the tenant database and reports whether its records still agree with each other.
    ///
    /// Read-only, always. There is deliberately no repair action: the useful half of a
    /// consistency check is knowing, and an automatic fix for a problem nobody has understood
    /// yet is how one bad row becomes a thousand. Where a documented recovery already exists -
    /// the Finance catch-up sweep for missing postings - the finding names it.
    ///
    /// Two kinds of thing are checked, and the second is the point:
    ///
    /// - **Orphans**, which the foreign keys should already make impossible. These are expected
    ///   to be zero, and are run anyway because "should be impossible" is a claim worth testing
    ///   against a database three tenants have been writing to.
    ///
    /// - **Cross-table arithmetic**, which no foreign key can express: that a journal entry
    ///   balances, that stock on hand equals what the movement ledger says, that a sale's total
    ///   is the sum of its lines, that a payroll run's net is its gross less its deductions.
    ///   These are where real drift shows up, because nothing in the schema prevents them.
    /// </summary>
    public interface IDataIntegrityService
    {
        Task<IntegrityReport> RunAsync();
    }
}
