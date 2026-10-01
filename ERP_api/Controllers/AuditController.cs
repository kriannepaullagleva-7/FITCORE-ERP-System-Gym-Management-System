using ERP_domain.entities;
using ERP_api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using ERP_api.DTOs;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Mvc;

namespace ERP_api.Controllers
{
    /// <summary>
    /// The tenant's audit trail, read back. Not a module of its own - it sits behind Reports,
    /// the same way it sits behind the Reports tab in the desktop client - and
    /// <see cref="IAuditQueryService"/> additionally refuses anyone below Admin, so a Manager
    /// who happens to hold Reports still cannot watch a colleague's every move.
    /// </summary>
    [ApiController]
    [Route("api/audit-events")]
    [Produces("application/json")]
    [Authorize]
    [RequireModule(ErpModules.SystemAdmin)]
    [RequireSubmodule(ErpModules.Sub.AuditLogs)]
    public class AuditController : ControllerBase
    {
        private readonly IAuditQueryService _auditQuery;

        public AuditController(IAuditQueryService auditQuery)
        {
            _auditQuery = auditQuery;
        }

        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<AuditEventDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<IEnumerable<AuditEventDto>>> Search(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string? module,
            [FromQuery] string? action,
            [FromQuery] string? entityName,
            [FromQuery] string? entityId,
            [FromQuery] int? take)
        {
            var events = await _auditQuery.SearchAsync(new AuditEventQuery(
                from, to, module, action, entityName, entityId, take ?? 200));

            return Ok(events.Select(e => e.ToDto()));
        }
    }
}
