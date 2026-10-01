using ERP_domain.entities;
using ERP_api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using ERP_api.DTOs;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Mvc;

namespace ERP_api.Controllers
{
    /// <summary>
    /// Operating costs other than payroll.
    /// </summary>
    [ApiController]
    [Route("api/expenses")]
    [Produces("application/json")]
    [Authorize]
    [RequireModule(ErpModules.Finance)]
    [RequireSubmodule(ErpModules.Sub.Expenses)]
    public class ExpensesController : ControllerBase
    {
        private readonly IExpenseService _expenseService;

        public ExpensesController(IExpenseService expenseService)
        {
            _expenseService = expenseService;
        }

        /// <summary>
        /// Filtering happens in the database rather than in the browser, so a long expense
        /// history does not have to be shipped to the client to be narrowed down.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<ExpenseView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<ExpenseView>>> GetAll(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string? category,
            [FromQuery] string? status)
        {
            return Ok(await _expenseService.GetExpensesAsync(from, to, category, status));
        }

        // Declared before the {id} route so the literal wins the match.
        [HttpGet("summary")]
        [ProducesResponseType(typeof(ExpenseSummary), StatusCodes.Status200OK)]
        public async Task<ActionResult<ExpenseSummary>> GetSummary(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            return Ok(await _expenseService.GetSummaryAsync(from, to));
        }

        [HttpGet("categories")]
        [ProducesResponseType(typeof(IEnumerable<string>), StatusCodes.Status200OK)]
        public ActionResult<IEnumerable<string>> GetCategories()
        {
            return Ok(ExpenseCategories.All);
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(ExpenseView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ExpenseView>> GetById(int id)
        {
            var expense = await _expenseService.GetExpenseByIdAsync(id);
            return expense is null ? NotFound() : Ok(expense);
        }

        [HttpPost]
        [ProducesResponseType(typeof(ExpenseView), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<ExpenseView>> Create([FromBody] CreateExpenseDto dto)
        {
            var expense = await _expenseService.CreateExpenseAsync(
                dto.Category, dto.Description, dto.Amount, dto.ExpenseDate,
                dto.PaymentMethod, dto.ReferenceNo, dto.RecordedByEmployeeId,
                dto.Status, dto.PaidTo, dto.SupplierId, dto.AccountId, dto.BankAccountId);

            return CreatedAtAction(nameof(GetById), new { id = expense.ExpenseId }, expense);
        }

        [HttpPut("{id:int}")]
        [ProducesResponseType(typeof(ExpenseView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ExpenseView>> Update(
            int id, [FromBody] UpdateExpenseDto dto)
        {
            var expense = await _expenseService.UpdateExpenseAsync(
                id, dto.Category, dto.Description, dto.Amount, dto.ExpenseDate,
                dto.PaymentMethod, dto.ReferenceNo, dto.RecordedByEmployeeId,
                dto.Status, dto.PaidTo, dto.SupplierId, dto.AccountId, dto.BankAccountId);

            return expense is null ? NotFound() : Ok(expense);
        }

        /// <summary>
        /// Settles an expense that was recorded as owed. Clears it from accounts payable and
        /// moves the money out of the cash or bank account it was paid from.
        /// </summary>
        [HttpPost("{id:int}/settle")]
        [ProducesResponseType(typeof(ExpenseView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ExpenseView>> Settle(int id, [FromBody] SettleExpenseDto dto)
        {
            var expense = await _expenseService.MarkPaidAsync(id, dto.PaymentMethod, dto.BankAccountId);
            return expense is null ? NotFound() : Ok(expense);
        }

        [HttpDelete("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(int id)
        {
            var deleted = await _expenseService.DeleteExpenseAsync(id);
            return deleted ? NoContent() : NotFound();
        }
    }
}
