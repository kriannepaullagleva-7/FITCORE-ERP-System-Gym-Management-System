using System;
using System.Collections.Generic;

namespace ERP_domain.entities
{
    public class Company
    {
        public int CompanyId { get; set; }
        public string CompanyCode { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Which enterprise tier this company is licensed for, and therefore which modules its
        /// users can reach. Micro is the existing behaviour, so it is the default for every
        /// company that predates this column.
        /// </summary>
        public EnterpriseTier EnterpriseTier { get; set; } = EnterpriseTier.Micro;

        public ICollection<Device> Devices { get; set; } = new List<Device>();
        public ICollection<AppUser> Users { get; set; } = new List<AppUser>();

        /// <summary>
        /// This company's subscription history. Platform data: a tenant never reads it, and it
        /// is what the Super Admin's revenue and churn figures are computed from.
        /// </summary>
        public ICollection<CompanySubscription> Subscriptions { get; set; } = new List<CompanySubscription>();
    }
}
