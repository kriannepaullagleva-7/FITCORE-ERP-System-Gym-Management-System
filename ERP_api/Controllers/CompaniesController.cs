using ERP_api.Infrastructure;
using ERP_domain.entities;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP_api.Controllers
{
    /// <summary>
    /// System administration for the SaaS platform itself: which companies exist, where each
    /// company database lives, and which devices belong to a company. This is the only
    /// controller that touches the master database.
    ///
    /// Every action addresses tenants by id, so it is guarded three times over: the caller must
    /// hold the System Administration module, must be an owner or a super admin, and
    /// cross-tenant administration must be switched on for the deployment. A manager of one
    /// company therefore cannot read the registry of another, and nobody can reach it at all on
    /// a deployment where the flag is off.
    ///
    /// Tenant Management is a platform subfeature of System Administration, reserved for the
    /// Super Admin. An owner administers their own gym; only the platform account administers
    /// the tenants themselves - so an Admin on any tier answers 403 here, and registering a
    /// tenant on a deployment with no Super Admin stays a configuration activity through the
    /// Bootstrap section rather than something reachable over HTTP.
    /// </summary>
    [ApiController]
    [Route("api/companies")]
    [Produces("application/json")]
    [Authorize(Roles = ErpRoles.Admin + "," + ErpRoles.SuperAdmin)]
    [RequireModule(ErpModules.SystemAdmin)]
    [RequireSubmodule(ErpModules.Sub.PlatformTenants)]
    [CrossTenantAdmin]
    public class CompaniesController : ControllerBase
    {
        private readonly ICompanyDirectoryService _directory;
        private readonly ITenantProvisioningService _provisioning;

        public CompaniesController(
            ICompanyDirectoryService directory,
            ITenantProvisioningService provisioning)
        {
            _directory = directory;
            _provisioning = provisioning;
        }

        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<Company>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<Company>>> GetAll()
        {
            return Ok(await _directory.GetCompaniesAsync());
        }

        [HttpGet("{companyId:int}")]
        [ProducesResponseType(typeof(Company), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<Company>> GetById(int companyId)
        {
            var company = await _directory.GetCompanyByIdAsync(companyId);
            return company is null ? NotFound() : Ok(company);
        }

        [HttpPost]
        [ProducesResponseType(typeof(Company), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<Company>> Create([FromBody] Company company)
        {
            var created = await _directory.CreateCompanyAsync(company);

            return CreatedAtAction(
                nameof(GetById), new { companyId = created.CompanyId }, created);
        }

        [HttpGet("databases")]
        [ProducesResponseType(typeof(IEnumerable<CompanyDatabase>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<CompanyDatabase>>> GetDatabases(
            [FromQuery] int? companyId)
        {
            return Ok(await _directory.GetCompanyDatabasesAsync(companyId));
        }

        [HttpPost("databases")]
        [ProducesResponseType(typeof(CompanyDatabase), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<CompanyDatabase>> CreateDatabase(
            [FromBody] CompanyDatabase companyDatabase)
        {
            var created = await _directory.CreateCompanyDatabaseAsync(companyDatabase);

            return CreatedAtAction(
                nameof(GetDatabases), new { companyId = created.CompanyId }, created);
        }

        [HttpGet("devices")]
        [ProducesResponseType(typeof(IEnumerable<Device>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<Device>>> GetDevices([FromQuery] int? companyId)
        {
            return Ok(await _directory.GetDevicesAsync(companyId));
        }

        [HttpPost("devices")]
        [ProducesResponseType(typeof(Device), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<Device>> CreateDevice([FromBody] Device device)
        {
            var created = await _directory.CreateDeviceAsync(device);

            return CreatedAtAction(
                nameof(GetDevices), new { companyId = created.CompanyId }, created);
        }

        // -----------------------------------------------------------------------------
        // Tenant provisioning. Registering a company only records where its database lives;
        // the database itself starts empty. These bring its schema up to date by applying
        // the existing tenant migrations. Migrations are idempotent, so both are safe to
        // call more than once.
        // -----------------------------------------------------------------------------

        [HttpGet("{companyId:int}/provisioning")]
        [ProducesResponseType(typeof(TenantProvisioningStatus), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<TenantProvisioningStatus>> GetProvisioningStatus(
            int companyId, CancellationToken cancellationToken)
        {
            if (await _directory.GetCompanyByIdAsync(companyId) is null)
            {
                return NotFound();
            }

            return Ok(await _provisioning.GetStatusAsync(companyId, cancellationToken));
        }

        [HttpPost("{companyId:int}/provision")]
        [ProducesResponseType(typeof(TenantProvisioningResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<ActionResult<TenantProvisioningResult>> Provision(
            int companyId, CancellationToken cancellationToken)
        {
            if (await _directory.GetCompanyByIdAsync(companyId) is null)
            {
                return NotFound();
            }

            return Ok(await _provisioning.ProvisionAsync(companyId, cancellationToken));
        }
    }
}
