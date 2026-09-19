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

        // Stock is valued at what it cost, falling back to the selling price when no cost has
        // been entered, so the figure is never silently zero.
        public decimal StockValue => QuantityOnHand * (CostPrice > 0 ? CostPrice : UnitPrice);

        public decimal RetailValue => QuantityOnHand * UnitPrice;
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
        public string Reference { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public int? RecordedByEmployeeId { get; set; }
        public string RecordedByName { get; set; } = "- unassigned -";
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
