using ERP_api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using ERP_api.DTOs;
using ERP_domain.entities;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Mvc;

namespace ERP_api.Controllers
{
    /// <summary>
    /// Customer master data. Replaces the old tenant/{companyId}/customers route: the company
    /// is now resolved by the server, so it is no longer selectable from the URL.
    /// </summary>
    [ApiController]
    [Route("api/customers")]
    [Produces("application/json")]
    [Authorize]
    [RequireModule(ErpModules.Sales)]
    public class CustomersController : ControllerBase
    {
        private readonly ICustomerService _customerService;

        public CustomersController(ICustomerService customerService)
        {
            _customerService = customerService;
        }

        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<CustomerDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<CustomerDto>>> GetAll()
        {
            var customers = await _customerService.GetAllCustomersAsync();
            return Ok(customers.Select(c => c.ToDto()));
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(CustomerDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<CustomerDto>> GetById(int id)
        {
            var customer = await _customerService.GetCustomerByIdAsync(id);
            return customer is null ? NotFound() : Ok(customer.ToDto());
        }

        [HttpPost]
        [ProducesResponseType(typeof(CustomerDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<CustomerDto>> Create([FromBody] CreateCustomerDto dto)
        {
            var created = await _customerService.CreateCustomerAsync(new Customer
            {
                CustomerCode = dto.CustomerCode,
                CustomerName = dto.CustomerName,
                ContactNumber = dto.ContactNumber,
                EmailAddress = dto.EmailAddress,
                Address = dto.Address
            });

            return CreatedAtAction(
                nameof(GetById), new { id = created.CustomerId }, created.ToDto());
        }

        [HttpDelete("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(int id)
        {
            var deleted = await _customerService.DeleteCustomerAsync(id);
            return deleted ? NoContent() : NotFound();
        }
    }
}
