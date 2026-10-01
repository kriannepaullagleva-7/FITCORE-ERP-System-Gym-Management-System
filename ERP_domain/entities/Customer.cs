using System;
using System.Collections.Generic;
using System.Text;

namespace ERP_domain.entities
{
    public class Customer : IBranchScoped
    {
        public int CustomerId { get; set; }

        /// <summary>The branch this customer was registered at. Null on a single-site tenant.</summary>
        public int? BranchId { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string? ContactNumber { get; set; }
        public string? EmailAddress { get; set; }
        public string? Address { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
