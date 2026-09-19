namespace ERP_infrastructure.services
{
    /// <summary>
    /// Who the audit trail should attribute an action to.
    /// </summary>
    public sealed record AuditActor(
        int? AppUserId,
        string Username,
        string RoleKey,
        int? CompanyId,
        string? IpAddress,
        string? DeviceId)
    {
        /// <summary>Work done by the server itself: the start-up bootstrapper, a migration, a test.</summary>
        public static readonly AuditActor System =
            new(null, "system", "system", null, null, null);
    }

    /// <summary>
    /// Supplies the signed-in user to code that has no access to the request.
    ///
    /// This project deliberately has no ASP.NET Core reference - it is the data and business
    /// layer, and is consumed by a desktop application as well as by the API. The HTTP-aware
    /// implementation therefore lives in ERP_api; everything here depends on the interface.
    /// </summary>
    public interface ICurrentUserAccessor
    {
        AuditActor Current { get; }
    }

    /// <summary>
    /// Used where there is no signed-in user: the bootstrapper, the design-time factories, the
    /// WinForms application and the tests. Attributes the work to the system rather than
    /// guessing at a person.
    /// </summary>
    public sealed class NullCurrentUserAccessor : ICurrentUserAccessor
    {
        public AuditActor Current => AuditActor.System;
    }
}
