using ERP_domain.entities;
using ERP_api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using ERP_api.DTOs;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Mvc;

namespace ERP_api.Controllers
{
    /// <summary>
    /// Staff records. Payroll runs against these, and expenses can be attributed to them.
    /// </summary>
    [ApiController]
    [Route("api/employees")]
    [Produces("application/json")]
    [Authorize]
    [RequireModule(ErpModules.Employees)]
    public class EmployeesController : ControllerBase
    {
        private readonly IEmployeeService _employeeService;
        private readonly IPayrollService _payrollService;

        public EmployeesController(
            IEmployeeService employeeService,
            IPayrollService payrollService)
        {
            _employeeService = employeeService;
            _payrollService = payrollService;
        }

        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<EmployeeDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<EmployeeDto>>> GetAll()
        {
            var employees = await _employeeService.GetAllEmployeesAsync();
            return Ok(employees.Select(e => e.ToDto()));
        }

        // Declared before the {id} route so the literal wins the match.
        [HttpGet("active")]
        [ProducesResponseType(typeof(IEnumerable<EmployeeDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<EmployeeDto>>> GetActive()
        {
            var employees = await _employeeService.GetActiveEmployeesAsync();
            return Ok(employees.Select(e => e.ToDto()));
        }

        [HttpGet("search")]
        [ProducesResponseType(typeof(IEnumerable<EmployeeDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<EmployeeDto>>> Search([FromQuery] string? term)
        {
            var employees = await _employeeService.SearchEmployeesAsync(term ?? string.Empty);
            return Ok(employees.Select(e => e.ToDto()));
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(EmployeeDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<EmployeeDto>> GetById(int id)
        {
            var employee = await _employeeService.GetEmployeeByIdAsync(id);
            return employee is null ? NotFound() : Ok(employee.ToDto());
        }

        [HttpGet("{employeeId:int}/payrolls")]
        [ProducesResponseType(typeof(IEnumerable<PayrollView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<PayrollView>>> GetPayrolls(int employeeId)
        {
            return Ok(await _payrollService.GetEmployeePayrollsAsync(employeeId));
        }

        [HttpPost]
        [ProducesResponseType(typeof(EmployeeDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<EmployeeDto>> Create([FromBody] CreateEmployeeDto dto)
        {
            var employee = await _employeeService.CreateEmployeeAsync(
                dto.EmployeeCode, dto.FirstName, dto.LastName, dto.Position, dto.Department,
                dto.Phone, dto.Email, dto.HireDate, dto.BasicSalary);

            return CreatedAtAction(
                nameof(GetById), new { id = employee.EmployeeId }, employee.ToDto());
        }

        [HttpPut("{id:int}")]
        [ProducesResponseType(typeof(EmployeeDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<EmployeeDto>> Update(
            int id, [FromBody] UpdateEmployeeDto dto)
        {
            var employee = await _employeeService.UpdateEmployeeAsync(
                id, dto.EmployeeCode, dto.FirstName, dto.LastName, dto.Position, dto.Department,
                dto.Phone, dto.Email, dto.HireDate, dto.BasicSalary, dto.Status);

            return employee is null ? NotFound() : Ok(employee.ToDto());
        }

        [HttpDelete("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _employeeService.DeleteEmployeeAsync(id);

            return result switch
            {
                EmployeeDeleteResult.Deleted => NoContent(),
                EmployeeDeleteResult.HasPayrollHistory => Conflict(new
                {
                    message = "This employee has payroll runs on record. " +
                              "Set their status to Inactive instead of deleting them."
                }),
                _ => NotFound()
            };
        }
    }
}
