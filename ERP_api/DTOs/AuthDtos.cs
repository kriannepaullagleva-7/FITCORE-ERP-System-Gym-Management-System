using System.ComponentModel.DataAnnotations;
using ERP_infrastructure.services;

namespace ERP_api.DTOs
{
    public class LoginRequestDto
    {
        [Required(ErrorMessage = "Enter your username or email address.")]
        [StringLength(200)]
        public string Username { get; set; } = "";

        [Required(ErrorMessage = "Enter your password.")]
        [StringLength(200, MinimumLength = 1)]
        public string Password { get; set; } = "";
    }

    /// <summary>
    /// What a successful sign-in returns. Carries no password, no connection string and no
    /// information about any company other than the one the user belongs to.
    /// </summary>
    public class LoginResponseDto
    {
        public string Token { get; set; } = "";
        public DateTime ExpiresAtUtc { get; set; }
        public CurrentUserDto User { get; set; } = new();
    }

    public class CurrentUserDto
    {
        public int AppUserId { get; set; }
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";

        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = "";
        public string EnterpriseTier { get; set; } = "";

        public string RoleKey { get; set; } = "";
        public string RoleDisplayName { get; set; } = "";
        public int RoleLevel { get; set; }

        public int? EmployeeId { get; set; }
        public bool MustChangePassword { get; set; }

        /// <summary>The modules this user may use. Drives the sidebar and the dashboard.</summary>
        public List<string> Modules { get; set; } = new();

        /// <summary>
        /// The subfeatures underneath those modules this user may open. Drives the tab strip
        /// inside a module workspace. Derived on the server from the modules, the company tier
        /// and the role level, so the desktop never has to know the tier rules.
        /// </summary>
        public List<string> Submodules { get; set; } = new();

        /// <summary>
        /// The branch this account is bound to, or null for somebody who works across the whole
        /// company. Set for a branch Manager or Staff; never set for an Admin/Owner.
        /// </summary>
        public int? BranchId { get; set; }

        /// <summary>
        /// True when this person may create branches and switch between them. The desktop draws
        /// its branch picker on this; the server refuses the header regardless if it is false.
        /// </summary>
        public bool CanManageBranches { get; set; }

        /// <summary>True for the Super Admin, whose workspace is the platform rather than a gym.</summary>
        public bool IsPlatformAdministrator { get; set; }
    }

    public class ChangePasswordRequestDto
    {
        [Required(ErrorMessage = "Enter your current password.")]
        public string CurrentPassword { get; set; } = "";

        [Required(ErrorMessage = "Enter a new password.")]
        [StringLength(200, MinimumLength = 8,
            ErrorMessage = "A new password must be at least 8 characters long.")]
        public string NewPassword { get; set; } = "";
    }

    public static class AuthMappings
    {
        public static CurrentUserDto ToDto(this AuthenticatedUser user) => new()
        {
            AppUserId = user.AppUserId,
            Username = user.Username,
            FullName = user.FullName,
            Email = user.Email,
            CompanyId = user.CompanyId,
            CompanyName = user.CompanyName,
            EnterpriseTier = user.EnterpriseTierName,
            RoleKey = user.RoleKey,
            RoleDisplayName = user.RoleDisplayName,
            RoleLevel = user.RoleLevel,
            EmployeeId = user.EmployeeId,
            MustChangePassword = user.MustChangePassword,
            Modules = user.Modules,

            // Sent alongside the modules so the desktop can build its tab strips without
            // holding a copy of the tier rules. The server re-derives this per request
            // regardless, so it is navigation guidance and never the boundary.
            Submodules = user.Submodules,
            IsPlatformAdministrator = user.IsPlatformAdministrator,

            BranchId = user.BranchId,
            CanManageBranches = user.CanManageBranches
        };
    }
}
