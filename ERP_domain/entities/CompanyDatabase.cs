using System;
using System.Collections.Generic;
using System.Text;

namespace ERP_domain.entities
{
    public class CompanyDatabase
    {
        public int CompanyDatabaseId { get; set; }
        public int CompanyId { get; set; }
        public string ServerName { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;

        public Company? Company { get; set; }
        public string CredentialKey { get; set; } = string.Empty;


    }
}
