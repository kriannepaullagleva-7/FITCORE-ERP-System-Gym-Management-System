using ERP_domain.entities;
using ERP_api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using ERP_api.DTOs;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Mvc;

namespace ERP_api.Controllers
{
    /// <summary>
    /// Sellable products. Stock levels for these live in the Inventory module.
    /// </summary>
    [ApiController]
    [Route("api/products")]
    [Produces("application/json")]
    [Authorize]
    [RequireModule(ErpModules.Inventory)]
    public class ProductsController : ControllerBase
    {
        private readonly IProductService _productService;

        public ProductsController(IProductService productService)
        {
            _productService = productService;
        }

        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<ProductDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<ProductDto>>> GetAll()
        {
            var products = await _productService.GetAllProductsAsync();
            return Ok(products.Select(p => p.ToDto()));
        }

        // Declared before the {id} route so the literal wins the match.
        [HttpGet("active")]
        [ProducesResponseType(typeof(IEnumerable<ProductDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<ProductDto>>> GetActive()
        {
            var products = await _productService.GetActiveProductsAsync();
            return Ok(products.Select(p => p.ToDto()));
        }

        /// <summary>The categories in use, for the product form and the inventory filter.</summary>
        [HttpGet("categories")]
        [ProducesResponseType(typeof(IEnumerable<string>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<string>>> GetCategories()
        {
            return Ok(await _productService.GetCategoriesAsync());
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ProductDto>> GetById(int id)
        {
            var product = await _productService.GetProductByIdAsync(id);
            return product is null ? NotFound() : Ok(product.ToDto());
        }

        [HttpPost]
        [ProducesResponseType(typeof(ProductDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<ProductDto>> Create([FromBody] CreateProductDto dto)
        {
            var product = await _productService.CreateProductAsync(
                dto.ProductCode, dto.ProductName, dto.Category, dto.CostPrice, dto.UnitPrice,
                dto.OpeningStock, dto.ReorderLevel);

            return CreatedAtAction(
                nameof(GetById), new { id = product.ProductId }, product.ToDto());
        }

        [HttpPut("{id:int}")]
        [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ProductDto>> Update(
            int id, [FromBody] UpdateProductDto dto)
        {
            var product = await _productService.UpdateProductAsync(
                id, dto.ProductCode, dto.ProductName, dto.Category, dto.CostPrice, dto.UnitPrice,
                dto.IsActive);

            return product is null ? NotFound() : Ok(product.ToDto());
        }

        [HttpDelete("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(int id)
        {
            var deleted = await _productService.DeleteProductAsync(id);
            return deleted ? NoContent() : NotFound();
        }
    }
}
