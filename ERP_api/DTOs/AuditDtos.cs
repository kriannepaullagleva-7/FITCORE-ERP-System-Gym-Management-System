namespace ERP_api.DTOs
{
    public class AuditEventDto
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

        /// <summary>Where the request came from, for the Security screen.</summary>
        public string? IpAddress { get; set; }
    }
}
