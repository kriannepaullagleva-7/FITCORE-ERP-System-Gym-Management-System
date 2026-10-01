using ERP_api.DTOs;
using ERP_api.Infrastructure;
using ERP_domain.entities;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP_api.Controllers
{
    /// <summary>
    /// The Super Admin's platform administration: tenants, subscriptions, platform accounts,
    /// installation settings, health and the platform audit trail.
    ///
    /// Everything here reads and writes the master database. It never reads a tenant's business
    /// data - when the Super Admin wants to look inside Tenant C, the request goes through the
    /// ordinary tenant-resolution path and the ordinary business endpoints exactly as a tenant
    /// user's would. Copying a tenant's operational records into the master database so the
    /// platform can see them is precisely what this separation exists to prevent.
    ///
    /// Guarded three times over: the caller must hold System Administration, must be a Super
    /// Admin - every route names a platform subfeature, and those are level 0 only - and
    /// cross-tenant administration must be enabled for the deployment.
    /// </summary>
    [ApiController]
    [Route("api/platform")]
    [Produces("application/json")]
    [Authorize(Roles = ErpRoles.SuperAdmin)]
    [RequireModule(ErpModules.SystemAdmin)]
    [RequireSubmodule(ErpModules.Sub.PlatformTenants)]
    [CrossTenantAdmin]
    public class PlatformController : ControllerBase
    {
        private readonly IPlatformAdminService _platform;
        private readonly IPlatformAnalyticsService _analytics;
        private readonly ITenantProvisioningService _provisioning;

        public PlatformController(
            IPlatformAdminService platform,
            IPlatformAnalyticsService analytics,
            ITenantProvisioningService provisioning)
        {
            _platform = platform;
            _analytics = analytics;
            _provisioning = provisioning;
        }

        // ================================================================== tenants

        [HttpGet("tenants")]
        [ProducesResponseType(typeof(IEnumerable<TenantView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<TenantView>>> GetTenants()
        {
            return Ok(await _platform.GetTenantsAsync());
        }

        [HttpGet("tenants/{companyId:int}")]
        [ProducesResponseType(typeof(TenantView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<TenantView>> GetTenant(int companyId)
        {
            var tenant = await _platform.GetTenantAsync(companyId);
            return tenant is null ? NotFound() : Ok(tenant);
        }

        [HttpPost("tenants")]
        [ProducesResponseType(typeof(TenantView), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<TenantView>> CreateTenant([FromBody] CreateTenantDto dto)
        {
            var tenant = await _platform.CreateTenantAsync(
                dto.CompanyCode, dto.CompanyName, dto.Tier,
                dto.ServerName, dto.DatabaseName, dto.CredentialKey);

            return CreatedAtAction(nameof(GetTenant), new { companyId = tenant.CompanyId }, tenant);
        }

        [HttpPut("tenants/{companyId:int}")]
        [ProducesResponseType(typeof(TenantView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<TenantView>> UpdateTenant(
            int companyId, [FromBody] UpdateTenantDto dto)
        {
            var tenant = await _platform.UpdateTenantAsync(
                companyId, dto.CompanyName, dto.Tier, dto.IsActive);

            return tenant is null ? NotFound() : Ok(tenant);
        }

        /// <summary>
        /// Changes what a tenant is licensed for. This is the single switch that decides which
        /// of the nine modules their users can reach, so it takes a stated reason and gets its
        /// own entry in the platform audit trail.
        /// </summary>
        [HttpPost("tenants/{companyId:int}/tier")]
        [RequireSubmodule(ErpModules.Sub.PlatformSubscriptions)]
        [ProducesResponseType(typeof(TenantView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<TenantView>> SetTier(
            int companyId, [FromBody] SetTierDto dto)
        {
            var tenant = await _platform.SetTierAsync(companyId, dto.Tier, dto.Reason);
            return tenant is null ? NotFound() : Ok(tenant);
        }

        [HttpPost("tenants/{companyId:int}/status")]
        [ProducesResponseType(typeof(TenantView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<TenantView>> SetTenantStatus(
            int companyId, [FromBody] SetTenantStatusDto dto)
        {
            var tenant = await _platform.SetActiveAsync(companyId, dto.IsActive, dto.Reason);
            return tenant is null ? NotFound() : Ok(tenant);
        }

        /// <summary>
        /// Brings a tenant database up to the current schema. Registering a company says where
        /// its data lives; this is what makes the database usable.
        /// </summary>
        [HttpPost("tenants/{companyId:int}/provision")]
        [ProducesResponseType(typeof(TenantProvisioningResult), StatusCodes.Status200OK)]
        public async Task<ActionResult<TenantProvisioningResult>> Provision(
            int companyId, CancellationToken cancellationToken)
        {
            return Ok(await _provisioning.ProvisionAsync(companyId, cancellationToken));
        }

        // ================================================================== subscriptions

        [HttpGet("plans")]
        [RequireSubmodule(ErpModules.Sub.PlatformPlans)]
        [ProducesResponseType(typeof(IEnumerable<SubscriptionPlanView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<SubscriptionPlanView>>> GetPlans()
        {
            return Ok(await _platform.GetPlansAsync());
        }

        [HttpPost("plans")]
        [RequireSubmodule(ErpModules.Sub.PlatformPlans)]
        [ProducesResponseType(typeof(SubscriptionPlanView), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<SubscriptionPlanView>> CreatePlan(
            [FromBody] CreateSubscriptionPlanDto dto)
        {
            var plan = await _platform.CreatePlanAsync(
                dto.PlanCode, dto.PlanName, dto.Tier,
                dto.MonthlyPrice, dto.AnnualPrice, dto.MaxUsers, dto.Description, dto.Capability);

            return CreatedAtAction(nameof(GetPlans), null, plan);
        }

        [HttpPut("plans/{planId:int}")]
        [RequireSubmodule(ErpModules.Sub.PlatformPlans)]
        [ProducesResponseType(typeof(SubscriptionPlanView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<SubscriptionPlanView>> UpdatePlan(
            int planId, [FromBody] UpdateSubscriptionPlanDto dto)
        {
            var plan = await _platform.UpdatePlanAsync(
                planId, dto.PlanName, dto.MonthlyPrice, dto.AnnualPrice,
                dto.MaxUsers, dto.Description, dto.Capability, dto.IsActive);

            return plan is null ? NotFound() : Ok(plan);
        }

        [HttpDelete("plans/{planId:int}")]
        [RequireSubmodule(ErpModules.Sub.PlatformPlans)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeletePlan(int planId)
        {
            var deleted = await _platform.DeletePlanAsync(planId);
            return deleted ? NoContent() : NotFound();
        }

        [HttpGet("subscriptions")]
        [RequireSubmodule(ErpModules.Sub.PlatformSubscriptions)]
        [ProducesResponseType(typeof(IEnumerable<CompanySubscriptionView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<CompanySubscriptionView>>> GetSubscriptions(
            [FromQuery] int? companyId, [FromQuery] string? status)
        {
            return Ok(await _platform.GetSubscriptionsAsync(companyId, status));
        }

        [HttpPost("subscriptions")]
        [RequireSubmodule(ErpModules.Sub.PlatformSubscriptions)]
        [ProducesResponseType(typeof(CompanySubscriptionView), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<CompanySubscriptionView>> Subscribe(
            [FromBody] SubscribeDto dto)
        {
            var subscription = await _platform.SubscribeAsync(
                dto.CompanyId, dto.SubscriptionPlanId, dto.BillingCycle, dto.StartDate, dto.AutoRenew);

            return CreatedAtAction(nameof(GetSubscriptions), null, subscription);
        }

        [HttpPost("subscriptions/{id:int}/renew")]
        [RequireSubmodule(ErpModules.Sub.PlatformSubscriptions)]
        [ProducesResponseType(typeof(CompanySubscriptionView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<CompanySubscriptionView>> RenewSubscription(int id)
        {
            var subscription = await _platform.RenewSubscriptionAsync(id);
            return subscription is null ? NotFound() : Ok(subscription);
        }

        [HttpPost("subscriptions/{id:int}/cancel")]
        [RequireSubmodule(ErpModules.Sub.PlatformSubscriptions)]
        [ProducesResponseType(typeof(CompanySubscriptionView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<CompanySubscriptionView>> CancelSubscription(
            int id, [FromBody] ReasonDto dto)
        {
            var subscription = await _platform.CancelSubscriptionAsync(id, dto.Reason);
            return subscription is null ? NotFound() : Ok(subscription);
        }

        [HttpPost("subscriptions/expire-lapsed")]
        [RequireSubmodule(ErpModules.Sub.PlatformSubscriptions)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<object>> ExpireLapsed()
        {
            return Ok(new { Expired = await _platform.ExpireLapsedSubscriptionsAsync() });
        }

        // ================================================================== accounts

        [HttpGet("users")]
        [RequireSubmodule(ErpModules.Sub.PlatformUsers)]
        [ProducesResponseType(typeof(IEnumerable<PlatformUserView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<PlatformUserView>>> GetUsers(
            [FromQuery] int? companyId, [FromQuery] string? search)
        {
            return Ok(await _platform.GetPlatformUsersAsync(companyId, search));
        }

        [HttpPost("users/{appUserId:int}/status")]
        [RequireSubmodule(ErpModules.Sub.PlatformUsers)]
        [ProducesResponseType(typeof(PlatformUserView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PlatformUserView>> SetUserStatus(
            int appUserId, [FromBody] SetUserStatusDto dto)
        {
            var user = await _platform.SetUserActiveAsync(appUserId, dto.IsActive);
            return user is null ? NotFound() : Ok(user);
        }

        /// <summary>
        /// Resets any account's password across the whole platform, and forces a change on
        /// next sign-in. This is the recovery path when a gym's only owner is locked out.
        /// </summary>
        [HttpPost("users/{appUserId:int}/reset-password")]
        [RequireSubmodule(ErpModules.Sub.PlatformUsers)]
        [ProducesResponseType(typeof(PlatformUserView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PlatformUserView>> ResetPassword(
            int appUserId, [FromBody] ResetPasswordRequestDto dto)
        {
            var user = await _platform.ResetPasswordAsync(appUserId, dto.NewPassword);
            return user is null ? NotFound() : Ok(user);
        }

        // ================================================================== settings and health

        [HttpGet("settings")]
        [RequireSubmodule(ErpModules.Sub.PlatformSettings)]
        [ProducesResponseType(typeof(IEnumerable<TenantSettingView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<TenantSettingView>>> GetSettings()
        {
            return Ok(await _platform.GetPlatformSettingsAsync());
        }

        [HttpPut("settings")]
        [RequireSubmodule(ErpModules.Sub.PlatformSettings)]
        [ProducesResponseType(typeof(IEnumerable<TenantSettingView>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<IEnumerable<TenantSettingView>>> UpdateSettings(
            [FromBody] UpdateSettingsDto dto)
        {
            return Ok(await _platform.SetPlatformSettingsAsync(dto.Values));
        }

        /// <summary>
        /// Probes every tenant database in turn. Slow by nature, so it is a screen the Super
        /// Admin asks for rather than something the dashboard runs on every load.
        /// </summary>
        [HttpGet("health")]
        [RequireSubmodule(ErpModules.Sub.PlatformMonitoring)]
        [ProducesResponseType(typeof(IEnumerable<TenantHealthView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<TenantHealthView>>> GetHealth(
            CancellationToken cancellationToken)
        {
            return Ok(await _platform.GetTenantHealthAsync(cancellationToken));
        }

        [HttpGet("audit")]
        [RequireSubmodule(ErpModules.Sub.PlatformAudit)]
        [ProducesResponseType(typeof(IEnumerable<AuditEvent>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<AuditEvent>>> GetAudit(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string? action,
            [FromQuery] int? companyId,
            [FromQuery] int take = 300)
        {
            return Ok(await _platform.GetPlatformAuditAsync(from, to, action, companyId, take));
        }

        // ================================================================== analytics

        [HttpGet("dashboard")]
        [RequireSubmodule(ErpModules.Sub.PlatformDashboard)]
        [ProducesResponseType(typeof(AnalyticsView), StatusCodes.Status200OK)]
        public async Task<ActionResult<AnalyticsView>> GetDashboard(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            return Ok(await _analytics.GetPlatformDashboardAsync(from, to));
        }

        [HttpGet("analytics")]
        [RequireSubmodule(ErpModules.Sub.PlatformAnalytics)]
        [ProducesResponseType(typeof(AnalyticsView), StatusCodes.Status200OK)]
        public async Task<ActionResult<AnalyticsView>> GetAnalytics(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            return Ok(await _analytics.GetPlatformAnalyticsAsync(from, to));
        }
    }
}
