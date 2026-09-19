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
            if (string.IsNullOrWhiteSpace(customer.CustomerCode))
            {
                throw new ValidationException("A customer code is required.");
            }

            if (string.IsNullOrWhiteSpace(customer.CustomerName))
            {
                throw new ValidationException("A customer name is required.");
            }

            return await _repository.AddAsync(customer);
        }

        public Task<bool> DeleteCustomerAsync(int id) => _repository.DeleteAsync(id);
    }
}
