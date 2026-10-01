namespace ERP_infrastructure.services
{
    /// <summary>What the trail read back looks like on screen - the same shape as the row
    /// written, minus nothing, since <see cref="AuditEvent"/> already has no sensitive fields.</summary>
    public sealed class AuditEventView
    {
        public long AuditEventId { get; set; }
        public DateTime OccurredAt { get; set; }
        public string Username { get; set; } = "";
        public string RoleKey { get; set; } = "";
        public string Action { get; set; } = "";
        public string Module { get; set; } = "";
        public string EntityName { get; set; } = "";
        public string? EntityId { get; set; }
        public string? OldValues { get; set; }
        public string? NewValues { get; set; }
        public string? Summary { get; set; }

        /// <summary>
        /// Where the request came from. Useful on the Security screen, where "somebody
        /// tried this account nine times" is a different piece of news from the same
        /// attempts arriving from one desk in the gym.
        /// </summary>
        public string? IpAddress { get; set; }
    }

    /// <summary>What the caller may filter the trail by. Every field is optional.</summary>
    public sealed record AuditEventQuery(
        DateTime? From = null,
        DateTime? To = null,
        string? Module = null,
        string? Action = null,
        string? EntityName = null,
        string? EntityId = null,
        int Take = 200);

    /// <summary>
    /// Reads the tenant audit trail back. Deliberately separate from <see cref="IAuditService"/>,
    /// which only ever writes - a reporting screen has no business holding a handle that can
    /// also record events, and the two have entirely different callers.
    /// </summary>
    public interface IAuditQueryService
    {
        Task<List<AuditEventView>> SearchAsync(AuditEventQuery query);
    }
}
