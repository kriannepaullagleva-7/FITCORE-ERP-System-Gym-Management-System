using System.ComponentModel.DataAnnotations;

namespace ERP_api.DTOs
{
    public class CustomerDto
    {
        public int CustomerId { get; set; }
        public string CustomerCode { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string? ContactNumber { get; set; }
        public string? EmailAddress { get; set; }
        public string? Address { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateCustomerDto
    {
        [Required(ErrorMessage = "A customer code is required.")]
        [StringLength(50)]
        public string CustomerCode { get; set; } = "";

        [Required(ErrorMessage = "A customer name is required.")]
        [StringLength(200)]
        public string CustomerName { get; set; } = "";

        [StringLength(30)]
        public string? ContactNumber { get; set; }

        [StringLength(100)]
        [OptionalEmailAddress]
        public string? EmailAddress { get; set; }

        [StringLength(300)]
        public string? Address { get; set; }
    }

    /// <summary>
    /// Carries IsActive so a customer can be retired without being deleted, which is what an
    /// operator normally wants for master data that history may still reference.
    /// </summary>
    public class UpdateCustomerDto : CreateCustomerDto
    {
        public bool IsActive { get; set; } = true;
    }

    public class SupplierDto
    {
        public int SupplierId { get; set; }
        public string SupplierCode { get; set; } = "";
        public string SupplierName { get; set; } = "";
        public string? ContactPerson { get; set; }
        public string? ContactNumber { get; set; }
        public string? EmailAddress { get; set; }
        public string? Address { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateSupplierDto
    {
        [Required(ErrorMessage = "A supplier code is required.")]
        [StringLength(50)]
        public string SupplierCode { get; set; } = "";

        [Required(ErrorMessage = "A supplier name is required.")]
        [StringLength(200)]
        public string SupplierName { get; set; } = "";

        [StringLength(100)]
        public string? ContactPerson { get; set; }

        [StringLength(30)]
        public string? ContactNumber { get; set; }

        [StringLength(100)]
        [OptionalEmailAddress]
        public string? EmailAddress { get; set; }

        [StringLength(300)]
        public string? Address { get; set; }
    }

    /// <summary>
    /// Carries IsActive so a supplier can be retired without being deleted.
    /// </summary>
    public class UpdateSupplierDto : CreateSupplierDto
    {
        public bool IsActive { get; set; } = true;
    }
}
