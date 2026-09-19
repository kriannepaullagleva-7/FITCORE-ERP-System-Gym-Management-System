using System.ComponentModel.DataAnnotations;

namespace ERP_api.DTOs
{
    // Lengths mirror the column widths configured in TenantErpDbContext, so an over-long value
    // is rejected as a 400 with a clear message instead of failing in the database.
    public class CreateMemberDto
    {
        [Required(ErrorMessage = "A first name is required.")]
        [StringLength(100)]
        public string FirstName { get; set; } = "";

        [Required(ErrorMessage = "A last name is required.")]
        [StringLength(100)]
        public string LastName { get; set; } = "";

        [StringLength(20)]
        public string Phone { get; set; } = "";

        [StringLength(100)]
        [OptionalEmailAddress]
        public string Email { get; set; } = "";
    }

    public class UpdateMemberDto
    {
        [Required(ErrorMessage = "A first name is required.")]
        [StringLength(100)]
        public string FirstName { get; set; } = "";

        [Required(ErrorMessage = "A last name is required.")]
        [StringLength(100)]
        public string LastName { get; set; } = "";

        [StringLength(20)]
        public string Phone { get; set; } = "";

        [StringLength(100)]
        [OptionalEmailAddress]
        public string Email { get; set; } = "";

        [Required]
        [RegularExpression("^(Active|Inactive|Suspended)$",
            ErrorMessage = "Status must be Active, Inactive or Suspended.")]
        public string Status { get; set; } = "Active";
    }

    public class MemberDto
    {
        public int MemberId { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Email { get; set; } = "";
        public DateTime JoinDate { get; set; }
        public string Status { get; set; } = "";
    }
}
