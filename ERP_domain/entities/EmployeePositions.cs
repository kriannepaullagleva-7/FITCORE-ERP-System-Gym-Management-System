namespace ERP_domain.entities
{
    /// <summary>
    /// The posts an employee can hold.
    ///
    /// Closed on purpose, and deliberately just the two operational posts. A position is no
    /// longer only a job title: it decides what system access the person's account is created
    /// with, so an arbitrary string here would mean an employee whose role cannot be resolved.
    /// System Admin is an application access-control role granted through User Access, not an
    /// operational post an employee is hired into - there is no "Admin" employee position, so
    /// the roster can never create one by accident.
    /// </summary>
    public static class EmployeePositions
    {
        public const string Staff = "Staff";
        public const string Manager = "Manager";

        public static readonly IReadOnlyList<string> All = new[] { Staff, Manager };

        public static bool IsKnown(string? value) =>
            value is not null && All.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The canonical spelling - Staff or Manager, never anything else - for a value that
        /// might be a free-text job title rather than one of the two exact strings. A branch's
        /// roster is seeded with real titles ("Branch Manager", "Receptionist") that describe the
        /// post without spelling either canonical word exactly, so an exact-match comparison
        /// would treat legitimate staff as unrecognised. Anything mentioning "manager" is that
        /// bucket; everything else - including a title as plain as "Receptionist" - defaults to
        /// Staff, the same floor <see cref="ToRoleKey"/> already uses for an unrecognised value.
        /// Only blank input is still unresolved, since there is no post to infer from nothing.
        /// </summary>
        public static string? Normalise(string? value)
        {
            var trimmed = (value ?? "").Trim();
            if (trimmed.Length == 0) return null;

            return trimmed.Contains(Manager, StringComparison.OrdinalIgnoreCase) ? Manager : Staff;
        }

        /// <summary>
        /// The sign-in role an employee in this position gets. Staff is the floor: anything
        /// unrecognised lands there rather than being granted something it should not have.
        /// </summary>
        public static string ToRoleKey(string? position) => Normalise(position) switch
        {
            Manager => ErpRoles.Manager,
            _ => ErpRoles.Staff
        };

        /// <summary>
        /// How senior this position is, using the same scale as <see cref="ErpRoles.LevelOf"/>
        /// where a lower number is more senior.
        /// </summary>
        public static int LevelOf(string? position) => ErpRoles.LevelOf(ToRoleKey(position));
    }
}
