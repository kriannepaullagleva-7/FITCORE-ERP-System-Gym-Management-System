using ERP_domain.entities;

namespace ERP_infrastructure.services
{
    public interface ISupplierService
    {
        Task<List<Supplier>> GetAllSuppliersAsync();
        Task<Supplier?> GetSupplierByIdAsync(int id);
        Task<Supplier> CreateSupplierAsync(Supplier supplier);

        /// <summary>
        /// Returns null when no supplier has that id, so the controller can answer 404 rather
        /// than inventing one.
        /// </summary>
        Task<Supplier?> UpdateSupplierAsync(
            int id,
            string supplierCode,
            string supplierName,
            string? contactPerson,
            string? contactNumber,
            string? emailAddress,
            string? address,
            bool isActive);

        Task<bool> DeleteSupplierAsync(int id);
    }
}
