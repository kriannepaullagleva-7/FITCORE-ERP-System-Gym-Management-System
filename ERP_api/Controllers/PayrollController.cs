using ERP_domain.entities;
using ERP_api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using ERP_api.DTOs;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Mvc;

namespace ERP_api.Controllers
{
    /// <summary>
    /// Pay runs.
    ///
    /// Gross and net pay never arrive from the client: the request carries basic salary,
    /// allowances and deductions, and the service computes the totals.
    /// </summary>
    [ApiController]
    [Route("api/payroll")]
    [Produces("application/json")]
    [Authorize]
    [RequireModule(ErpModules.Payroll)]
    public class PayrollController : ControllerBase
    {
        private readonly IPayrollService _payrollService;

        public PayrollController(IPayrollService payrollService)
        {
            _payrollService = payrollService;
        }

        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<PayrollView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<PayrollView>>> GetAll()
        {
            return Ok(await _payrollService.GetAllPayrollsAsync());
        }

        // Declared before the {id} route so the literal wins the match.
        [HttpGet("summary")]
        [ProducesResponseType(typeof(PayrollSummary), StatusCodes.Status200OK)]
        public async Task<ActionResult<PayrollSummary>> GetSummary()
        {
            return Ok(await _payrollService.GetSummaryAsync());
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(PayrollView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PayrollView>> GetById(int id)
        {
            var payroll = await _payrollService.GetPayrollByIdAsync(id);
            return payroll is null ? NotFound() : Ok(payroll);
        }

        [HttpPost]
        [ProducesResponseType(typeof(PayrollView), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<PayrollView>> Create([FromBody] CreatePayrollDto dto)
        {
            var payroll = await _payrollService.CreatePayrollAsync(
                dto.EmployeeId, dto.PeriodStart, dto.PeriodEnd,
                dto.BasicSalary, dto.Allowances, dto.Deductions,
                dto.OvertimeHours, dto.OvertimeRate, dto.Notes);

            return CreatedAtAction(nameof(GetById), new { id = payroll.PayrollId }, payroll);
        }

        [HttpPut("{id:int}")]
        [ProducesResponseType(typeof(PayrollView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PayrollView>> Update(
            int id, [FromBody] UpdatePayrollDto dto)
        {
            var payroll = await _payrollService.UpdatePayrollAsync(
                id, dto.PeriodStart, dto.PeriodEnd,
                dto.BasicSalary, dto.Allowances, dto.Deductions,
                dto.OvertimeHours, dto.OvertimeRate, dto.Notes);

            return payroll is null ? NotFound() : Ok(payroll);
        }

        [HttpPatch("{id:int}/status")]
        [ProducesResponseType(typeof(PayrollView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PayrollView>> UpdateStatus(
            int id, [FromBody] UpdatePayrollStatusDto dto)
        {
            var payroll = await _payrollService.SetStatusAsync(id, dto.Status);
            return payroll is null ? NotFound() : Ok(payroll);
        }

        [HttpDelete("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(int id)
        {
            var deleted = await _payrollService.DeletePayrollAsync(id);
            return deleted ? NoContent() : NotFound();
        }
    }
}
