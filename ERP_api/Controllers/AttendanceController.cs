using ERP_domain.entities;
using ERP_api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using ERP_api.DTOs;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Mvc;

namespace ERP_api.Controllers
{
    /// <summary>
    /// Attendance is a submodule of Employees, not a module of its own - it is gated on the
    /// same permission rather than a separate one, and has no entry of its own in the sidebar
    /// or the module catalogue.
    /// </summary>
    [ApiController]
    [Route("api/attendance")]
    [Produces("application/json")]
    [Authorize]
    [RequireModule(ErpModules.Employees)]
    public class AttendanceController : ControllerBase
    {
        private readonly IAttendanceService _attendanceService;

        public AttendanceController(IAttendanceService attendanceService)
        {
            _attendanceService = attendanceService;
        }

        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<AttendanceDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<AttendanceDto>>> GetAll(
            [FromQuery] int? employeeId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            var rows = await _attendanceService.GetAllAsync(employeeId, from, to);
            return Ok(rows.Select(r => r.ToDto()));
        }

        // Declared before the {id} route so the literal wins the match.
        [HttpGet("summary")]
        [ProducesResponseType(typeof(AttendancePeriodSummary), StatusCodes.Status200OK)]
        public async Task<ActionResult<AttendancePeriodSummary>> GetSummary(
            [FromQuery] int employeeId, [FromQuery] DateTime periodStart, [FromQuery] DateTime periodEnd)
        {
            return Ok(await _attendanceService.GetPeriodSummaryAsync(employeeId, periodStart, periodEnd));
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(AttendanceDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<AttendanceDto>> GetById(int id)
        {
            var row = await _attendanceService.GetByIdAsync(id);
            return row is null ? NotFound() : Ok(row.ToDto());
        }

        [HttpPost]
        [ProducesResponseType(typeof(AttendanceDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<AttendanceDto>> Create([FromBody] CreateAttendanceDto dto)
        {
            var row = await _attendanceService.CreateAsync(
                dto.EmployeeId, dto.Date, dto.TimeIn, dto.TimeOut, dto.Status, dto.Notes);

            return CreatedAtAction(nameof(GetById), new { id = row.AttendanceId }, row.ToDto());
        }

        [HttpPut("{id:int}")]
        [ProducesResponseType(typeof(AttendanceDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<AttendanceDto>> Update(int id, [FromBody] UpdateAttendanceDto dto)
        {
            var row = await _attendanceService.UpdateAsync(
                id, dto.Date, dto.TimeIn, dto.TimeOut, dto.Status, dto.Notes);

            return row is null ? NotFound() : Ok(row.ToDto());
        }

        [HttpDelete("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(int id)
        {
            var deleted = await _attendanceService.DeleteAsync(id);
            return deleted ? NoContent() : NotFound();
        }
    }
}
