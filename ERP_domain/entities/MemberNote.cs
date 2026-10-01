namespace ERP_domain.entities
{
    public static class MemberNoteCategories
    {
        public const string General = "General";
        public const string Health = "Health";
        public const string Billing = "Billing";
        public const string Incident = "Incident";
        public const string Retention = "Retention";

        public static readonly IReadOnlyList<string> All =
            new[] { General, Health, Billing, Incident, Retention };

        public static bool IsKnown(string? value) =>
            value is not null && All.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Something worth remembering about a member.
    ///
    /// Kept as its own rows rather than a free-text column on the member so each note carries
    /// its own date and author. A gym's useful knowledge about a member - an injury, a dispute,
    /// why they nearly left last year - is a sequence of observations, and flattening it into
    /// one field loses who said what and when.
    ///
    /// Notes are append-only from the operator's point of view: the service allows an edit
    /// only by the author, and the change-tracker sweep records it either way.
    /// </summary>
    public class MemberNote : IAuditable
    {
        public int MemberNoteId { get; set; }

        public int MemberId { get; set; }

        /// <summary>One of <see cref="MemberNoteCategories"/>.</summary>
        public string Category { get; set; } = MemberNoteCategories.General;

        public string Note { get; set; } = "";

        /// <summary>
        /// Pinned notes are shown first and highlighted. This is how a gym flags the thing the
        /// front desk must see before speaking to this member.
        /// </summary>
        public bool IsPinned { get; set; }

        public int? AuthorUserId { get; set; }
        public string Author { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public Member Member { get; set; } = null!;
    }
}
