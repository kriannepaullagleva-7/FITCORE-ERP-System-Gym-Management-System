using System.ComponentModel.DataAnnotations;

namespace ERP_api.DTOs
{
    public class CreateProductDto
    {
        [Required(ErrorMessage = "A product code is required.")]
        [StringLength(50)]
        public string ProductCode { get; set; } = "";

        [Required(ErrorMessage = "A product name is required.")]
        [StringLength(200)]
        public string ProductName { get; set; } = "";

        [StringLength(50)]
        public string Category { get; set; } = "Other";

        [Range(0, 10000000, ErrorMessage = "Cost price cannot be negative.")]
        public decimal CostPrice { get; set; }

        [Range(0, 10000000, ErrorMessage = "Selling price cannot be negative.")]
        public decimal UnitPrice { get; set; }

        [Range(0, 10000000, ErrorMessage = "Opening stock cannot be negative.")]
        public decimal OpeningStock { get; set; }

        [Range(0, 10000000, ErrorMessage = "Reorder level cannot be negative.")]
        public decimal ReorderLevel { get; set; }
    }

    public class UpdateProductDto
    {
        [Required(ErrorMessage = "A product code is required.")]
        [StringLength(50)]
        public string ProductCode { get; set; } = "";

        [Required(ErrorMessage = "A product name is required.")]
        [StringLength(200)]
        public string ProductName { get; set; } = "";

        [StringLength(50)]
        public string Category { get; set; } = "Other";

        [Range(0, 10000000, ErrorMessage = "Cost price cannot be negative.")]
        public decimal CostPrice { get; set; }

        [Range(0, 10000000, ErrorMessage = "Selling price cannot be negative.")]
        public decimal UnitPrice { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class StockMovementRequestDto
    {
        [Range(0.0001, 10000000, ErrorMessage = "Quantity must be greater than zero.")]
        public decimal Quantity { get; set; }

        [StringLength(60)]
        public string Reference { get; set; } = "";

        [StringLength(300)]
        public string Notes { get; set; } = "";

        /// <summary>Optional: the member of staff recording the movement.</summary>
        public int? RecordedByEmployeeId { get; set; }
    }

    public class StockAdjustmentDto
    {
        [Range(0, 10000000, ErrorMessage = "The new quantity cannot be negative.")]
        public decimal NewQuantity { get; set; }

        [StringLength(300)]
        public string Notes { get; set; } = "";

        public int? RecordedByEmployeeId { get; set; }
    }

    public class ReorderLevelDto
    {
        [Range(0, 10000000, ErrorMessage = "Reorder level cannot be negative.")]
        public decimal ReorderLevel { get; set; }
    }

    public class ProductDto
    {
        public int ProductId { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string Category { get; set; } = "";
        public decimal CostPrice { get; set; }
        public decimal UnitPrice { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>Null until the product has been edited since it was created.</summary>
        public DateTime? UpdatedAt { get; set; }
    }
}
