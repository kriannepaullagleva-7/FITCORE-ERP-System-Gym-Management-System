using ERP_domain.entities;

namespace ERP_infrastructure.services
{
    /// <summary>
    /// Read and write access to the master registry: which companies exist, where each one's
    /// database lives, and which devices belong to them. This is SaaS administration, and it is
    /// the only service that touches the master database.
    /// </summary>
    public interface ICompanyDirectoryService
    {
        Task<List<Company>> GetCompaniesAsync();
        Task<Company?> GetCompanyByIdAsync(int companyId);
        Task<Company> CreateCompanyAsync(Company company);

        Task<List<CompanyDatabase>> GetCompanyDatabasesAsync(int? companyId = null);
        Task<CompanyDatabase> CreateCompanyDatabaseAsync(CompanyDatabase companyDatabase);

        Task<List<Device>> GetDevicesAsync(int? companyId = null);
        Task<Device> CreateDeviceAsync(Device device);
    }
}
