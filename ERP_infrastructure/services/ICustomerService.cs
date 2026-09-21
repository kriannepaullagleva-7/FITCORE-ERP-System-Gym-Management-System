using ERP_domain.entities;

namespace ERP_infrastructure.services
{
    public interface ICustomerService
    {
        Task<List<Customer>> GetAllCustomersAsync();
        Task<Customer?> GetCustomerByIdAsync(int id);
        Task<Customer> CreateCustomerAsync(Customer customer);

        /// <summary>
        /// Returns null when no customer has that id, so the controller can answer 404 rather
        /// than inventing one.
        /// </summary>
        Task<Customer?> UpdateCustomerAsync(
            int id,
            string customerCode,
            string customerName,
            string? contactNumber,
            string? emailAddress,
            string? address,
            bool isActive);

        Task<bool> DeleteCustomerAsync(int id);
    }
}
