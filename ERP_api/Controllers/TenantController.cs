using Microsoft.AspNetCore.Authorization;
using ERP_api.Infrastructure;
using ERP_infrastructure.services;
using ERP_infrastructure.tenant;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP_api.Controllers
{
    /// <summary>
    /// Tenant diagnostics.
    ///
    /// The "current" endpoint reports which tenant the server resolved for the caller. It
    /// deliberately returns the company id and how it was decided, never the connection
    /// string or any credential.
    ///
    /// The "probe" endpoint takes a company id in the URL and so is cross-tenant by nature.
    /// It replaces the old test-tenant route and is disabled unless
    /// Tenancy:EnableCrossTenantAdminApi is turned on.
    /// </summary>
    [ApiController]
    [Route("api/tenant")]
    [Produces("application/json")]
    [Authorize]
    public class TenantController : ControllerBase
    {
        private readonly ITenantContext _tenantContext;
        private readonly ITenantDbContextFactory _tenantDbContextFactory;

        public TenantController(
            ITenantContext tenantContext,
            ITenantDbContextFactory tenantDbContextFactory)
        {
            _tenantContext = tenantContext;
            _tenantDbContextFactory = tenantDbContextFactory;
        }

        [HttpGet("current")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult GetCurrent()
        {
            return Ok(new
            {
                resolved = _tenantContext.IsResolved,
                companyId = _tenantContext.IsResolved ? _tenantContext.CompanyId : (int?)null,
                source = _tenantContext.Source.ToString(),
                usingFallbackConnection = _tenantContext.UsedFallback
            });
        }

        [HttpGet("probe/{companyId:int}")]
        [CrossTenantAdmin]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> Probe(int companyId)
        {
            await using var tenantDb = await _tenantDbContextFactory.CreateAsync(companyId);

            return Ok(new
            {
                companyId,
                canConnect = await tenantDb.Database.CanConnectAsync(),
                productCount = await tenantDb.Products.CountAsync(),
                memberCount = await tenantDb.Members.CountAsync()
            });
        }
    }
}
