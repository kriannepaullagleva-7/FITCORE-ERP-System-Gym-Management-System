using System;
using System.Collections.Generic;
using System.Text;

namespace ERP_infrastructure.services
{
    public interface ITenantDatabaseResolver
    {
        Task<TenantDatabaseInfo> GetDatabaseInfoAsync(int companyId);
    }
}