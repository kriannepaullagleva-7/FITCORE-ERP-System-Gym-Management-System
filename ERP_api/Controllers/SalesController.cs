using ERP_domain.entities;
using ERP_api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using ERP_api.DTOs;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Mvc;

namespace ERP_api.Controllers
{
    /// <summary>
    /// Point-of-sale transactions. Sale lines are managed through their parent sale rather
    /// than through a controller of their own.
    /// </summary>
    [ApiController]
    [Route("api/sales")]
    [Produces("application/json")]
    [Authorize]
    [RequireModule(ErpModules.Sales)]
    public class SalesController : ControllerBase
    {
        private readonly ISaleService _saleService;
        private readonly IPaymentService _paymentService;

        public SalesController(ISaleService saleService, IPaymentService paymentService)
        {
            _saleService = saleService;
            _paymentService = paymentService;
        }

        /// <summary>
        /// All sales, or those inside a date range when one is given. Filtering by date
        /// happens in the database rather than in the browser.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<SaleView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<SaleView>>> GetAll(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            if (from.HasValue || to.HasValue)
            {
                var range = ReportRange.Resolve(from, to);
                return Ok(await _saleService.GetSalesInRangeAsync(range.FromUtc, range.ToUtc));
            }

            return Ok(await _saleService.GetAllSalesAsync());
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(SaleDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<SaleDto>> GetById(int id)
        {
            var sale = await _saleService.GetSaleByIdAsync(id);
            return sale is null ? NotFound() : Ok(sale.ToDto());
        }

        /// <summary>The sale, its lines and the payments recorded against it.</summary>
        [HttpGet("{id:int}/detail")]
        [ProducesResponseType(typeof(SaleDetailView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<SaleDetailView>> GetDetail(int id)
        {
            var detail = await _saleService.GetSaleDetailAsync(id);
            return detail is null ? NotFound() : Ok(detail);
        }

        [HttpGet("{id:int}/items")]
        [ProducesResponseType(typeof(IEnumerable<SaleLineView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<SaleLineView>>> GetItems(int id)
        {
            var lines = await _saleService.GetSaleLinesAsync(id);
            return Ok(lines);
        }

        [HttpGet("{id:int}/payments")]
        [ProducesResponseType(typeof(IEnumerable<PaymentView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<PaymentView>>> GetPayments(int id)
        {
            return Ok(await _paymentService.GetSalePaymentsAsync(id));
        }

        /// <summary>
        /// Rings up a sale. The caller sends products and quantities only: unit prices are
        /// read from the catalogue on the server, so the browser cannot set its own price.
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(SaleDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<SaleDto>> Create([FromBody] CreateSaleDto dto)
        {
            var items = dto.Items
                .Select(i => new SaleLineRequest { ProductId = i.ProductId, Quantity = i.Quantity })
                .ToList();

            var sale = await _saleService.CreateSaleAsync(
                dto.MemberId, items, dto.Discount, dto.CashierEmployeeId, dto.Notes,
                dto.SettleNow, dto.PaymentMethod, dto.WalkInName, dto.AmountTendered);

            return CreatedAtAction(nameof(GetById), new { id = sale.SaleId }, sale.ToDto());
        }

        /// <summary>
        /// Reverses a sale: stock goes back and the record is kept as Cancelled. This is the
        /// safe counterpart to Delete for anything that has money attached to it.
        /// </summary>
        [HttpPost("{id:int}/cancel")]
        [ProducesResponseType(typeof(SaleView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<SaleView>> Cancel(int id, [FromBody] CancelSaleDto? dto)
        {
            var sale = await _saleService.CancelSaleAsync(id, dto?.Reason ?? string.Empty);
            return sale is null ? NotFound() : Ok(sale);
        }

        [HttpDelete("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(int id)
        {
            var deleted = await _saleService.DeleteSaleAsync(id);
            return deleted ? NoContent() : NotFound();
        }
    }
}
