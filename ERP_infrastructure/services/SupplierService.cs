using ERP_domain.entities;
using ERP_infrastructure.data;
using ERP_infrastructure.repositories;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    /// <summary>
    /// Suppliers are master data with no rules beyond a required code and name, so this wraps
    /// the generic repository in the same simple wrapper style.
    /// </summary>
    public class SupplierService : ISupplierService
    {
        private readonly IGenericRepository<Supplier> _repository;
        private readonly TenantErpDbContext _context;

        public SupplierService(IGenericRepository<Supplier> repository, TenantErpDbContext context)
        {
            _repository = repository;
            _context = context;
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

        /// <summary>
        /// Removes a supplier the gym has never actually used.
        ///
        /// Everything that references a supplier - stock movements, purchases, supplier payments
        /// and expenses - is restricted by its own foreign key, so the database already refuses
        /// this. But it refuses as a provider error, which the API maps to 500 and the desktop
        /// shows as "server problem": true, useless, and indistinguishable from an outage.
        ///
        /// Checking first turns that into a sentence naming what is in the way, which is how
        /// MemberService and ProductService already behave. The foreign keys stay as the
        /// backstop for any write that goes around this service.
        /// </summary>
        public async Task<bool> DeleteSupplierAsync(int id)
        {
            var supplier = await _repository.GetByIdAsync(id);
            if (supplier is null) return false;

            var blockers = new List<string>();

            if (await _context.StockMovements.AnyAsync(m => m.SupplierId == id))
                blockers.Add("stock movements");

            if (await _context.Purchases.AnyAsync(p => p.SupplierId == id))
                blockers.Add("purchase orders");

            if (await _context.SupplierPayments.AnyAsync(p => p.SupplierId == id))
                blockers.Add("payments");

            if (await _context.Expenses.AnyAsync(e => e.SupplierId == id))
                blockers.Add("expenses");

            if (blockers.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{supplier.SupplierName} has {Join(blockers)} on record and cannot be " +
                    "deleted, because that history is part of the purchase and inventory " +
                    "record. Mark the supplier inactive instead.");
            }

            return await _repository.DeleteAsync(id);
        }

        /// <summary>Renders "a, b and c", so the refusal reads as a sentence rather than a list.</summary>
        private static string Join(List<string> parts) =>
            parts.Count == 1
                ? parts[0]
                : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];

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
