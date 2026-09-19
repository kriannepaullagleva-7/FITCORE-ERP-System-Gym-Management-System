using ERP_domain.entities;

namespace ERP_infrastructure.services
{
    /// <summary>
    /// Everything the server knows about a signed-in person, and the only shape of that
    /// information that ever leaves the server. It deliberately carries no password hash and
    /// no connection detail.
    /// </summary>
    public class AuthenticatedUser
    {
        public int AppUserId { get; set; }
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";

        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = "";
        public string CompanyCode { get; set; } = "";
        public EnterpriseTier EnterpriseTier { get; set; }
        public string EnterpriseTierName => EnterpriseTier.ToString();

        public string RoleKey { get; set; } = "";
        public string RoleDisplayName { get; set; } = "";
        public int RoleLevel { get; set; }

        public int? EmployeeId { get; set; }
        public bool MustChangePassword { get; set; }

        /// <summary>
        /// The modules this person may actually use: their role's grants, adjusted by their own
        /// overrides, then narrowed to what the company's tier includes.
        /// </summary>
        public List<string> Modules { get; set; } = new();
    }

    /// <summary>A row in the User Access table.</summary>
    public class UserAccountView
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

        /// <summary>Resolved from the tenant database when the caller asks for it.</summary>
        public string EmployeeName { get; set; } = "";

        public DateTime? LastLoginAt { get; set; }
        public DateTime CreatedAt { get; set; }

        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = "";

        /// <summary>Effective module access, already tier-filtered.</summary>
        public List<string> Modules { get; set; } = new();
    }

    /// <summary>
    /// One line of the permission editor: what the role gives, what the user's own override
    /// says, and what the two combine to. Sent as a unit so the editor can show an
    /// administrator *why* a box is ticked rather than just that it is.
    /// </summary>
    public class ModulePermissionView
    {
        public string Module { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Group { get; set; } = "";

        /// <summary>False when the company's tier does not include this module at all.</summary>
        public bool AvailableInTier { get; set; }

        public bool GrantedByRole { get; set; }

        /// <summary>Null when this user has no override and simply follows their role.</summary>
        public bool? UserOverride { get; set; }

        public bool Effective { get; set; }
    }

    public class UserPermissionEditorView
    {
        public int AppUserId { get; set; }
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
        public string RoleKey { get; set; } = "";
        public string RoleDisplayName { get; set; } = "";
        public bool IsActive { get; set; }
        public string EnterpriseTierName { get; set; } = "";
        public List<ModulePermissionView> Modules { get; set; } = new();
    }

    public class RoleView
    {
        public int RoleId { get; set; }
        public string RoleKey { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public int HierarchyLevel { get; set; }
        public List<string> DefaultModules { get; set; } = new();
    }

    /// <summary>Why a sign-in attempt failed, so the API can answer with the right status.</summary>
    public enum LoginFailureReason
    {
        None = 0,
        InvalidCredentials = 1,
        AccountDeactivated = 2,
        CompanyInactive = 3
    }

    public class LoginResult
    {
        public bool Succeeded { get; set; }
        public LoginFailureReason Reason { get; set; }
        public AuthenticatedUser? User { get; set; }

        public static LoginResult Fail(LoginFailureReason reason) =>
            new() { Succeeded = false, Reason = reason };

        public static LoginResult Success(AuthenticatedUser user) =>
            new() { Succeeded = true, User = user };
    }
}
