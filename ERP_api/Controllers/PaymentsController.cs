using ERP_domain.entities;
using ERP_api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using ERP_api.DTOs;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Mvc;

namespace ERP_api.Controllers
{
    /// <summary>
    /// Payments: against a subscription, against a sale, or standalone.
    /// </summary>
    [ApiController]
    [Route("api/payments")]
    [Produces("application/json")]
    [Authorize]
    [RequireModule(ErpModules.Payments)]
    public class PaymentsController : ControllerBase
    {
        private readonly IPaymentService _paymentService;

        public PaymentsController(IPaymentService paymentService)
        {
            _paymentService = paymentService;
        }

        /// <summary>
        /// All payments, or those inside a date range when one is given.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<PaymentView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<PaymentView>>> GetAll(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            if (from.HasValue || to.HasValue)
            {
                var range = ReportRange.Resolve(from, to);
                return Ok(await _paymentService.GetPaymentsInRangeAsync(range.FromUtc, range.ToUtc));
            }

            return Ok(await _paymentService.GetAllPaymentsAsync());
        }

        /// <summary>Collection totals and what is still outstanding.</summary>
        [HttpGet("summary")]
        [ProducesResponseType(typeof(PaymentSummary), StatusCodes.Status200OK)]
        public async Task<ActionResult<PaymentSummary>> GetSummary()
        {
            return Ok(await _paymentService.GetSummaryAsync());
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PaymentDto>> GetById(int id)
        {
            var payment = await _paymentService.GetPaymentByIdAsync(id);
            return payment is null ? NotFound() : Ok(payment.ToDto());
        }

        [HttpPost]
        [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<PaymentDto>> Record([FromBody] RecordPaymentDto dto)
        {
            var payment = await _paymentService.RecordPaymentAsync(
                dto.MemberId,
                dto.SubscriptionId,
                dto.SaleId,
                dto.Amount,
                dto.PaymentDate ?? DateTime.UtcNow,
                dto.Method,
                dto.ReferenceNo,
                dto.Status,
                dto.Notes);

            return CreatedAtAction(
                nameof(GetById), new { id = payment.PaymentId }, payment.ToDto());
        }

        [HttpPut("{id:int}")]
        [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PaymentDto>> Update(
            int id, [FromBody] UpdatePaymentDto dto)
        {
            var payment = await _paymentService.UpdatePaymentAsync(
                id,
                dto.Amount,
                dto.PaymentDate ?? default,
                dto.Method,
                dto.ReferenceNo,
                dto.Status,
                dto.Notes);

            return payment is null ? NotFound() : Ok(payment.ToDto());
        }

        [HttpPatch("{id:int}/status")]
        [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PaymentDto>> UpdateStatus(
            int id, [FromBody] UpdatePaymentStatusDto dto)
        {
            var payment = await _paymentService.UpdatePaymentStatusAsync(id, dto.Status);
            return payment is null ? NotFound() : Ok(payment.ToDto());
        }

        /// <summary>
        /// Reverses a payment by marking it Refunded. The row stays, because destroying a
        /// settled financial record would leave the ledger unable to explain itself.
        /// </summary>
        [HttpPost("{id:int}/void")]
        [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PaymentDto>> Void(int id, [FromBody] VoidPaymentDto? dto)
        {
            var payment = await _paymentService.VoidPaymentAsync(id, dto?.Reason ?? string.Empty);
            return payment is null ? NotFound() : Ok(payment.ToDto());
        }

        [HttpDelete("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(int id)
        {
            var deleted = await _paymentService.DeletePaymentAsync(id);
            return deleted ? NoContent() : NotFound();
        }
    }
}
