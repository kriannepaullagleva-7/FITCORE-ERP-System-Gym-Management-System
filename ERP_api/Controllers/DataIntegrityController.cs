using ERP_domain.entities;
using ERP_api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Mvc;

namespace ERP_api.Controllers
{
    /// <summary>
    /// Whether this tenant's records still agree with each other.
    ///
    /// Read-only by design, and there is no repair endpoint beside it. The sweep reports what it
    /// found; the two problems it can find that have a supported recovery - payments and sales
    /// that never reached the ledger - are fixed by the Finance catch-up sweep, which already
    /// exists, is idempotent, and is guarded on Journal Entries rather than on this.
    ///
    /// Guarded at Admin on every tier. A Micro gym has no ledger and most of the finance checks
    /// simply do not run for them, but the stock and sale arithmetic matters to every tenant,
    /// and an owner should be able to ask whether their own books hang together.
    /// </summary>
    [ApiController]
    [Route("api/data-integrity")]
    [Produces("application/json")]
    [Authorize]
    [RequireModule(ErpModules.SystemAdmin)]
    [RequireSubmodule(ErpModules.Sub.DataIntegrity)]
    public class DataIntegrityController : ControllerBase
    {
        private readonly IDataIntegrityService _integrity;

        public DataIntegrityController(IDataIntegrityService integrity)
        {
            _integrity = integrity;
        }

        [HttpGet]
        [ProducesResponseType(typeof(IntegrityReport), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<IntegrityReport>> Run()
        {
            return Ok(await _integrity.RunAsync());
        }
    }
}
