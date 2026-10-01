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
    [RequireModule(ErpModules.BusinessIntelligence)]
    [RequireSubmodule(ErpModules.Sub.OperationalReports)]
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

        // The dashboard is the landing page for every role that holds Business Intelligence.
        // Staff no longer hold the module at all - by design, since Business Intelligence is
        // withdrawn from Staff entirely rather than merely narrowed - so this now behaves
        // exactly like every other BI submodule: reachable to the roles the module reaches.
        [HttpGet("dashboard")]
        [RequireSubmodule(ErpModules.Sub.ExecutiveDashboard)]
        [ProducesResponseType(typeof(DashboardSummary), StatusCodes.Status200OK)]
        public async Task<ActionResult<DashboardSummary>> GetDashboard()
        {
            var summary = await _dashboardService.GetSummaryAsync();
            return Ok(summary);
        }

        // This is the Subscriptions tab's own data, not a report - a front-desk Staff member on
        // a Micro tenant needs it exactly as much as Members or Membership Plans. Without this
        // override the class-level OperationalReports rule above still applied even though the
        // action already overrode RequireModule, so a tenant with no Reports subfeature (Micro,
        // and Staff on every tier) got a 403 opening a tab their own module includes.
        [HttpGet("membership-overview")]
        [RequireModule(ErpModules.Membership)]
        [RequireSubmodule(ErpModules.Sub.Subscriptions)]
        [ProducesResponseType(typeof(IEnumerable<MembershipView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<MembershipView>>> GetMembershipOverview()
        {
            var overview = await _memberService.GetMembershipOverviewAsync();
            return Ok(overview);
        }

        // Each module report is guarded by the module it reports on, not by Business
        // Intelligence. The controller-level pair is the fallback; these overrides are what
        // make the per-module Reports submodules mean something.
        //
        // It matters because the desktop draws a Reports tab inside Membership, Payments,
        // Sales and Inventory, and guards each tab on the matching submodule. Left on the
        // controller's Business Intelligence rule, an administrator who withdrew BI from a
        // manager would leave those tabs drawn and every one of them answering 403 - the tab
        // and the endpoint disagreeing, which is the one combination the courtesy of hiding a
        // tab is supposed to prevent.
        [HttpGet("sales")]
        [RequireModule(ErpModules.Sales)]
        [RequireSubmodule(ErpModules.Sub.SalesReports)]
        [ProducesResponseType(typeof(SalesReport), StatusCodes.Status200OK)]
        public async Task<ActionResult<SalesReport>> GetSalesReport(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            return Ok(await _reportService.GetSalesReportAsync(from, to));
        }

        [HttpGet("payments")]
        [RequireModule(ErpModules.Payments)]
        [RequireSubmodule(ErpModules.Sub.PaymentReports)]
        [ProducesResponseType(typeof(PaymentReport), StatusCodes.Status200OK)]
        public async Task<ActionResult<PaymentReport>> GetPaymentReport(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            return Ok(await _reportService.GetPaymentReportAsync(from, to));
        }

        [HttpGet("inventory")]
        [RequireModule(ErpModules.Inventory)]
        [RequireSubmodule(ErpModules.Sub.InventoryReports)]
        [ProducesResponseType(typeof(InventoryReport), StatusCodes.Status200OK)]
        public async Task<ActionResult<InventoryReport>> GetInventoryReport(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            return Ok(await _reportService.GetInventoryReportAsync(from, to));
        }

        [HttpGet("membership")]
        [RequireModule(ErpModules.Membership)]
        [RequireSubmodule(ErpModules.Sub.MembershipReports)]
        [ProducesResponseType(typeof(MembershipReport), StatusCodes.Status200OK)]
        public async Task<ActionResult<MembershipReport>> GetMembershipReport(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            return Ok(await _reportService.GetMembershipReportAsync(from, to));
        }

        [HttpGet("employees")]
        [RequireModule(ErpModules.Employees)]
        [RequireSubmodule(ErpModules.Sub.EmployeeReports)]
        [ProducesResponseType(typeof(EmployeeReport), StatusCodes.Status200OK)]
        public async Task<ActionResult<EmployeeReport>> GetEmployeeReport(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            return Ok(await _reportService.GetEmployeeReportAsync(from, to));
        }

        [HttpGet("payroll")]
        [RequireModule(ErpModules.Payroll)]
        [RequireSubmodule(ErpModules.Sub.PayrollReports)]
        [ProducesResponseType(typeof(PayrollReport), StatusCodes.Status200OK)]
        public async Task<ActionResult<PayrollReport>> GetPayrollReport(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            return Ok(await _reportService.GetPayrollReportAsync(from, to));
        }

        // Reconciliation compares the takings against the general ledger, so it needs the
        // ledger to exist - which is why the submodule behind it is Medium even though the rest
        // of Payment Management is available from Micro upwards.
        [HttpGet("payment-reconciliation")]
        [RequireModule(ErpModules.Payments)]
        [RequireSubmodule(ErpModules.Sub.PaymentReconciliation)]
        [ProducesResponseType(typeof(PaymentReconciliationView), StatusCodes.Status200OK)]
        public async Task<ActionResult<PaymentReconciliationView>> GetPaymentReconciliation(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            return Ok(await _reportService.GetPaymentReconciliationAsync(from, to));
        }

        // Expenses are a Finance subfeature, so the expense report is gated exactly as
        // /api/expenses is. On the controller's Business Intelligence rule a Micro or Small
        // manager could read every expense row here that /api/expenses refuses them - the same
        // data, two answers, which is the kind of gap a report endpoint is easy to leave open.
        [HttpGet("expenses")]
        [RequireModule(ErpModules.Finance)]
        [RequireSubmodule(ErpModules.Sub.Expenses)]
        [ProducesResponseType(typeof(ExpenseReport), StatusCodes.Status200OK)]
        public async Task<ActionResult<ExpenseReport>> GetExpenseReport(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            return Ok(await _reportService.GetExpenseReportAsync(from, to));
        }
    }
}
