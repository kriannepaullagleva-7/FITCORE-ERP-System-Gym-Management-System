using ERP_api.DTOs;
using ERP_api.Infrastructure;
using ERP_domain.entities;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP_api.Controllers
{
    /// <summary>
    /// Leave: time an employee is away, and whether they are paid for it.
    ///
    /// Part of Employee Management, because granting leave is a staffing decision. Payroll only
    /// reads the outcome - approved paid leave counts towards the hours a run is calculated
    /// from, and approved unpaid leave does not.
    /// </summary>
    [ApiController]
    [Route("api/leave")]
    [Produces("application/json")]
    [Authorize]
    [RequireModule(ErpModules.Employees)]
    [RequireSubmodule(ErpModules.Sub.Leave)]
    public class LeaveController : ControllerBase
    {
        private readonly ILeaveService _leave;

        public LeaveController(ILeaveService leave)
        {
            _leave = leave;
        }

        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<LeaveRequestView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<LeaveRequestView>>> GetAll(
            [FromQuery] int? employeeId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string? status)
        {
            return Ok(await _leave.GetRequestsAsync(employeeId, from, to, status));
        }

        [HttpGet("types")]
        [ProducesResponseType(typeof(IEnumerable<string>), StatusCodes.Status200OK)]
        public ActionResult<IEnumerable<string>> GetTypes() => Ok(LeaveTypes.All);

        [HttpGet("balance/{employeeId:int}")]
        [ProducesResponseType(typeof(LeaveBalanceView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<LeaveBalanceView>> GetBalance(
            int employeeId, [FromQuery] int? year)
        {
            return Ok(await _leave.GetBalanceAsync(employeeId, year));
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(LeaveRequestView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<LeaveRequestView>> GetById(int id)
        {
            var request = await _leave.GetRequestAsync(id);
            return request is null ? NotFound() : Ok(request);
        }

        [HttpPost]
        [ProducesResponseType(typeof(LeaveRequestView), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<LeaveRequestView>> Create(
            [FromBody] CreateLeaveRequestDto dto)
        {
            var request = await _leave.CreateRequestAsync(
                dto.EmployeeId, dto.LeaveType, dto.StartDate, dto.EndDate, dto.IsPaid, dto.Reason);

            return CreatedAtAction(nameof(GetById), new { id = request.LeaveRequestId }, request);
        }

        [HttpPut("{id:int}")]
        [ProducesResponseType(typeof(LeaveRequestView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<LeaveRequestView>> Update(
            int id, [FromBody] UpdateLeaveRequestDto dto)
        {
            var request = await _leave.UpdateRequestAsync(
                id, dto.LeaveType, dto.StartDate, dto.EndDate, dto.IsPaid, dto.Reason);

            return request is null ? NotFound() : Ok(request);
        }

        [HttpPost("{id:int}/decide")]
        [ProducesResponseType(typeof(LeaveRequestView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<LeaveRequestView>> Decide(
            int id, [FromBody] DecideLeaveRequestDto dto)
        {
            var request = await _leave.DecideAsync(id, dto.Approve, dto.Notes);
            return request is null ? NotFound() : Ok(request);
        }

        [HttpPost("{id:int}/cancel")]
        [ProducesResponseType(typeof(LeaveRequestView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<LeaveRequestView>> Cancel(
            int id, [FromBody] ReasonDto dto)
        {
            var request = await _leave.CancelAsync(id, dto.Reason);
            return request is null ? NotFound() : Ok(request);
        }

        [HttpDelete("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(int id)
        {
            var deleted = await _leave.DeleteAsync(id);
            return deleted ? NoContent() : NotFound();
        }
    }
}
