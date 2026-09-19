using ERP_api.DTOs;
using ERP_api.Infrastructure;
using ERP_domain.entities;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP_api.Controllers
{
    /// <summary>
    /// User Access: who can sign in to this company, with which role, and which modules they
    /// may use.
    ///
    /// Every action takes the company from the caller's own token, never from the URL or the
    /// body. That single decision is what makes this screen tenant-safe - there is no id a
    /// caller could change to reach another company's users, because no such id is accepted.
    /// </summary>
    [ApiController]
    [Route("api/users")]
    [Produces("application/json")]
    [Authorize]
    [RequireModule(ErpModules.UserAccess)]
    public class UsersController : ControllerBase
    {
        private readonly IUserAccountService _users;
        private readonly IEmployeeService _employees;

        public UsersController(IUserAccountService users, IEmployeeService employees)
        {
            _users = users;
            _employees = employees;
        }

        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<UserAccountView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<UserAccountView>>> GetAll(
            CancellationToken cancellationToken)
        {
            var users = (await _users.GetUsersAsync(User.GetCompanyId(), cancellationToken)).ToList();

            await AttachEmployeeNamesAsync(users, cancellationToken);
            return Ok(users);
        }

        // Declared before the {id} route so the literal wins the match.
        [HttpGet("roles")]
        [ProducesResponseType(typeof(IEnumerable<RoleView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<RoleView>>> GetRoles(
            CancellationToken cancellationToken)
        {
            return Ok(await _users.GetRolesAsync(cancellationToken));
        }

        /// <summary>
        /// The module catalogue as this company sees it, so the editor can show which modules
        /// exist but are not part of the company plan.
        /// </summary>
        [HttpGet("modules")]
        [ProducesResponseType(typeof(IEnumerable<object>), StatusCodes.Status200OK)]
        public ActionResult<IEnumerable<object>> GetModules()
        {
            var tierName = User.FindFirst(FitCoreClaims.EnterpriseTier)?.Value;

            var tier = Enum.TryParse<EnterpriseTier>(tierName, ignoreCase: true, out var parsed)
                ? parsed
                : EnterpriseTier.Micro;

            return Ok(ErpModules.All.Select(m => new
            {
                module = m.Key,
                displayName = m.DisplayName,
                group = m.Group,
                availableInTier = m.MinimumTier <= tier,
                minimumTier = m.MinimumTier.ToString()
            }));
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(UserAccountView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<UserAccountView>> GetById(
            int id, CancellationToken cancellationToken)
        {
            var user = await _users.GetUserAsync(User.GetCompanyId(), id, cancellationToken);

            if (user is null) return NotFound();

            await AttachEmployeeNamesAsync(new[] { user }, cancellationToken);
            return Ok(user);
        }

        /// <summary>
        /// The permission editor for one user: role default, personal override and effective
        /// value for every module.
        /// </summary>
        [HttpGet("{id:int}/permissions")]
        [ProducesResponseType(typeof(UserPermissionEditorView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<UserPermissionEditorView>> GetPermissions(
            int id, CancellationToken cancellationToken)
        {
            var editor = await _users.GetPermissionEditorAsync(
                User.GetCompanyId(), id, cancellationToken);

            return editor is null ? NotFound() : Ok(editor);
        }

        [HttpPut("{id:int}/permissions")]
        [ProducesResponseType(typeof(UserPermissionEditorView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<UserPermissionEditorView>> SetPermissions(
            int id,
            [FromBody] UpdateModuleAccessRequestDto request,
            CancellationToken cancellationToken)
        {
            var editor = await _users.SetModuleAccessAsync(
                User.GetCompanyId(),
                id,
                request.Modules,
                User.GetRoleLevel(),
                User.GetUsername(),
                cancellationToken);

            return editor is null ? NotFound() : Ok(editor);
        }

        [HttpPost]
        [ProducesResponseType(typeof(UserAccountView), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<UserAccountView>> Create(
            [FromBody] CreateUserRequestDto request, CancellationToken cancellationToken)
        {
            var user = await _users.CreateUserAsync(
                User.GetCompanyId(),
                request.Username,
                request.FullName,
                request.Email,
                request.Password,
                request.RoleKey,
                request.EmployeeId,
                User.GetRoleLevel(),
                cancellationToken);

            return CreatedAtAction(nameof(GetById), new { id = user.AppUserId }, user);
        }

        [HttpPut("{id:int}")]
        [ProducesResponseType(typeof(UserAccountView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<UserAccountView>> Update(
            int id, [FromBody] UpdateUserRequestDto request, CancellationToken cancellationToken)
        {
            var user = await _users.UpdateUserAsync(
                User.GetCompanyId(),
                id,
                request.FullName,
                request.Email,
                request.EmployeeId,
                User.GetRoleLevel(),
                cancellationToken);

            return user is null ? NotFound() : Ok(user);
        }

        [HttpPatch("{id:int}/role")]
        [ProducesResponseType(typeof(UserAccountView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<UserAccountView>> SetRole(
            int id, [FromBody] UpdateUserRoleRequestDto request, CancellationToken cancellationToken)
        {
            var user = await _users.SetRoleAsync(
                User.GetCompanyId(),
                id,
                request.RoleKey,
                User.GetRoleLevel(),
                User.GetAppUserId(),
                cancellationToken);

            return user is null ? NotFound() : Ok(user);
        }

        [HttpPatch("{id:int}/status")]
        [ProducesResponseType(typeof(UserAccountView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<UserAccountView>> SetStatus(
            int id, [FromBody] UpdateUserStatusRequestDto request, CancellationToken cancellationToken)
        {
            var user = await _users.SetActiveAsync(
                User.GetCompanyId(),
                id,
                request.IsActive,
                User.GetRoleLevel(),
                User.GetAppUserId(),
                cancellationToken);

            return user is null ? NotFound() : Ok(user);
        }

        [HttpPost("{id:int}/reset-password")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ResetPassword(
            int id, [FromBody] ResetPasswordRequestDto request, CancellationToken cancellationToken)
        {
            var reset = await _users.ResetPasswordAsync(
                User.GetCompanyId(), id, request.NewPassword, User.GetRoleLevel(), cancellationToken);

            return reset ? NoContent() : NotFound();
        }

        /// <summary>
        /// Fills in the employee name for accounts linked to one.
        ///
        /// The names live in the tenant database while the accounts live in the master
        /// database, so they cannot be joined in SQL. They are looked up in one pass here
        /// rather than one query per row.
        /// </summary>
        private async Task AttachEmployeeNamesAsync(
            IReadOnlyCollection<UserAccountView> users, CancellationToken cancellationToken)
        {
            if (!users.Any(u => u.EmployeeId.HasValue)) return;

            try
            {
                var employees = (await _employees.GetAllEmployeesAsync())
                    .ToDictionary(e => e.EmployeeId);

                foreach (var user in users)
                {
                    if (user.EmployeeId is int id && employees.TryGetValue(id, out var employee))
                    {
                        user.EmployeeName = $"{employee.FirstName} {employee.LastName}".Trim();
                    }
                }
            }
            catch (Exception)
            {
                // A tenant database that cannot be reached must not take the User Access screen
                // down with it; the accounts themselves are in the master database and are fine.
                // The rows simply show no linked employee.
            }
        }
    }
}
