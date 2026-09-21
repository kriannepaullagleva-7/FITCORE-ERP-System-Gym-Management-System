using ERP_infrastructure.services;

namespace ERP_api.Infrastructure
{
    /// <summary>
    /// Reads the signed-in user off the current request for the audit trail.
    ///
    /// Everything here comes from claims the server itself signed at sign-in, so a client
    /// cannot forge an attribution. The IP address and device are taken from the connection and
    /// the request headers, which are advisory rather than trusted - they are recorded to help
    /// an administrator recognise a session, not to make an access decision.
    /// </summary>
    public sealed class HttpCurrentUserAccessor : ICurrentUserAccessor
    {
        private readonly IHttpContextAccessor _accessor;

        public HttpCurrentUserAccessor(IHttpContextAccessor accessor)
        {
            _accessor = accessor;
        }

        public AuditActor Current
        {
            get
            {
                var http = _accessor.HttpContext;

                // Background work and start-up run outside a request.
                if (http?.User?.Identity?.IsAuthenticated != true)
                {
                    return AuditActor.System;
                }

                var user = http.User;
                var companyId = user.GetCompanyId();

                return new AuditActor(
                    AppUserId: user.GetAppUserId() is var id && id > 0 ? id : null,
                    Username: user.GetUsername(),
                    RoleKey: user.GetRoleKey(),
                    CompanyId: companyId > 0 ? companyId : null,
                    IpAddress: http.Connection.RemoteIpAddress?.ToString(),
                    DeviceId: http.Request.Headers.TryGetValue("X-Device-Id", out var device)
                        ? device.ToString()
                        : null,

                    // Both are already in the signed token. The name is what a receipt
                    // prints; the employee id is how a till transaction is attributed to a
                    // roster entry without the client naming a cashier of its choosing.
                    FullName: user.GetFullName(),
                    EmployeeId: user.GetEmployeeId());
            }
        }
    }
}
