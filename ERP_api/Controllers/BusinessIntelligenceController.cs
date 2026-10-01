using ERP_api.Infrastructure;
using ERP_domain.entities;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP_api.Controllers
{
    /// <summary>
    /// Business Intelligence: the KPIs and charts for one tenant.
    ///
    /// Every area answers the same shape - a set of KPI groups, a set of chart definitions and
    /// the tables behind them - so the desktop has one analytics screen rather than eight, and
    /// adding an area is a method on the server rather than a new form.
    ///
    /// The dashboard and the operational reports live on <c>/api/reports</c>, which predates
    /// this controller and is what the front desk already calls. Those are Micro-tier
    /// subfeatures; everything here is Medium, which is why they are separated rather than
    /// merged.
    /// </summary>
    [ApiController]
    [Route("api/bi")]
    [Produces("application/json")]
    [Authorize]
    [RequireModule(ErpModules.BusinessIntelligence)]
    public class BusinessIntelligenceController : ControllerBase
    {
        private readonly IBusinessIntelligenceService _analytics;
        private readonly IBranchService _branches;

        public BusinessIntelligenceController(
            IBusinessIntelligenceService analytics, IBranchService branches)
        {
            _analytics = analytics;
            _branches = branches;
        }

        /// <summary>Every KPI FitCore measures, across all nine modules, in one round trip.</summary>
        [HttpGet("kpi")]
        [RequireSubmodule(ErpModules.Sub.KpiDashboard)]
        [ProducesResponseType(typeof(AnalyticsView), StatusCodes.Status200OK)]
        public async Task<ActionResult<AnalyticsView>> GetKpiDashboard(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            return Ok(await _analytics.GetKpiDashboardAsync(from, to));
        }

        [HttpGet("areas")]
        [RequireSubmodule(ErpModules.Sub.KpiDashboard)]
        [ProducesResponseType(typeof(IEnumerable<string>), StatusCodes.Status200OK)]
        public ActionResult<IEnumerable<string>> GetAreas() => Ok(AnalyticsAreas.All);

        /// <summary>
        /// The charted overview: the headline KPIs of three modules beside revenue-versus-
        /// expenses, sales by day and the member mix.
        ///
        /// This is the area <c>/kpi</c> is not. That one answers "every figure we measure" and
        /// is all cards by design, because forty-six charts in one response is not a dashboard.
        /// This one answers "how is the gym doing" and is mostly charts. Both are listed by
        /// <c>/areas</c>, so both need a route.
        /// </summary>
        [HttpGet("overview")]
        [RequireSubmodule(ErpModules.Sub.KpiDashboard)]
        [ProducesResponseType(typeof(AnalyticsView), StatusCodes.Status200OK)]
        public Task<ActionResult<AnalyticsView>> GetOverview(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to) =>
            AreaAsync(AnalyticsAreas.Overview, from, to);

        [HttpGet("membership")]
        [RequireSubmodule(ErpModules.Sub.MembershipAnalytics)]
        [ProducesResponseType(typeof(AnalyticsView), StatusCodes.Status200OK)]
        public Task<ActionResult<AnalyticsView>> GetMembership(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to) =>
            AreaAsync(AnalyticsAreas.Membership, from, to);

        [HttpGet("sales")]
        [RequireSubmodule(ErpModules.Sub.SalesAnalytics)]
        [ProducesResponseType(typeof(AnalyticsView), StatusCodes.Status200OK)]
        public Task<ActionResult<AnalyticsView>> GetSales(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to) =>
            AreaAsync(AnalyticsAreas.Sales, from, to);

        [HttpGet("payments")]
        [RequireSubmodule(ErpModules.Sub.PaymentAnalytics)]
        [ProducesResponseType(typeof(AnalyticsView), StatusCodes.Status200OK)]
        public Task<ActionResult<AnalyticsView>> GetPayments(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to) =>
            AreaAsync(AnalyticsAreas.Payments, from, to);

        [HttpGet("inventory")]
        [RequireSubmodule(ErpModules.Sub.InventoryAnalytics)]
        [ProducesResponseType(typeof(AnalyticsView), StatusCodes.Status200OK)]
        public Task<ActionResult<AnalyticsView>> GetInventory(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to) =>
            AreaAsync(AnalyticsAreas.Inventory, from, to);

        [HttpGet("workforce")]
        [RequireSubmodule(ErpModules.Sub.WorkforceAnalytics)]
        [ProducesResponseType(typeof(AnalyticsView), StatusCodes.Status200OK)]
        public Task<ActionResult<AnalyticsView>> GetWorkforce(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to) =>
            AreaAsync(AnalyticsAreas.Workforce, from, to);

        [HttpGet("finance")]
        [RequireSubmodule(ErpModules.Sub.FinanceAnalytics)]
        [ProducesResponseType(typeof(AnalyticsView), StatusCodes.Status200OK)]
        public Task<ActionResult<AnalyticsView>> GetFinance(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to) =>
            AreaAsync(AnalyticsAreas.Finance, from, to);

        [HttpGet("profitability")]
        [RequireSubmodule(ErpModules.Sub.ProfitabilityAnalytics)]
        [ProducesResponseType(typeof(AnalyticsView), StatusCodes.Status200OK)]
        public Task<ActionResult<AnalyticsView>> GetProfitability(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to) =>
            AreaAsync(AnalyticsAreas.Profitability, from, to);

        /// <summary>
        /// The whole company beside each of its branches, over one period.
        ///
        /// Deliberately not affected by the branch picker: this endpoint reads across every
        /// branch on purpose, which is what makes a comparison possible. That is why it is an
        /// Admin subfeature - a branch manager is bound to one branch, and a comparison is by
        /// definition everybody else's figures.
        /// </summary>
        [HttpGet("branches")]
        [RequireSubmodule(ErpModules.Sub.BranchPerformance)]
        [ProducesResponseType(typeof(BranchComparisonView), StatusCodes.Status200OK)]
        public async Task<ActionResult<BranchComparisonView>> GetBranchPerformance(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            var start = from ?? DateTime.UtcNow.Date.AddMonths(-1);
            var end = to ?? DateTime.UtcNow.Date;

            return Ok(await _branches.CompareAsync(start, end));
        }

        private async Task<ActionResult<AnalyticsView>> AreaAsync(
            string area, DateTime? from, DateTime? to)
        {
            return Ok(await _analytics.GetAnalyticsAsync(area, from, to));
        }
    }
}
