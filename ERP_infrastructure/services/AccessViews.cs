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
        /// The branch this account is bound to, or null for somebody who works across the whole
        /// company. Null is also what every account on a Micro or Small tenant carries, because
        /// branching is a Medium feature and those tenants have no branches to be bound to.
        /// </summary>
        public int? BranchId { get; set; }

        /// <summary>
        /// True when this person may create branches and switch between them - the Admin/Owner
        /// of a Medium tenant, and nobody else. A Manager or Staff account is bound to its own
        /// branch and a branch is not theirs to choose, so the desktop draws no picker for them
        /// and the server would refuse the header if it did.
        /// </summary>
        public bool CanManageBranches =>
            EnterpriseTier >= ERP_domain.entities.EnterpriseTier.Medium
            && RoleLevel <= 1
            && BranchId is null;

        /// <summary>
        /// The modules this person may actually use: their role's grants, adjusted by their own
        /// overrides, then narrowed to what the company's tier includes.
        /// </summary>
        public List<string> Modules { get; set; } = new();

        /// <summary>
        /// The subfeatures underneath those modules that this person may actually open.
        ///
        /// Derived from <see cref="Modules"/>, <see cref="EnterpriseTier"/> and
        /// <see cref="RoleLevel"/> rather than stored, so it cannot disagree with them. The
        /// desktop builds its tab strips from this; the server re-derives it per request.
        /// </summary>
        public List<string> Submodules { get; set; } = new();

        /// <summary>
        /// True for the platform account. A Super Admin administers FitCore itself rather than
        /// working in a gym, which is why the platform subfeatures are theirs alone.
        /// </summary>
        public bool IsPlatformAdministrator => RoleLevel == 0;
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
    /// One subfeature as the permission editor shows it: which module it belongs underneath,
    /// and the three independent reasons it is or is not available.
    ///
    /// A submodule is never granted directly, so there is no override column here. What the
    /// editor offers instead is an explanation - "this is a Medium feature", "this is Manager
    /// and above", "the user does not hold Finance" - so an administrator can see why a screen
    /// is missing rather than guessing at it.
    /// </summary>
    public class SubmodulePermissionView
    {
        public string Submodule { get; set; } = "";
        public string Module { get; set; } = "";
        public string ModuleDisplayName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Description { get; set; } = "";
        public string MinimumTierName { get; set; } = "";

        public bool AvailableInTier { get; set; }
        public bool AllowedForRole { get; set; }
        public bool HoldsParentModule { get; set; }
        public bool Effective { get; set; }
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
        public int RoleLevel { get; set; }
        public List<ModulePermissionView> Modules { get; set; } = new();
        public List<SubmodulePermissionView> Submodules { get; set; } = new();
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
