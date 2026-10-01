using System;
using System.Collections.Generic;

namespace ERP_infrastructure.services
{
    // A product joined to its stock record, with the derived stock status.
    public class InventoryView
    {
        public int InventoryId { get; set; }
        public int ProductId { get; set; }
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public decimal CostPrice { get; set; }
        public decimal UnitPrice { get; set; }
        public bool IsActive { get; set; }
        public decimal QuantityOnHand { get; set; }
        public decimal ReorderLevel { get; set; }

        // "Out of Stock", "Low Stock" or "In Stock".
        public string StockStatus { get; set; } = string.Empty;

        /// <summary>
        /// Weighted average cost of the units on hand. Differs from <see cref="CostPrice"/>,
        /// which is what the catalogue says one costs today, as soon as two deliveries have
        /// arrived at different prices.
        /// </summary>
        public decimal AverageCost { get; set; }

        /// <summary>What the most recent delivery cost per unit.</summary>
        public decimal LastUnitCost { get; set; }

        /// <summary>
        /// What the stock on hand is worth, at weighted average cost.
        ///
        /// Set by the service rather than derived here, because a product that has never been
        /// through a costed receipt has no average and would otherwise be valued at nothing.
        /// The service falls back to the catalogue cost in that case.
        /// </summary>
        public decimal StockValue { get; set; }

        public decimal RetailValue => QuantityOnHand * UnitPrice;

        /// <summary>The gross profit still sitting on the shelf.</summary>
        public decimal PotentialMargin => RetailValue - StockValue;

        public DateTime LastUpdatedAt { get; set; }
    }

    // A single stock in/out entry from the movement ledger.
    public class StockMovementView
    {
        public int StockMovementId { get; set; }
        public int ProductId { get; set; }
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string MovementType { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal BalanceBefore { get; set; }
        public decimal BalanceAfter { get; set; }

        /// <summary>What one unit was worth as it moved, and the value of the whole movement.</summary>
        public decimal UnitCost { get; set; }
        public decimal TotalCost { get; set; }

        public string Reference { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public int? SupplierId { get; set; }
        public string SupplierName { get; set; } = "";
        public int? RecordedByEmployeeId { get; set; }
        public string RecordedByName { get; set; } = "- unassigned -";
        public int? PerformedByUserId { get; set; }
        public string PerformedBy { get; set; } = "";
        public DateTime MovementDate { get; set; }
    }

    // Headline stock figures for the Inventory module.
    public class InventorySummary
    {
        public int TotalProducts { get; set; }
        public int ActiveProducts { get; set; }
        public int InStock { get; set; }
        public int LowStock { get; set; }
        public int OutOfStock { get; set; }
        public decimal TotalUnits { get; set; }
        public decimal StockValue { get; set; }
        public decimal RetailValue { get; set; }
    }
}
