using ERP_api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using ERP_api.DTOs;
using ERP_domain.entities;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Mvc;

namespace ERP_api.Controllers
{
    /// <summary>
    /// Supplier master data. Replaces the old tenant/{companyId}/suppliers route: the company
    /// is now resolved by the server, so it is no longer selectable from the URL.
    /// </summary>
    [ApiController]
    [Route("api/suppliers")]
    [Produces("application/json")]
    [Authorize]
    [RequireModule(ErpModules.Inventory)]
    public class SuppliersController : ControllerBase
    {
        private readonly ISupplierService _supplierService;

        public SuppliersController(ISupplierService supplierService)
        {
            _supplierService = supplierService;
        }

        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<SupplierDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<SupplierDto>>> GetAll()
        {
            var suppliers = await _supplierService.GetAllSuppliersAsync();
            return Ok(suppliers.Select(s => s.ToDto()));
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(SupplierDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<SupplierDto>> GetById(int id)
        {
            var supplier = await _supplierService.GetSupplierByIdAsync(id);
            return supplier is null ? NotFound() : Ok(supplier.ToDto());
        }

        [HttpPost]
        [ProducesResponseType(typeof(SupplierDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<SupplierDto>> Create([FromBody] CreateSupplierDto dto)
        {
            var created = await _supplierService.CreateSupplierAsync(new Supplier
            {
                SupplierCode = dto.SupplierCode,
                SupplierName = dto.SupplierName,
                ContactPerson = dto.ContactPerson,
                ContactNumber = dto.ContactNumber,
                EmailAddress = dto.EmailAddress,
                Address = dto.Address
            });

            return CreatedAtAction(
                nameof(GetById), new { id = created.SupplierId }, created.ToDto());
        }

        [HttpDelete("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(int id)
        {
            var deleted = await _supplierService.DeleteSupplierAsync(id);
            return deleted ? NoContent() : NotFound();
        }
    }
}
