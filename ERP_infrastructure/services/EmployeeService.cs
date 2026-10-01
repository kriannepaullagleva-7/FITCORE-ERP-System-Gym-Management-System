using ERP_domain.entities;
using ERP_infrastructure.repositories;

namespace ERP_infrastructure.services
{
    public class EmployeeService : IEmployeeService
    {
        private static readonly string[] AllowedStatuses = { "Active", "Inactive", "Terminated" };

        /// <summary>The password every auto-created account starts with, then must change.</summary>
        public const string DefaultAccountPassword = "FitCore@2026";

        private readonly IEmployeeRepository _repository;
        private readonly ICurrentUserAccessor _actor;
        private readonly IUserAccountService _users;

        public EmployeeService(
            IEmployeeRepository repository,
            ICurrentUserAccessor actor,
            IUserAccountService users)
        {
            _repository = repository;
            _actor = actor;
            _users = users;
        }

        // ------------------------------------------------------------------ authorisation
        //
        // These guards live here rather than on the controller on purpose. A rule enforced at
        // the HTTP edge only holds for callers that come through that edge; enforced in the
        // service it holds for every caller, including a future one.

        private int ActingLevel => ErpRoles.LevelOf(_actor.Current.RoleKey);

        private bool IsAdmin => ActingLevel <= ErpRoles.LevelOf(ErpRoles.Admin);

        /// <summary>
        /// Managing the roster at all. Staff are refused: the module may be visible to them
        /// for a colleague lookup, but changing records is not theirs to do.
        /// </summary>
        private void RequireRosterAuthority(string verb)
        {
            // Work with no signed-in user is the bootstrapper or a test, which is trusted.
            if (_actor.Current.AppUserId is null) return;

            if (ActingLevel > ErpRoles.LevelOf(ErpRoles.Manager))
            {
                throw new ForbiddenOperationException(
                    $"Your account does not have permission to {verb} employees.");
            }
        }

        /// <summary>
        /// Only an administrator appoints somebody to a post more senior than Staff. Checked
        /// against the acting user's real role from the token, so editing the request cannot
        /// get around it.
        /// </summary>
        private void RequireAdminFor(string what)
        {
            if (_actor.Current.AppUserId is null) return;

            if (!IsAdmin)
            {
                throw new ForbiddenOperationException(
                    $"Only an administrator can {what}.");
            }
        }

        /// <summary>
        /// Who may change a given employee's pay. An administrator may set anyone's; a manager
        /// may set anyone's pay but their own - Staff or a peer Manager alike. The one rule that
        /// never bends is that nobody sets their own pay, checked by employee id rather than
        /// position, since that is what is actually being protected.
        /// </summary>
        private void RequireSalaryAuthority(Employee target)
        {
            if (_actor.Current.AppUserId is null) return;
            if (IsAdmin) return;

            if (ActingLevel == ErpRoles.LevelOf(ErpRoles.Manager))
            {
                if (_actor.Current.EmployeeId.HasValue &&
                    _actor.Current.EmployeeId.Value == target.EmployeeId)
                {
                    throw new ForbiddenOperationException("You cannot change your own pay.");
                }

                return;
            }

            throw new ForbiddenOperationException(
                "Only an administrator or manager can change an employee's pay.");
        }

        private string RequirePosition(string? position)
        {
            var normalised = EmployeePositions.Normalise(position);

            if (normalised is null)
            {
                throw new ValidationException(
                    "Position must be Staff or Manager.");
            }

            // A manager may hire, but only at Staff level - appointing a peer or a senior is
            // an administrator's decision.
            if (!string.Equals(normalised, EmployeePositions.Staff, StringComparison.OrdinalIgnoreCase))
            {
                RequireAdminFor($"appoint somebody as {normalised}");
            }

            return normalised;
        }

        public Task<List<Employee>> GetAllEmployeesAsync() => _repository.GetAllAsync();

        public Task<List<Employee>> GetActiveEmployeesAsync() => _repository.GetActiveEmployeesAsync();

        public Task<List<Employee>> SearchEmployeesAsync(string term) => _repository.SearchAsync(term);

        public Task<Employee?> GetEmployeeByIdAsync(int id) => _repository.GetByIdAsync(id);

        public async Task<Employee> CreateEmployeeAsync(
            string employeeCode, string firstName, string lastName, string position,
            string department, string phone, string email, DateTime hireDate, decimal basicSalary,
            decimal hourlyRate = 0m)
        {
            RequireRosterAuthority("add");

            var code = Clean(employeeCode);
            var post = RequirePosition(position);

            ValidateCore(code, firstName, lastName, basicSalary, hourlyRate);

            // No separate pay gate needed here: RequirePosition above already forced a
            // Manager's new hire to Staff level or refused it, and a Manager setting a Staff
            // hire's starting pay is exactly what "Manager can create Staff salary records"
            // means. An Admin reaches here regardless of the position chosen.

            if (await _repository.CodeExistsAsync(code))
            {
                throw new ValidationException($"Employee code '{code}' is already in use.");
            }

            return await _repository.AddAsync(new Employee
            {
                EmployeeCode = code,
                FirstName = Clean(firstName),
                LastName = Clean(lastName),
                Position = post,
                Department = Clean(department),
                Phone = Clean(phone),
                Email = Clean(email),
                HireDate = hireDate == default ? DateTime.UtcNow : hireDate,
                BasicSalary = basicSalary,
                HourlyRate = hourlyRate,
                Status = "Active",
                CreatedAt = DateTime.UtcNow
            });
        }

        public async Task<Employee?> UpdateEmployeeAsync(
            int id, string employeeCode, string firstName, string lastName, string position,
            string department, string phone, string email, DateTime hireDate,
            decimal basicSalary, string status, decimal hourlyRate = 0m)
        {
            RequireRosterAuthority("edit");

            var employee = await _repository.GetByIdAsync(id);
            if (employee == null) return null;

            var code = Clean(employeeCode);

            ValidateCore(code, firstName, lastName, basicSalary, hourlyRate);

            // Only what actually changes is guarded. A manager may correct a phone number on
            // an administrator's record; they may not alter that person's pay or their post,
            // and the pay rule further depends on who the record belongs to - see
            // RequireSalaryAuthority.
            if (basicSalary != employee.BasicSalary || hourlyRate != employee.HourlyRate)
            {
                RequireSalaryAuthority(employee);
            }

            var post = RequirePositionChange(employee, position);

            if (!AllowedStatuses.Contains(status))
            {
                throw new ValidationException(
                    "Status must be Active, Inactive or Terminated.");
            }

            if (await _repository.CodeExistsAsync(code, id))
            {
                throw new ValidationException($"Employee code '{code}' is already in use.");
            }

            employee.EmployeeCode = code;
            employee.FirstName = Clean(firstName);
            employee.LastName = Clean(lastName);
            employee.Position = post;
            employee.Department = Clean(department);
            employee.Phone = Clean(phone);
            employee.Email = Clean(email);
            employee.HireDate = hireDate == default ? employee.HireDate : hireDate;
            employee.BasicSalary = basicSalary;
            employee.HourlyRate = hourlyRate;
            employee.Status = status;

            return await _repository.UpdateAsync(employee);
        }

        /// <summary>
        /// Validates a position on an existing employee, and treats a change of post as the
        /// promotion or demotion it is.
        ///
        /// The stored text survives a save that does not actually move the employee between
        /// buckets - a real job title such as "Branch Manager" is not collapsed to the bare
        /// canonical "Manager" just because a non-admin's read-only combo resubmitted it
        /// unchanged, or because an admin corrected an unrelated field. Only an actual
        /// promotion or demotion writes the new canonical value.
        /// </summary>
        private string RequirePositionChange(Employee employee, string? position)
        {
            var wanted = EmployeePositions.Normalise(position)
                ?? throw new ValidationException("Position must be Staff or Manager.");

            var current = EmployeePositions.Normalise(employee.Position);

            // Unchanged, so nothing to authorise - a manager can still edit the rest of the
            // record of somebody senior to them without being able to move them.
            if (string.Equals(wanted, current, StringComparison.OrdinalIgnoreCase))
            {
                return employee.Position;
            }

            var direction = EmployeePositions.LevelOf(wanted) < EmployeePositions.LevelOf(current)
                ? "promote"
                : "demote";

            RequireAdminFor($"{direction} an employee");
            return wanted;
        }

        public async Task<EmployeeDeleteResult> DeleteEmployeeAsync(int id)
        {
            RequireRosterAuthority("remove");

            var employee = await _repository.GetByIdAsync(id);
            if (employee == null) return EmployeeDeleteResult.NotFound;

            // Pay history is a financial record. Rather than destroy it, the caller is told to
            // mark the employee Inactive instead.
            if (await _repository.CountPayrollsAsync(id) > 0)
            {
                return EmployeeDeleteResult.HasPayrollHistory;
            }

            await _repository.DeleteAsync(id);
            return EmployeeDeleteResult.Deleted;
        }

        /// <summary>
        /// Gives an employee a sign-in, reusing the User Access machinery rather than a
        /// second copy of it - so the password is hashed the same way, the seniority rule is
        /// the same one, and the account is bound to the same company.
        ///
        /// The two records live in different physical databases: the Employee is in this
        /// tenant's database and the AppUser is in the master. There is no transaction that
        /// spans both, so the order matters and is deliberate:
        ///
        ///   1. the employee is already saved by the time this runs,
        ///   2. an existing account for the same person is detected and reused,
        ///   3. only then is a new account written,
        ///   4. a failure leaves the employee intact and the operation repeatable.
        ///
        /// Nothing is rolled back on failure. Deleting a saved employee because a second
        /// database was briefly unreachable would lose real data to solve a retryable problem.
        /// </summary>
        public async Task<EmployeeAccountResult> EnsureAccountAsync(
            Employee employee, CancellationToken cancellationToken = default)
        {
            RequireRosterAuthority("create accounts for");

            var email = Clean(employee.Email);

            if (string.IsNullOrWhiteSpace(email))
            {
                return EmployeeAccountResult.Skipped(
                    $"{employee.FirstName} {employee.LastName} was saved, but no email address " +
                    "was given, so no sign-in could be created. Add one and edit the employee " +
                    "to create their account.");
            }

            var companyId = _actor.Current.CompanyId;

            if (companyId is null or <= 0)
            {
                return EmployeeAccountResult.Skipped(
                    "The employee was saved, but FitCore could not tell which company the " +
                    "account should belong to. Their sign-in can be created by editing them.");
            }

            try
            {
                // Idempotent: a retry after a half-finished attempt finds the account that
                // already exists rather than creating a second one.
                var existing = (await _users.GetUsersAsync(companyId.Value, cancellationToken))
                    .FirstOrDefault(u =>
                        u.EmployeeId == employee.EmployeeId ||
                        string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(u.Username, email, StringComparison.OrdinalIgnoreCase));

                if (existing is not null)
                {
                    return EmployeeAccountResult.AlreadyExisted(
                        $"{email} already has a FitCore sign-in, so no second account was created.");
                }

                // The email is the username, because that is what the operator was told the
                // credential is. Sign-in accepts either, so both spellings work.
                var account = await _users.CreateUserAsync(
                    companyId: companyId.Value,
                    username: email,
                    fullName: $"{employee.FirstName} {employee.LastName}".Trim(),
                    email: email,
                    password: DefaultAccountPassword,
                    roleKey: EmployeePositions.ToRoleKey(employee.Position),
                    employeeId: employee.EmployeeId,
                    actingRoleLevel: ActingLevel,
                    cancellationToken: cancellationToken);

                return EmployeeAccountResult.Created(
                    $"A sign-in was created for {email}. They must change the default password " +
                    "the first time they sign in.");
            }
            catch (ValidationException ex)
            {
                // A rule refused it - a taken username, a role above the caller's own. The
                // employee stays; the message says precisely what to fix.
                return EmployeeAccountResult.Failed(
                    $"The employee was saved, but their sign-in could not be created: {ex.Message} " +
                    "Edit the employee to try again.");
            }
            catch (ForbiddenOperationException ex)
            {
                return EmployeeAccountResult.Failed(
                    $"The employee was saved, but their sign-in could not be created: {ex.Message}");
            }
        }

        private static void ValidateCore(
            string code, string firstName, string lastName, decimal basicSalary, decimal hourlyRate)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ValidationException("An employee code is required.");

            if (string.IsNullOrWhiteSpace(firstName))
                throw new ValidationException("A first name is required.");

            if (string.IsNullOrWhiteSpace(lastName))
                throw new ValidationException("A last name is required.");

            if (basicSalary < 0m)
                throw new ValidationException("Basic salary cannot be negative.");

            if (hourlyRate < 0m)
                throw new ValidationException("Hourly rate cannot be negative.");
        }

        private static string Clean(string? value) => (value ?? string.Empty).Trim();
    }
}
