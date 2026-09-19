using ERP_domain.entities;
using ERP_api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Mvc;

namespace ERP_api.Controllers
{
    /// <summary>
    /// Cross-module read models: the executive dashboard and the module reports. These are
    /// reports rather than resources, so they are grouped here instead of being hung off
    /// whichever module happens to own most of the data.
    ///
    /// Every report takes an optional date range. Omitting it defaults to the current month,
    /// and the filtering is done in the database, not in the browser.
    /// </summary>
    [ApiController]
    [Route("api/reports")]
    [Produces("application/json")]
    [Authorize]
    [RequireModule(ErpModules.Reports)]
    public class ReportsController : ControllerBase
    {
        private readonly IDashboardService _dashboardService;
        private readonly IMemberService _memberService;
        private readonly IReportService _reportService;

        public ReportsController(
            IDashboardService dashboardService,
            IMemberService memberService,
            IReportService reportService)
        {
            _dashboardService = dashboardService;
            _memberService = memberService;
            _reportService = reportService;
        }

        // The dashboard is the landing page for every role, so it is gated on the Dashboard
        // module rather than on Reports, which only managers and owners hold by default.
        [HttpGet("dashboard")]
        [RequireModule(ErpModules.Dashboard)]
        [ProducesResponseType(typeof(DashboardSummary), StatusCodes.Status200OK)]
        public async Task<ActionResult<DashboardSummary>> GetDashboard()
        {
            var summary = await _dashboardService.GetSummaryAsync();
            return Ok(summary);
        }

        [HttpGet("membership-overview")]
        [RequireModule(ErpModules.Membership)]
        [ProducesResponseType(typeof(IEnumerable<MembershipView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<MembershipView>>> GetMembershipOverview()
        {
            var overview = await _memberService.GetMembershipOverviewAsync();
            return Ok(overview);
        }

        [HttpGet("sales")]
        [ProducesResponseType(typeof(SalesReport), StatusCodes.Status200OK)]
        public async Task<ActionResult<SalesReport>> GetSalesReport(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            return Ok(await _reportService.GetSalesReportAsync(from, to));
        }

        [HttpGet("payments")]
        [ProducesResponseType(typeof(PaymentReport), StatusCodes.Status200OK)]
        public async Task<ActionResult<PaymentReport>> GetPaymentReport(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            return Ok(await _reportService.GetPaymentReportAsync(from, to));
        }

        [HttpGet("inventory")]
        [ProducesResponseType(typeof(InventoryReport), StatusCodes.Status200OK)]
        public async Task<ActionResult<InventoryReport>> GetInventoryReport(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            return Ok(await _reportService.GetInventoryReportAsync(from, to));
        }

        [HttpGet("membership")]
        [ProducesResponseType(typeof(MembershipReport), StatusCodes.Status200OK)]
        public async Task<ActionResult<MembershipReport>> GetMembershipReport(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            return Ok(await _reportService.GetMembershipReportAsync(from, to));
        }

        [HttpGet("expenses")]
        [ProducesResponseType(typeof(ExpenseReport), StatusCodes.Status200OK)]
        public async Task<ActionResult<ExpenseReport>> GetExpenseReport(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            return Ok(await _reportService.GetExpenseReportAsync(from, to));
        }
    }
}
