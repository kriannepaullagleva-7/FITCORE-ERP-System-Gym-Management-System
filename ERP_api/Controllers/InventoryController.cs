using ERP_domain.entities;
using ERP_api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using ERP_api.DTOs;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Mvc;

namespace ERP_api.Controllers
{
    /// <summary>
    /// Stock on hand and the movements that change it.
    /// </summary>
    [ApiController]
    [Route("api/inventory")]
    [Produces("application/json")]
    [Authorize]
    [RequireModule(ErpModules.Inventory)]
    public class InventoryController : ControllerBase
    {
        private readonly IInventoryService _inventoryService;

        public InventoryController(IInventoryService inventoryService)
        {
            _inventoryService = inventoryService;
        }

        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<InventoryView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<InventoryView>>> GetAll()
        {
            var inventory = await _inventoryService.GetInventoryAsync();
            return Ok(inventory);
        }

        /// <summary>Headline stock counts and valuations for the Inventory KPIs.</summary>
        [HttpGet("summary")]
        [ProducesResponseType(typeof(InventorySummary), StatusCodes.Status200OK)]
        public async Task<ActionResult<InventorySummary>> GetSummary()
        {
            return Ok(await _inventoryService.GetSummaryAsync());
        }

        // Declared before the {productId} route so the literal wins the match.
        [HttpGet("movements")]
        [ProducesResponseType(typeof(IEnumerable<StockMovementView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<StockMovementView>>> GetMovements(
            [FromQuery] int? productId, [FromQuery] int? take)
        {
            var movements = await _inventoryService.GetMovementsAsync(productId, take ?? 200);
            return Ok(movements);
        }

        [HttpGet("{productId:int}")]
        [ProducesResponseType(typeof(InventoryView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<InventoryView>> GetByProductId(int productId)
        {
            var row = await _inventoryService.GetByProductIdAsync(productId);
            return row is null ? NotFound() : Ok(row);
        }

        [HttpPost("{productId:int}/stock-in")]
        [ProducesResponseType(typeof(InventoryView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<InventoryView>> StockIn(
            int productId, [FromBody] StockInRequestDto dto)
        {
            var row = await _inventoryService.StockInAsync(
                productId, dto.Quantity, dto.SupplierId, dto.Reference, dto.Notes,
                dto.RecordedByEmployeeId);

            return Ok(row);
        }

        [HttpPost("{productId:int}/stock-out")]
        [ProducesResponseType(typeof(InventoryView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<InventoryView>> StockOut(
            int productId, [FromBody] StockOutRequestDto dto)
        {
            var row = await _inventoryService.StockOutAsync(
                productId, dto.Quantity, dto.Reason, dto.Notes, dto.RecordedByEmployeeId);

            return Ok(row);
        }

        [HttpPost("{productId:int}/adjust")]
        [ProducesResponseType(typeof(InventoryView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<InventoryView>> Adjust(
            int productId, [FromBody] StockAdjustmentDto dto)
        {
            var row = await _inventoryService.AdjustAsync(
                productId, dto.NewQuantity, dto.Notes, dto.RecordedByEmployeeId);
            return Ok(row);
        }

        [HttpPut("{productId:int}/reorder-level")]
        [ProducesResponseType(typeof(InventoryView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<InventoryView>> SetReorderLevel(
            int productId, [FromBody] ReorderLevelDto dto)
        {
            var row = await _inventoryService.SetReorderLevelAsync(productId, dto.ReorderLevel);
            return Ok(row);
        }
    }
}
