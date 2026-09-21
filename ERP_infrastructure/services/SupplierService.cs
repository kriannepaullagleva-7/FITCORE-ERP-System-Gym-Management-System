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
            Validate(supplier.SupplierCode, supplier.SupplierName);
            await EnsureCodeIsFree(supplier.SupplierCode, exceptId: null);

            supplier.SupplierCode = supplier.SupplierCode.Trim();
            supplier.SupplierName = supplier.SupplierName.Trim();

            return await _repository.AddAsync(supplier);
        }

        public async Task<Supplier?> UpdateSupplierAsync(
            int id,
            string supplierCode,
            string supplierName,
            string? contactPerson,
            string? contactNumber,
            string? emailAddress,
            string? address,
            bool isActive)
        {
            var supplier = await _repository.GetByIdAsync(id);
            if (supplier is null) return null;

            Validate(supplierCode, supplierName);
            await EnsureCodeIsFree(supplierCode, exceptId: id);

            supplier.SupplierCode = supplierCode.Trim();
            supplier.SupplierName = supplierName.Trim();
            supplier.ContactPerson = contactPerson;
            supplier.ContactNumber = contactNumber;
            supplier.EmailAddress = emailAddress;
            supplier.Address = address;
            supplier.IsActive = isActive;

            return await _repository.UpdateAsync(supplier);
        }

        public Task<bool> DeleteSupplierAsync(int id) => _repository.DeleteAsync(id);

        private static void Validate(string code, string name)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ValidationException("A supplier code is required.");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ValidationException("A supplier name is required.");
            }
        }

        /// <summary>
        /// SupplierCode carries a unique index. Checking here turns what would otherwise be a
        /// provider error and a 500 into a 400 that names the problem.
        /// </summary>
        private async Task EnsureCodeIsFree(string code, int? exceptId)
        {
            var trimmed = code.Trim();

            var clash = (await _repository.GetAllAsync()).Any(s =>
                s.SupplierId != exceptId &&
                string.Equals(s.SupplierCode, trimmed, StringComparison.OrdinalIgnoreCase));

            if (clash)
            {
                throw new ValidationException($"Supplier code '{trimmed}' is already in use.");
            }
        }
    }
}
