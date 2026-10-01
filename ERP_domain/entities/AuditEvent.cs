namespace ERP_domain.entities
{
    /// <summary>
    /// One recorded action: who did what, to which record, when.
    ///
    /// The same type is mapped into both databases, because the two halves of the trail belong
    /// in different places. Business actions - a sale, a payroll run, a stock adjustment - live
    /// in the tenant database alongside the rows they describe, so a tenant's history travels
    /// with its data and one busy gym's till does not write into a table shared by every
    /// customer. Sign-in, permission and company changes live in the master database, because
    /// they happen before a tenant is known: a failed sign-in has no company to attribute to
    /// and therefore no tenant database to write to.
    ///
    /// There are deliberately no foreign keys. An audit row has to outlive the record it
    /// describes, which is the whole point of keeping one.
    /// </summary>
    public class AuditEvent
    {
        public long AuditEventId { get; set; }

        /// <summary>UTC. Defaulted in the database so a row cannot be written without one.</summary>
        public DateTime OccurredAt { get; set; }

        /// <summary>Null only for an event with no company yet, such as a failed sign-in.</summary>
        public int? CompanyId { get; set; }

        /// <summary>Not a foreign key: the account may later be removed.</summary>
        public int? AppUserId { get; set; }

        public string Username { get; set; } = "";

        public string RoleKey { get; set; } = "";

        /// <summary>Create, Update, Delete, Login, LoginFailed, PayrollPaid, StockAdjusted...</summary>
        public string Action { get; set; } = "";

        /// <summary>A key from <see cref="ErpModules"/>, so the trail filters the same way the UI does.</summary>
        public string Module { get; set; } = "";

        /// <summary>The entity type acted on, for example "Member".</summary>
        public string EntityName { get; set; } = "";

        /// <summary>Held as text so a composite or non-integer key still fits.</summary>
        public string? EntityId { get; set; }

        /// <summary>
        /// JSON. For an update this holds only the properties that actually changed, so an
        /// edited phone number costs a few dozen bytes rather than a copy of the row.
        /// Credentials are excluded before serialisation.
        /// </summary>
        public string? OldValues { get; set; }

        public string? NewValues { get; set; }

        public string? IpAddress { get; set; }

        public string? DeviceId { get; set; }

        /// <summary>A human sentence for the activity list, when the raw values would not read well.</summary>
        public string? Summary { get; set; }
    }

    /// <summary>The action names used across the application, so they stay spelled the same.</summary>
    public static class AuditActions
    {
        public const string Create = "Create";
        public const string Update = "Update";
        public const string Delete = "Delete";
        public const string Archive = "Archive";

        public const string Login = "Login";
        public const string LoginFailed = "LoginFailed";
        public const string Logout = "Logout";
        public const string PasswordChanged = "PasswordChanged";

        public const string PermissionsChanged = "PermissionsChanged";
        public const string RoleChanged = "RoleChanged";
        public const string StatusChanged = "StatusChanged";
        public const string PasswordReset = "PasswordReset";

        public const string SaleCompleted = "SaleCompleted";
        public const string SaleCancelled = "SaleCancelled";
        public const string PaymentRecorded = "PaymentRecorded";
        public const string StockAdjusted = "StockAdjusted";
        public const string StockReceived = "StockReceived";
        public const string StockIssued = "StockIssued";
        public const string PayrollPaid = "PayrollPaid";
        public const string PayrollGenerated = "PayrollGenerated";
        public const string PayrollStatusChanged = "PayrollStatusChanged";
        public const string SubscriptionRenewed = "SubscriptionRenewed";
        public const string SubscriptionCancelled = "SubscriptionCancelled";
    }
}
