using ERP_api.DTOs;
using ERP_api.Infrastructure;
using ERP_domain.entities;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP_api.Controllers
{
    /// <summary>
    /// Purchasing: buying stock, receiving it, and paying for it.
    ///
    /// Part of Inventory Management rather than a module of its own, because a purchase order
    /// only exists to put stock on a shelf. It is gated on the Inventory module and the
    /// Purchases subfeature, so a receptionist who can count stock still cannot commit the gym
    /// to spending money.
    /// </summary>
    [ApiController]
    [Route("api/purchases")]
    [Produces("application/json")]
    [Authorize]
    [RequireModule(ErpModules.Inventory)]
    [RequireSubmodule(ErpModules.Sub.Purchases)]
    public class PurchasesController : ControllerBase
    {
        private readonly IPurchaseService _purchases;

        public PurchasesController(IPurchaseService purchases)
        {
            _purchases = purchases;
        }

        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<PurchaseView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<PurchaseView>>> GetAll(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string? status,
            [FromQuery] int? supplierId)
        {
            return Ok(await _purchases.GetPurchasesAsync(from, to, status, supplierId));
        }

        // Literal routes are declared before the parameter route so they win the match.
        [HttpGet("summary")]
        [ProducesResponseType(typeof(PurchaseSummary), StatusCodes.Status200OK)]
        public async Task<ActionResult<PurchaseSummary>> GetSummary()
        {
            return Ok(await _purchases.GetSummaryAsync());
        }

        [HttpGet("statuses")]
        [ProducesResponseType(typeof(IEnumerable<string>), StatusCodes.Status200OK)]
        public ActionResult<IEnumerable<string>> GetStatuses() => Ok(PurchaseStatuses.All);

        [HttpGet("payments")]
        [ProducesResponseType(typeof(IEnumerable<SupplierPaymentView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<SupplierPaymentView>>> GetSupplierPayments(
            [FromQuery] int? supplierId,
            [FromQuery] int? purchaseId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to)
        {
            return Ok(await _purchases.GetSupplierPaymentsAsync(supplierId, purchaseId, from, to));
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(PurchaseView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PurchaseView>> GetById(int id)
        {
            var purchase = await _purchases.GetPurchaseAsync(id);
            return purchase is null ? NotFound() : Ok(purchase);
        }

        [HttpPost]
        [ProducesResponseType(typeof(PurchaseView), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<PurchaseView>> Create([FromBody] CreatePurchaseDto dto)
        {
            var purchase = await _purchases.CreatePurchaseAsync(
                dto.SupplierId, dto.OrderDate, dto.ExpectedDate, dto.SupplierReference,
                dto.Discount, dto.Tax, dto.Notes, dto.Lines.Select(ToRequest));

            return CreatedAtAction(nameof(GetById), new { id = purchase.PurchaseId }, purchase);
        }

        [HttpPut("{id:int}")]
        [ProducesResponseType(typeof(PurchaseView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PurchaseView>> Update(
            int id, [FromBody] UpdatePurchaseDto dto)
        {
            var purchase = await _purchases.UpdateDraftAsync(
                id, dto.SupplierId, dto.OrderDate, dto.ExpectedDate, dto.SupplierReference,
                dto.Discount, dto.Tax, dto.Notes, dto.Lines.Select(ToRequest));

            return purchase is null ? NotFound() : Ok(purchase);
        }

        [HttpPost("{id:int}/order")]
        [ProducesResponseType(typeof(PurchaseView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PurchaseView>> MarkOrdered(int id)
        {
            var purchase = await _purchases.MarkOrderedAsync(id);
            return purchase is null ? NotFound() : Ok(purchase);
        }

        /// <summary>
        /// Receives the goods. This is the event that puts stock on the shelf at what it cost,
        /// moves each product's weighted average, and raises what is owed to the supplier.
        /// </summary>
        [HttpPost("{id:int}/receive")]
        [ProducesResponseType(typeof(PurchaseView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PurchaseView>> Receive(
            int id, [FromBody] ReceivePurchaseDto? dto)
        {
            var purchase = await _purchases.ReceiveAsync(id, dto?.ReceivedByItemId);
            return purchase is null ? NotFound() : Ok(purchase);
        }

        [HttpPost("{id:int}/cancel")]
        [ProducesResponseType(typeof(PurchaseView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PurchaseView>> Cancel(
            int id, [FromBody] CancelPurchaseDto dto)
        {
            var purchase = await _purchases.CancelAsync(id, dto.Reason);
            return purchase is null ? NotFound() : Ok(purchase);
        }

        [HttpPost("payments")]
        [ProducesResponseType(typeof(SupplierPaymentView), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<SupplierPaymentView>> PaySupplier(
            [FromBody] PaySupplierDto dto)
        {
            var payment = await _purchases.PaySupplierAsync(
                dto.SupplierId, dto.PurchaseId, dto.Amount, dto.PaymentDate,
                dto.Method, dto.ReferenceNo, dto.Notes, dto.BankAccountId);

            return CreatedAtAction(nameof(GetSupplierPayments), null, payment);
        }

        [HttpDelete("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(int id)
        {
            var deleted = await _purchases.DeleteDraftAsync(id);
            return deleted ? NoContent() : NotFound();
        }

        private static PurchaseLineRequest ToRequest(PurchaseLineDto dto) => new()
        {
            ProductId = dto.ProductId,
            Quantity = dto.Quantity,
            UnitCost = dto.UnitCost
        };
    }
}
