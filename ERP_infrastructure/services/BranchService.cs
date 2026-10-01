using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    /// <summary>One branch, as every screen and endpoint sees it.</summary>
    public class BranchView
    {
        public int BranchId { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Address { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Email { get; set; } = "";
        public bool IsPrimary { get; set; }
        public bool IsActive { get; set; }
        public DateTime OpenedOn { get; set; }

        /// <summary>Headcount, so the branch list is not a list of empty names.</summary>
        public int EmployeeCount { get; set; }
        public int MemberCount { get; set; }
    }

    /// <summary>
    /// What one branch did over a period, and the shape the company total is reported in too -
    /// the consolidated row is the same measurements with <see cref="BranchId"/> left null, so a
    /// comparison never has to reconcile two different definitions of revenue.
    /// </summary>
    public class BranchSummaryView
    {
        /// <summary>Null on the consolidated row that covers the whole company.</summary>
        public int? BranchId { get; set; }
        public string BranchCode { get; set; } = "";
        public string BranchName { get; set; } = "";
        public bool IsConsolidated => BranchId is null;

        public int Members { get; set; }
        public int ActiveSubscriptions { get; set; }
        public int Employees { get; set; }

        public decimal MembershipRevenue { get; set; }
        public decimal SalesRevenue { get; set; }
        public decimal PaymentsCollected { get; set; }
        public decimal Expenses { get; set; }
        public decimal PayrollCost { get; set; }

        public decimal TotalRevenue => MembershipRevenue + SalesRevenue;
        public decimal NetPosition => TotalRevenue - Expenses - PayrollCost;

        public int SaleCount { get; set; }
        public int PaymentCount { get; set; }

        /// <summary>This branch's revenue as a percentage of the company's, for the comparison.</summary>
        public decimal ShareOfCompanyRevenue { get; set; }
    }

    /// <summary>The company beside each of its branches, over one period.</summary>
    public class BranchComparisonView
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }

        /// <summary>The whole company, branches and unassigned records together.</summary>
        public BranchSummaryView Company { get; set; } = new();

        public List<BranchSummaryView> Branches { get; set; } = new();

        public string? TopBranchByRevenue { get; set; }
        public string? LowestBranchByRevenue { get; set; }
    }

    public class BranchTransferResult
    {
        public int MembersMoved { get; set; }
        public int EmployeesMoved { get; set; }
        public int CustomersMoved { get; set; }
        public int TotalMoved => MembersMoved + EmployeesMoved + CustomersMoved;
    }

    /// <summary>What a transfer is allowed to move.</summary>
    public enum BranchTransferScope
    {
        Members = 0,
        Employees = 1,
        Customers = 2,
        AllStandingRecords = 3
    }

    public interface IBranchService
    {
        Task<List<BranchView>> GetBranchesAsync(bool includeInactive = true);
        Task<BranchView?> GetBranchAsync(int branchId);
        Task<Branch> CreateBranchAsync(Branch branch);
        Task<BranchView?> UpdateBranchAsync(
            int branchId, string code, string name, string address, string phone,
            string email, bool isActive);
        Task<bool> SetPrimaryAsync(int branchId);
        Task<bool> DeleteBranchAsync(int branchId);
        Task<BranchTransferResult> TransferAsync(
            int fromBranchId, int toBranchId, BranchTransferScope scope);
        Task<BranchComparisonView> CompareAsync(DateTime from, DateTime to);

        /// <summary>
        /// The branch ids that exist and are open, for validating a requested selection.
        /// Cheap enough to call per branch switch; never called on a request that has not asked
        /// for a branch.
        /// </summary>
        Task<HashSet<int>> GetSelectableBranchIdsAsync();
    }

    /// <summary>
    /// The company's branch network.
    ///
    /// This service is the one place that deliberately reads across branches, so almost every
    /// query here carries <c>IgnoreQueryFilters</c>. That is not a loophole: branch
    /// administration is Admin-only by <c>[RequireSubmodule]</c>, and an Admin account is never
    /// bound to a branch in the first place. The filter exists to stop a branch manager reading
    /// another branch, and a branch manager cannot reach any of these endpoints.
    /// </summary>
    public class BranchService : IBranchService
    {
        private readonly TenantErpDbContext _db;

        public BranchService(TenantErpDbContext db)
        {
            _db = db;
        }

        public async Task<List<BranchView>> GetBranchesAsync(bool includeInactive = true)
        {
            var branches = await _db.Branches
                .AsNoTracking()
                .Where(b => includeInactive || b.IsActive)
                .OrderByDescending(b => b.IsPrimary)
                .ThenBy(b => b.Code)
                .ToListAsync();

            if (branches.Count == 0) return new List<BranchView>();

            var employeeCounts = await _db.Employees
                .IgnoreQueryFilters()
                .Where(e => e.BranchId != null)
                .GroupBy(e => e.BranchId!.Value)
                .Select(g => new { BranchId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.BranchId, x => x.Count);

            var memberCounts = await _db.Members
                .IgnoreQueryFilters()
                .Where(m => m.BranchId != null)
                .GroupBy(m => m.BranchId!.Value)
                .Select(g => new { BranchId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.BranchId, x => x.Count);

            return branches.Select(b => ToView(b, employeeCounts, memberCounts)).ToList();
        }

        public async Task<BranchView?> GetBranchAsync(int branchId)
        {
            var branch = await _db.Branches.AsNoTracking()
                .FirstOrDefaultAsync(b => b.BranchId == branchId);

            if (branch is null) return null;

            var employees = await _db.Employees.IgnoreQueryFilters()
                .CountAsync(e => e.BranchId == branchId);
            var members = await _db.Members.IgnoreQueryFilters()
                .CountAsync(m => m.BranchId == branchId);

            return ToView(
                branch,
                new Dictionary<int, int> { [branchId] = employees },
                new Dictionary<int, int> { [branchId] = members });
        }

        public async Task<Branch> CreateBranchAsync(Branch branch)
        {
            Validate(branch.Code, branch.Name);
            await EnsureCodeIsFree(branch.Code, exceptId: null);

            branch.Code = branch.Code.Trim().ToUpperInvariant();
            branch.Name = branch.Name.Trim();
            branch.CreatedAt = DateTime.UtcNow;

            if (branch.OpenedOn == default) branch.OpenedOn = DateTime.UtcNow.Date;

            // The first branch a company creates is its primary one, whatever was asked for.
            // A tenant with branches but no primary would have nowhere to put a record written
            // while no branch was selected.
            var isFirst = !await _db.Branches.AnyAsync();
            if (isFirst) branch.IsPrimary = true;

            if (branch.IsPrimary && !isFirst) await ClearPrimaryFlags();

            _db.Branches.Add(branch);
            await _db.SaveChangesAsync();

            return branch;
        }

        public async Task<BranchView?> UpdateBranchAsync(
            int branchId, string code, string name, string address, string phone,
            string email, bool isActive)
        {
            var branch = await _db.Branches.FirstOrDefaultAsync(b => b.BranchId == branchId);
            if (branch is null) return null;

            Validate(code, name);
            await EnsureCodeIsFree(code, exceptId: branchId);

            if (branch.IsPrimary && !isActive)
            {
                throw new ValidationException(
                    "The primary branch cannot be closed. Make another branch primary first, " +
                    "then close this one.");
            }

            branch.Code = code.Trim().ToUpperInvariant();
            branch.Name = name.Trim();
            branch.Address = address?.Trim() ?? "";
            branch.Phone = phone?.Trim() ?? "";
            branch.Email = email?.Trim() ?? "";
            branch.IsActive = isActive;

            await _db.SaveChangesAsync();

            return await GetBranchAsync(branchId);
        }

        public async Task<bool> SetPrimaryAsync(int branchId)
        {
            var branch = await _db.Branches.FirstOrDefaultAsync(b => b.BranchId == branchId);
            if (branch is null) return false;

            if (!branch.IsActive)
            {
                throw new ValidationException(
                    "A closed branch cannot be the primary one. Reopen it first.");
            }

            await ClearPrimaryFlags();

            branch.IsPrimary = true;
            await _db.SaveChangesAsync();

            return true;
        }

        /// <summary>
        /// Deleting a branch is refused as soon as anything has happened there, for the same
        /// reason a member who has paid cannot be deleted: the branch is what explains which
        /// till took the money, and a sale whose branch has been deleted is a sale that belongs
        /// to nobody. Closing it is the supported alternative and keeps every figure intact.
        /// </summary>
        public async Task<bool> DeleteBranchAsync(int branchId)
        {
            var branch = await _db.Branches.FirstOrDefaultAsync(b => b.BranchId == branchId);
            if (branch is null) return false;

            if (branch.IsPrimary)
            {
                throw new ValidationException(
                    "The primary branch cannot be deleted. Make another branch primary first.");
            }

            var blockers = await CountRecordsAsync(branchId);

            if (blockers.Total > 0)
            {
                throw new ValidationException(
                    $"'{branch.Name}' has {blockers.Describe()} on record and cannot be deleted. " +
                    "Close the branch instead, or transfer its records to another branch first - " +
                    "either way its history stays where it happened.");
            }

            _db.Branches.Remove(branch);
            await _db.SaveChangesAsync();

            return true;
        }

        /// <summary>
        /// Moves standing records - the people - from one branch to another.
        ///
        /// Transactions are deliberately not transferable. A sale, a payment, a payroll run and
        /// an expense are statements about what happened at a particular site on a particular
        /// day; moving one would change two branches' revenue after the fact and leave the
        /// ledger, which is company-wide, disagreeing with both. Moving a member or an employee
        /// changes only where they are managed from, which is a real thing that happens.
        /// </summary>
        public async Task<BranchTransferResult> TransferAsync(
            int fromBranchId, int toBranchId, BranchTransferScope scope)
        {
            if (fromBranchId == toBranchId)
            {
                throw new ValidationException(
                    "The source and destination branches are the same.");
            }

            _ = await _db.Branches.FirstOrDefaultAsync(b => b.BranchId == fromBranchId)
                ?? throw new ValidationException("The branch being transferred from was not found.");

            var destination = await _db.Branches.FirstOrDefaultAsync(b => b.BranchId == toBranchId)
                ?? throw new ValidationException("The branch being transferred to was not found.");

            if (!destination.IsActive)
            {
                throw new ValidationException(
                    $"'{destination.Name}' is closed, so records cannot be transferred into it.");
            }

            var result = new BranchTransferResult();

            var moveMembers = scope is BranchTransferScope.Members or BranchTransferScope.AllStandingRecords;
            var moveEmployees = scope is BranchTransferScope.Employees or BranchTransferScope.AllStandingRecords;
            var moveCustomers = scope is BranchTransferScope.Customers or BranchTransferScope.AllStandingRecords;

            if (moveMembers)
            {
                var members = await _db.Members.IgnoreQueryFilters()
                    .Where(m => m.BranchId == fromBranchId).ToListAsync();

                foreach (var member in members) member.BranchId = toBranchId;
                result.MembersMoved = members.Count;
            }

            if (moveEmployees)
            {
                var employees = await _db.Employees.IgnoreQueryFilters()
                    .Where(e => e.BranchId == fromBranchId).ToListAsync();

                foreach (var employee in employees) employee.BranchId = toBranchId;
                result.EmployeesMoved = employees.Count;
            }

            if (moveCustomers)
            {
                var customers = await _db.Customers.IgnoreQueryFilters()
                    .Where(c => c.BranchId == fromBranchId).ToListAsync();

                foreach (var customer in customers) customer.BranchId = toBranchId;
                result.CustomersMoved = customers.Count;
            }

            if (result.TotalMoved > 0) await _db.SaveChangesAsync();

            return result;
        }

        public async Task<BranchComparisonView> CompareAsync(DateTime from, DateTime to)
        {
            // Inclusive of the whole closing day, so a comparison run "to today" counts today.
            var start = from.Date;
            var end = to.Date.AddDays(1).AddTicks(-1);

            var branches = await _db.Branches.AsNoTracking()
                .OrderByDescending(b => b.IsPrimary).ThenBy(b => b.Code)
                .ToListAsync();

            var company = await MeasureAsync(null, start, end);
            company.BranchName = "All branches";
            company.BranchCode = "ALL";
            company.ShareOfCompanyRevenue = company.TotalRevenue == 0 ? 0 : 100m;

            var rows = new List<BranchSummaryView>();

            foreach (var branch in branches)
            {
                var row = await MeasureAsync(branch.BranchId, start, end);
                row.BranchCode = branch.Code;
                row.BranchName = branch.Name;

                row.ShareOfCompanyRevenue = company.TotalRevenue == 0
                    ? 0
                    : Math.Round(row.TotalRevenue / company.TotalRevenue * 100m, 2);

                rows.Add(row);
            }

            var ranked = rows.Where(r => r.TotalRevenue > 0)
                             .OrderByDescending(r => r.TotalRevenue).ToList();

            return new BranchComparisonView
            {
                From = start,
                To = to.Date,
                Company = company,
                Branches = rows,
                TopBranchByRevenue = ranked.FirstOrDefault()?.BranchName,
                LowestBranchByRevenue = ranked.Count > 1 ? ranked.Last().BranchName : null
            };
        }

        public async Task<HashSet<int>> GetSelectableBranchIdsAsync()
        {
            var ids = await _db.Branches.AsNoTracking()
                .Where(b => b.IsActive)
                .Select(b => b.BranchId)
                .ToListAsync();

            return ids.ToHashSet();
        }

        /// <summary>
        /// One branch's figures, or the whole company's when <paramref name="branchId"/> is null.
        ///
        /// Every query ignores the request's own branch filter and states the branch it wants
        /// explicitly, so the numbers do not change depending on which branch the Admin happened
        /// to have selected when they opened the screen.
        /// </summary>
        private async Task<BranchSummaryView> MeasureAsync(int? branchId, DateTime start, DateTime end)
        {
            var members = await _db.Members.IgnoreQueryFilters()
                .CountAsync(m => branchId == null || m.BranchId == branchId);

            var employees = await _db.Employees.IgnoreQueryFilters()
                .CountAsync(e => (branchId == null || e.BranchId == branchId) && e.Status == "Active");

            var activeSubscriptions = await _db.Subscriptions.IgnoreQueryFilters()
                .CountAsync(s => (branchId == null || s.BranchId == branchId) && s.Status == "Active");

            var sales = await _db.Sales.IgnoreQueryFilters()
                .Where(s => (branchId == null || s.BranchId == branchId)
                         && s.SaleDate >= start && s.SaleDate <= end)
                .Select(s => new { s.TotalAmount })
                .ToListAsync();

            var payments = await _db.Payments.IgnoreQueryFilters()
                .Where(p => (branchId == null || p.BranchId == branchId)
                         && p.PaymentDate >= start && p.PaymentDate <= end
                         && p.Status == "Completed")
                .Select(p => new { p.Amount, p.Category })
                .ToListAsync();

            var expenses = await _db.Expenses.IgnoreQueryFilters()
                .Where(e => (branchId == null || e.BranchId == branchId)
                         && e.ExpenseDate >= start && e.ExpenseDate <= end)
                .SumAsync(e => (decimal?)e.Amount) ?? 0m;

            var payroll = await _db.Payrolls.IgnoreQueryFilters()
                .Where(p => (branchId == null || p.BranchId == branchId)
                         && p.PeriodEnd >= start && p.PeriodEnd <= end
                         && p.Status == "Paid")
                .SumAsync(p => (decimal?)p.NetPay) ?? 0m;

            return new BranchSummaryView
            {
                BranchId = branchId,
                Members = members,
                Employees = employees,
                ActiveSubscriptions = activeSubscriptions,
                SalesRevenue = sales.Sum(s => s.TotalAmount),
                SaleCount = sales.Count,
                PaymentsCollected = payments.Sum(p => p.Amount),
                PaymentCount = payments.Count,
                MembershipRevenue = payments
                    .Where(p => string.Equals(p.Category, PaymentCategories.Membership,
                                              StringComparison.OrdinalIgnoreCase))
                    .Sum(p => p.Amount),
                Expenses = expenses,
                PayrollCost = payroll
            };
        }

        private async Task ClearPrimaryFlags()
        {
            var current = await _db.Branches.Where(b => b.IsPrimary).ToListAsync();
            foreach (var branch in current) branch.IsPrimary = false;
        }

        private static BranchView ToView(
            Branch branch,
            IReadOnlyDictionary<int, int> employeeCounts,
            IReadOnlyDictionary<int, int> memberCounts) => new()
            {
                BranchId = branch.BranchId,
                Code = branch.Code,
                Name = branch.Name,
                Address = branch.Address,
                Phone = branch.Phone,
                Email = branch.Email,
                IsPrimary = branch.IsPrimary,
                IsActive = branch.IsActive,
                OpenedOn = branch.OpenedOn,
                EmployeeCount = employeeCounts.TryGetValue(branch.BranchId, out var e) ? e : 0,
                MemberCount = memberCounts.TryGetValue(branch.BranchId, out var m) ? m : 0
            };

        private sealed record RecordCounts(int Members, int Employees, int Sales, int Payments)
        {
            public int Total => Members + Employees + Sales + Payments;

            public string Describe()
            {
                var parts = new List<string>();
                if (Members > 0) parts.Add($"{Members} member(s)");
                if (Employees > 0) parts.Add($"{Employees} employee(s)");
                if (Sales > 0) parts.Add($"{Sales} sale(s)");
                if (Payments > 0) parts.Add($"{Payments} payment(s)");
                return string.Join(", ", parts);
            }
        }

        private async Task<RecordCounts> CountRecordsAsync(int branchId) => new(
            await _db.Members.IgnoreQueryFilters().CountAsync(m => m.BranchId == branchId),
            await _db.Employees.IgnoreQueryFilters().CountAsync(e => e.BranchId == branchId),
            await _db.Sales.IgnoreQueryFilters().CountAsync(s => s.BranchId == branchId),
            await _db.Payments.IgnoreQueryFilters().CountAsync(p => p.BranchId == branchId));

        private static void Validate(string code, string name)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ValidationException("A branch code is required.");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ValidationException("A branch name is required.");
            }
        }

        private async Task EnsureCodeIsFree(string code, int? exceptId)
        {
            // Upper-cased before comparing, because that is how CreateBranchAsync stores it.
            // Comparing the raw input would let "bra" past a check that "BRA" is taken, and the
            // unique index would then answer with a provider error and a 500.
            var trimmed = code.Trim().ToUpperInvariant();

            var clash = await _db.Branches.AnyAsync(b =>
                b.BranchId != exceptId && b.Code == trimmed);

            if (clash)
            {
                throw new ValidationException($"Branch code '{trimmed}' is already in use.");
            }
        }
    }
}
