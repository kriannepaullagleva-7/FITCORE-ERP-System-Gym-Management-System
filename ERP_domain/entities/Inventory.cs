using System;
using System.Collections.Generic;
using System.Text;

namespace ERP_domain.entities
{
    public class Inventory
    {
        public int InventoryId { get; set; }
        public int ProductId { get; set; }
        public decimal QuantityOnHand { get; set; }
        public decimal ReorderLevel { get; set; }
        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
        public Product? Product { get; set; }
    }
}
