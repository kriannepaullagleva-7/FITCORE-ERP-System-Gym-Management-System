using ERP_domain.entities;
using ERP_infrastructure.repositories;

namespace ERP_infrastructure.services
{
    /// <summary>
    /// Customers are a thin master-data list with no business rules of their own beyond a
    /// required code and name, so this service wraps the generic repository rather than
    /// introducing a bespoke one.
    /// </summary>
    public class CustomerService : ICustomerService
    {
        private readonly IGenericRepository<Customer> _repository;

        public CustomerService(IGenericRepository<Customer> repository)
        {
            _repository = repository;
        }

        public Task<List<Customer>> GetAllCustomersAsync() => _repository.GetAllAsync();

        public Task<Customer?> GetCustomerByIdAsync(int id) => _repository.GetByIdAsync(id);

        public async Task<Customer> CreateCustomerAsync(Customer customer)
        {
            Validate(customer.CustomerCode, customer.CustomerName);
            await EnsureCodeIsFree(customer.CustomerCode, exceptId: null);

            customer.CustomerCode = customer.CustomerCode.Trim();
            customer.CustomerName = customer.CustomerName.Trim();

            return await _repository.AddAsync(customer);
        }

        public async Task<Customer?> UpdateCustomerAsync(
            int id,
            string customerCode,
            string customerName,
            string? contactNumber,
            string? emailAddress,
            string? address,
            bool isActive)
        {
            var customer = await _repository.GetByIdAsync(id);
            if (customer is null) return null;

            Validate(customerCode, customerName);
            await EnsureCodeIsFree(customerCode, exceptId: id);

            customer.CustomerCode = customerCode.Trim();
            customer.CustomerName = customerName.Trim();
            customer.ContactNumber = contactNumber;
            customer.EmailAddress = emailAddress;
            customer.Address = address;
            customer.IsActive = isActive;

            return await _repository.UpdateAsync(customer);
        }

        public Task<bool> DeleteCustomerAsync(int id) => _repository.DeleteAsync(id);

        private static void Validate(string code, string name)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ValidationException("A customer code is required.");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ValidationException("A customer name is required.");
            }
        }

        /// <summary>
        /// CustomerCode carries a unique index. Checking here turns what would otherwise be a
        /// provider error and a 500 into a 400 that names the problem.
        /// </summary>
        private async Task EnsureCodeIsFree(string code, int? exceptId)
        {
            var trimmed = code.Trim();

            var clash = (await _repository.GetAllAsync()).Any(c =>
                c.CustomerId != exceptId &&
                string.Equals(c.CustomerCode, trimmed, StringComparison.OrdinalIgnoreCase));

            if (clash)
            {
                throw new ValidationException($"Customer code '{trimmed}' is already in use.");
            }
        }
    }
}
