using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    /// <summary>
    /// Master-database operations behind the SaaS administration endpoints. Kept in the
    /// service layer so the controllers do not talk to a DbContext directly.
    /// </summary>
    public class CompanyDirectoryService : ICompanyDirectoryService
    {
        private readonly MasterErpDbContext _masterDb;

        public CompanyDirectoryService(MasterErpDbContext masterDb)
        {
            _masterDb = masterDb;
        }

        public Task<List<Company>> GetCompaniesAsync() =>
            _masterDb.Companies.AsNoTracking().OrderBy(x => x.CompanyId).ToListAsync();

        public Task<Company?> GetCompanyByIdAsync(int companyId) =>
            _masterDb.Companies.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == companyId);

        public async Task<Company> CreateCompanyAsync(Company company)
        {
            if (string.IsNullOrWhiteSpace(company.CompanyCode))
            {
                throw new ValidationException("A company code is required.");
            }

            if (string.IsNullOrWhiteSpace(company.CompanyName))
            {
                throw new ValidationException("A company name is required.");
            }

            var codeTaken = await _masterDb.Companies
                .AnyAsync(x => x.CompanyCode == company.CompanyCode);

            if (codeTaken)
            {
                throw new ValidationException(
                    $"A company with the code '{company.CompanyCode}' already exists.");
            }

            _masterDb.Companies.Add(company);
            await _masterDb.SaveChangesAsync();
            return company;
        }

        public Task<List<CompanyDatabase>> GetCompanyDatabasesAsync(int? companyId = null)
        {
            var query = _masterDb.CompanyDatabases.AsNoTracking().AsQueryable();

            if (companyId.HasValue)
            {
                query = query.Where(x => x.CompanyId == companyId.Value);
            }

            return query.OrderBy(x => x.CompanyDatabaseId).ToListAsync();
        }

        public async Task<CompanyDatabase> CreateCompanyDatabaseAsync(CompanyDatabase companyDatabase)
        {
            if (string.IsNullOrWhiteSpace(companyDatabase.ServerName) ||
                string.IsNullOrWhiteSpace(companyDatabase.DatabaseName))
            {
                throw new ValidationException("A server name and database name are required.");
            }

            if (string.IsNullOrWhiteSpace(companyDatabase.CredentialKey))
            {
                throw new ValidationException(
                    "A credential key is required. It names the TenantCredentials entry that " +
                    "holds the login for this database.");
            }

            var companyExists = await _masterDb.Companies
                .AnyAsync(x => x.CompanyId == companyDatabase.CompanyId);

            if (!companyExists)
            {
                throw new ValidationException(
                    $"No company with id {companyDatabase.CompanyId} exists.");
            }

            _masterDb.CompanyDatabases.Add(companyDatabase);
            await _masterDb.SaveChangesAsync();
            return companyDatabase;
        }

        public Task<List<Device>> GetDevicesAsync(int? companyId = null)
        {
            var query = _masterDb.Devices.AsNoTracking().AsQueryable();

            if (companyId.HasValue)
            {
                query = query.Where(x => x.CompanyId == companyId.Value);
            }

            return query.OrderBy(x => x.DeviceCode).ToListAsync();
        }

        public async Task<Device> CreateDeviceAsync(Device device)
        {
            if (string.IsNullOrWhiteSpace(device.DeviceCode))
            {
                throw new ValidationException("A device code is required.");
            }

            var companyExists = await _masterDb.Companies
                .AnyAsync(x => x.CompanyId == device.CompanyId);

            if (!companyExists)
            {
                throw new ValidationException($"No company with id {device.CompanyId} exists.");
            }

            _masterDb.Devices.Add(device);
            await _masterDb.SaveChangesAsync();
            return device;
        }
    }
}
