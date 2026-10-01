using ERP_api.DTOs;
using ERP_api.Infrastructure;
using ERP_domain.entities;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP_api.Controllers
{
    /// <summary>
    /// Returns and refunds: goods coming back over the counter.
    ///
    /// Part of Sales Management. A return is its own transaction rather than an edit to the
    /// sale it came from, so the original receipt, the day's takings and the audit trail all
    /// stay exactly as they were rung up.
    /// </summary>
    [ApiController]
    [Route("api/returns")]
    [Produces("application/json")]
    [Authorize]
    [RequireModule(ErpModules.Sales)]
    [RequireSubmodule(ErpModules.Sub.SalesReturns)]
    public class SaleReturnsController : ControllerBase
    {
        private readonly ISaleReturnService _returns;

        public SaleReturnsController(ISaleReturnService returns)
        {
            _returns = returns;
        }

        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<SaleReturnView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<SaleReturnView>>> GetAll(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int? saleId)
        {
            return Ok(await _returns.GetReturnsAsync(from, to, saleId));
        }

        [HttpGet("reasons")]
        [ProducesResponseType(typeof(IEnumerable<string>), StatusCodes.Status200OK)]
        public ActionResult<IEnumerable<string>> GetReasons() => Ok(ReturnReasons.All);

        /// <summary>
        /// What is still returnable on a sale, line by line. The till asks for this before
        /// offering a return, so the operator is shown what can come back rather than
        /// discovering the limit when the server refuses them.
        /// </summary>
        [HttpGet("returnable/{saleId:int}")]
        [ProducesResponseType(typeof(IEnumerable<ReturnableLineView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<ReturnableLineView>>> GetReturnable(int saleId)
        {
            return Ok(await _returns.GetReturnableLinesAsync(saleId));
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(SaleReturnView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<SaleReturnView>> GetById(int id)
        {
            var saleReturn = await _returns.GetReturnAsync(id);
            return saleReturn is null ? NotFound() : Ok(saleReturn);
        }

        [HttpPost]
        [ProducesResponseType(typeof(SaleReturnView), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<SaleReturnView>> Create([FromBody] CreateSaleReturnDto dto)
        {
            var saleReturn = await _returns.CreateReturnAsync(
                dto.SaleId,
                dto.Lines.Select(l => new ReturnLineRequest
                {
                    SaleItemId = l.SaleItemId,
                    Quantity = l.Quantity
                }),
                dto.Reason, dto.RefundAmount, dto.RefundMethod,
                dto.RestockToInventory, dto.Notes);

            return CreatedAtAction(nameof(GetById), new { id = saleReturn.SaleReturnId }, saleReturn);
        }

        [HttpPost("{id:int}/cancel")]
        [ProducesResponseType(typeof(SaleReturnView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<SaleReturnView>> Cancel(
            int id, [FromBody] CancelSaleReturnDto dto)
        {
            var saleReturn = await _returns.CancelReturnAsync(id, dto.Reason);
            return saleReturn is null ? NotFound() : Ok(saleReturn);
        }
    }
}
