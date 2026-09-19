using System;
using System.Collections.Generic;
using System.Text;

namespace ERP_domain.entities
{
    public class Product : IAuditable
    {
        public int ProductId { get; set; }

        /// <summary>Stock keeping unit, unique within the tenant.</summary>
        public string ProductCode { get; set; } = string.Empty;

        public string ProductName { get; set; } = string.Empty;

        /// <summary>Supplements, Beverages, Apparel, Equipment, Accessories or Other.</summary>
        public string Category { get; set; } = "Other";

        /// <summary>What the gym pays for one unit. Drives the margin shown in reports.</summary>
        public decimal CostPrice { get; set; }

        /// <summary>What the customer pays for one unit.</summary>
        public decimal UnitPrice { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Stamped by the DbContext whenever this record is changed.</summary>
        public DateTime? UpdatedAt { get; set; }
    }
}
