using System.ComponentModel.DataAnnotations;

namespace ERP_api.DTOs
{
    public class CreateUserRequestDto
    {
        [Required(ErrorMessage = "A username is required.")]
        [StringLength(100, MinimumLength = 3,
            ErrorMessage = "A username must be between 3 and 100 characters.")]
        public string Username { get; set; } = "";

        [Required(ErrorMessage = "A full name is required.")]
        [StringLength(200)]
        public string FullName { get; set; } = "";

        [OptionalEmailAddress]
        [StringLength(200)]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "A password is required.")]
        [StringLength(200, MinimumLength = 8,
            ErrorMessage = "A password must be at least 8 characters long.")]
        public string Password { get; set; } = "";

        [Required(ErrorMessage = "A role is required.")]
        public string RoleKey { get; set; } = "";

        /// <summary>Optional link to an Employee record in this company's tenant database.</summary>
        public int? EmployeeId { get; set; }
    }

    public class UpdateUserRequestDto
    {
        [Required(ErrorMessage = "A full name is required.")]
        [StringLength(200)]
        public string FullName { get; set; } = "";

        [OptionalEmailAddress]
        [StringLength(200)]
        public string Email { get; set; } = "";

        public int? EmployeeId { get; set; }
    }

    /// <summary>
    /// The complete module set this user should end up with. Sending the whole set rather than
    /// a delta means the editor and the server can never disagree about what was unticked.
    /// </summary>
    public class UpdateModuleAccessRequestDto
    {
        public List<string> Modules { get; set; } = new();
    }

    public class UpdateUserRoleRequestDto
    {
        [Required]
        public string RoleKey { get; set; } = "";
    }

    public class UpdateUserStatusRequestDto
    {
        public bool IsActive { get; set; }
    }

    public class ResetPasswordRequestDto
    {
        [Required(ErrorMessage = "A new password is required.")]
        [StringLength(200, MinimumLength = 8,
            ErrorMessage = "A password must be at least 8 characters long.")]
        public string NewPassword { get; set; } = "";
    }
}
