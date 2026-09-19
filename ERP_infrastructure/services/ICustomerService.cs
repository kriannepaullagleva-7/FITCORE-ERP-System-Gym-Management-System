using ERP_domain.entities;

namespace ERP_infrastructure.services
{
    public interface ICustomerService
    {
        Task<List<Customer>> GetAllCustomersAsync();
        Task<Customer?> GetCustomerByIdAsync(int id);
        Task<Customer> CreateCustomerAsync(Customer customer);
        Task<bool> DeleteCustomerAsync(int id);
    }
}
