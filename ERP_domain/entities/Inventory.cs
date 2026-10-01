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

        /// <summary>
        /// Weighted average cost of the units currently on hand.
        ///
        /// Recalculated on every receipt as
        /// <c>(existing value + received value) / (existing quantity + received quantity)</c>,
        /// and left alone by an issue - selling something does not change what the remainder
        /// cost. This is what inventory is valued at and what a sale charges to cost of goods
        /// sold, so the gross profit on a line reflects what the gym actually paid for that
        /// stock rather than what the product's catalogue cost happens to say today.
        /// </summary>
        public decimal AverageCost { get; set; }

        /// <summary>The unit cost of the most recent receipt, kept for comparison.</summary>
        public decimal LastUnitCost { get; set; }

        public Product? Product { get; set; }

        /// <summary>What the stock on hand is worth at weighted average cost.</summary>
        public decimal StockValue => QuantityOnHand * AverageCost;
    }
}
