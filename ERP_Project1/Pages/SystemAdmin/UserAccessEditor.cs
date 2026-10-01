using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// The per-user module access editor, shared by Users and Permissions.
    ///
    /// Both screens need to change one person's access and there must only be one way to do it:
    /// two dialogs would eventually disagree about what a tier ceiling means, or about whether
    /// an unticked box records a decision or merely a default.
    ///
    /// The editor shows the whole catalogue with the reason each module is or is not available -
    /// granted by the role, withheld by the plan, or a deliberate decision about this person. An
    /// administrator who can only see a ticked box has no way to tell those apart, and would
    /// have to guess whether unticking one is a change or a no-op.
    /// </summary>
    internal static class UserAccessEditor
    {
        /// <summary>
        /// Opens the editor for one account. Returns true only when a change was saved, so the
        /// caller knows whether it needs to reload.
        /// </summary>
        /// <param name="owner">The page the dialog is shown over.</param>
        /// <param name="showError">
        /// Where a failure message goes. Passed in rather than shown here, because each page has
        /// its own status strip and a desktop screen never reports a server failure in a
        /// message box of its own.
        /// </param>
        public static async Task<bool> EditModulesAsync(
            Control owner, FitCoreSession session, int appUserId, Action<string?> showError)
        {
            var loaded = await session.Users.GetPermissionsAsync(appUserId);

            if (!loaded.IsSuccess || loaded.Value is null)
            {
                showError(loaded.ErrorMessage);
                return false;
            }

            var editor = loaded.Value;

            var fields = editor.Modules.Select(m => new FieldSpec(
                    m.Module, m.DisplayName, FieldKind.Check)
                {
                    Value = m.Effective,

                    // A module the plan does not include cannot be granted at all - the server
                    // seeds its answer from the tier, so a tick here would be refused. Showing
                    // it read-only explains the ceiling instead of hiding it.
                    ReadOnly = !m.AvailableInTier,

                    Hint = !m.AvailableInTier
                        ? $"Not part of the {editor.EnterpriseTierName} Enterprise plan."
                        : m.UserOverride is null
                            ? m.GrantedByRole
                                ? "Granted by their role."
                                : "Not granted by their role."
                            : m.UserOverride == true
                                ? "Granted specifically to this person."
                                : "Withheld specifically from this person."
                })
                .ToList();

            return EditDialog.Run(owner,
                $"{editor.FullName} · access",
                $"{editor.RoleDisplayName} on the {editor.EnterpriseTierName} plan. " +
                "Ticking a box that the role already grants changes nothing; unticking one " +
                "records a decision about this person that survives a change to the role.",
                fields,
                async f =>
                {
                    var granted = f
                        .Where(x => x.Flag)
                        .Select(x => x.Key)
                        .ToList();

                    var result = await session.Users.SetPermissionsAsync(appUserId, granted);
                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Save access");
        }
    }
}
