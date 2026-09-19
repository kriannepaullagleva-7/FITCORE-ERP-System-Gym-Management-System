namespace ERP_UI.DTOs
{
    /// <summary>
    /// Client-side copies of the authentication contracts. As with the other DTOs here, they
    /// are duplicated rather than shared so the Blazor client keeps zero project references.
    /// </summary>
    public class LoginRequestDto
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }

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

        public List<string> Modules { get; set; } = new();

        /// <summary>Initials for the avatar, e.g. "Kris Santos" becomes "KS".</summary>
        public string Initials
        {
            get
            {
                var parts = (FullName ?? "")
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                return parts.Length switch
                {
                    0 => "FC",
                    1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant(),
                    _ => $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant()
                };
            }
        }

        public bool Can(string module) =>
            Modules.Any(m => string.Equals(m, module, StringComparison.OrdinalIgnoreCase));
    }

    public class ChangePasswordRequestDto
    {
        public string CurrentPassword { get; set; } = "";
        public string NewPassword { get; set; } = "";
    }

    // ----------------------------------------------------------------------------------
    // User Access
    // ----------------------------------------------------------------------------------

    public class UserAccountDto
    {
        public int AppUserId { get; set; }
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public string RoleKey { get; set; } = "";
        public string RoleDisplayName { get; set; } = "";
        public int RoleLevel { get; set; }
        public bool IsActive { get; set; }
        public int? EmployeeId { get; set; }
        public string EmployeeName { get; set; } = "";
        public DateTime? LastLoginAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = "";
        public List<string> Modules { get; set; } = new();
    }

    public class ModulePermissionDto
    {
        public string Module { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Group { get; set; } = "";
        public bool AvailableInTier { get; set; }
        public bool GrantedByRole { get; set; }
        public bool? UserOverride { get; set; }
        public bool Effective { get; set; }
    }

    public class UserPermissionEditorDto
    {
        public int AppUserId { get; set; }
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
        public string RoleKey { get; set; } = "";
        public string RoleDisplayName { get; set; } = "";
        public bool IsActive { get; set; }
        public string EnterpriseTierName { get; set; } = "";
        public List<ModulePermissionDto> Modules { get; set; } = new();
    }

    public class RoleDto
    {
        public int RoleId { get; set; }
        public string RoleKey { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public int HierarchyLevel { get; set; }
        public List<string> DefaultModules { get; set; } = new();
    }

    public class CreateUserRequestDto
    {
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
        public string RoleKey { get; set; } = "";
        public int? EmployeeId { get; set; }
    }

    public class UpdateUserRequestDto
    {
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public int? EmployeeId { get; set; }
    }

    public class UpdateModuleAccessRequestDto
    {
        public List<string> Modules { get; set; } = new();
    }

    public class UpdateUserRoleRequestDto
    {
        public string RoleKey { get; set; } = "";
    }

    public class UpdateUserStatusRequestDto
    {
        public bool IsActive { get; set; }
    }

    public class ResetPasswordRequestDto
    {
        public string NewPassword { get; set; } = "";
    }

    /// <summary>
    /// The module keys, mirrored from the server so the sidebar and the pages agree with the
    /// API about what a permission is called.
    /// </summary>
    public static class Modules
    {
        public const string Dashboard  = "dashboard";
        public const string Membership = "membership";
        public const string Sales      = "sales";
        public const string Payments   = "payments";
        public const string Inventory  = "inventory";
        public const string Employees  = "employees";
        public const string Payroll    = "payroll";
        public const string Reports    = "reports";

        // Medium tier. Mirrors ERP_domain.entities.ErpModules; the server is authoritative and
        // answers 403 regardless of what the browser believes.
        public const string Expenses   = "expenses";
        public const string Finance    = "finance";
        public const string BusinessIntelligence = "businessintelligence";
        public const string UserAccess = "useraccess";
        public const string SystemAdmin = "systemadmin";
    }
}
