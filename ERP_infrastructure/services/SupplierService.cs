using ERP_domain.entities;
using ERP_infrastructure.repositories;

namespace ERP_infrastructure.services
{
    /// <summary>
    /// Suppliers are master data with no rules beyond a required code and name, so this wraps
    /// the generic repository in the same way <see cref="CustomerService"/> does.
    /// </summary>
    public class SupplierService : ISupplierService
    {
        private readonly IGenericRepository<Supplier> _repository;

        public SupplierService(IGenericRepository<Supplier> repository)
        {
            _repository = repository;
        }

        public Task<List<Supplier>> GetAllSuppliersAsync() => _repository.GetAllAsync();

        public Task<Supplier?> GetSupplierByIdAsync(int id) => _repository.GetByIdAsync(id);

        public async Task<Supplier> CreateSupplierAsync(Supplier supplier)
        {
            if (string.IsNullOrWhiteSpace(supplier.SupplierCode))
            {
                throw new ValidationException("A supplier code is required.");
            }

            if (string.IsNullOrWhiteSpace(supplier.SupplierName))
            {
                throw new ValidationException("A supplier name is required.");
            }

            return await _repository.AddAsync(supplier);
        }

        public Task<bool> DeleteSupplierAsync(int id) => _repository.DeleteAsync(id);
    }
}
