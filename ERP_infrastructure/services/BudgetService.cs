using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    /// <summary>
    /// Budgets: what the gym plans to earn and spend, by account and month.
    ///
    /// A budget is never posted. It expresses intent rather than a transaction, so it lives
    /// beside the ledger rather than in it, and Budget vs Actual reads both and shows the
    /// difference. The ledger is never adjusted to match the plan.
    /// </summary>
    public interface IBudgetService
    {
        Task<List<BudgetView>> GetBudgetsAsync(int? year = null);

        Task<BudgetView?> GetBudgetAsync(int budgetId);

        Task<BudgetView> CreateBudgetAsync(string name, int year, string notes);

        Task<BudgetView?> UpdateBudgetAsync(int budgetId, string name, int year, string notes);

        /// <summary>Signs a budget off. Only an approved budget is compared against actuals.</summary>
        Task<BudgetView?> ApproveAsync(int budgetId);

        Task<bool> DeleteBudgetAsync(int budgetId);

        /// <summary>
        /// Sets one account's figure for one month, creating or replacing the line. Setting it
        /// to zero removes the line rather than storing a nought, so an untouched account stays
        /// visibly untouched.
        /// </summary>
        Task<BudgetView?> SetLineAsync(int budgetId, int accountId, int month, decimal amount, string notes);

        /// <summary>
        /// Spreads one annual figure evenly across twelve months, which is how most budgets
        /// actually start life before anybody adjusts a month.
        /// </summary>
        Task<BudgetView?> SpreadAsync(int budgetId, int accountId, decimal annualAmount);

        Task<bool> RemoveLineAsync(int budgetLineId);
    }

    public class BudgetService : IBudgetService
    {
        private readonly TenantErpDbContext _context;
        private readonly ICurrentUserAccessor _actor;

        public BudgetService(TenantErpDbContext context, ICurrentUserAccessor actor)
        {
            _context = context;
            _actor = actor;
        }

        public async Task<List<BudgetView>> GetBudgetsAsync(int? year = null)
        {
            var query = _context.Budgets.AsNoTracking().Include(b => b.Lines).AsQueryable();

            if (year.HasValue) query = query.Where(b => b.Year == year.Value);

            var budgets = await query
                .OrderByDescending(b => b.Year)
                .ThenBy(b => b.Name)
                .ToListAsync();

            // The list does not need every line's account, only the totals.
            return budgets.Select(b => ToView(b, includeLines: false)).ToList();
        }

        public async Task<BudgetView?> GetBudgetAsync(int budgetId)
        {
            var budget = await _context.Budgets
                .AsNoTracking()
                .Include(b => b.Lines).ThenInclude(l => l.Account)
                .FirstOrDefaultAsync(b => b.BudgetId == budgetId);

            return budget is null ? null : ToView(budget, includeLines: true);
        }

        public async Task<BudgetView> CreateBudgetAsync(string name, int year, string notes)
        {
            var clean = Clean(name);

            if (clean.Length == 0) throw new ValidationException("A budget needs a name.");

            if (year is < 2000 or > 2100)
            {
                throw new ValidationException("The year must be between 2000 and 2100.");
            }

            if (await _context.Budgets.AnyAsync(b => b.Year == year && b.Name == clean))
            {
                throw new ValidationException(
                    $"There is already a budget called '{clean}' for {year}.");
            }

            var budget = new Budget
            {
                Name = clean,
                Year = year,
                Notes = Clean(notes),
                Status = BudgetStatuses.Draft,
                CreatedAt = DateTime.UtcNow
            };

            _context.Budgets.Add(budget);
            await _context.SaveChangesAsync();

            return (await GetBudgetAsync(budget.BudgetId))!;
        }

        public async Task<BudgetView?> UpdateBudgetAsync(int budgetId, string name, int year, string notes)
        {
            var budget = await _context.Budgets.FirstOrDefaultAsync(b => b.BudgetId == budgetId);
            if (budget is null) return null;

            var clean = Clean(name);
            if (clean.Length == 0) throw new ValidationException("A budget needs a name.");

            if (await _context.Budgets.AnyAsync(b =>
                    b.Year == year && b.Name == clean && b.BudgetId != budgetId))
            {
                throw new ValidationException($"There is already a budget called '{clean}' for {year}.");
            }

            budget.Name = clean;
            budget.Year = year;
            budget.Notes = Clean(notes);

            await _context.SaveChangesAsync();

            return await GetBudgetAsync(budgetId);
        }

        public async Task<BudgetView?> ApproveAsync(int budgetId)
        {
            var budget = await _context.Budgets
                .Include(b => b.Lines)
                .FirstOrDefaultAsync(b => b.BudgetId == budgetId);

            if (budget is null) return null;

            if (budget.Lines.Count == 0)
            {
                throw new ValidationException(
                    "An empty budget has nothing to compare against. Add at least one line first.");
            }

            var actor = _actor.Current;

            budget.Status = BudgetStatuses.Approved;
            budget.ApprovedAt = DateTime.UtcNow;
            budget.ApprovedByUserId = actor.AppUserId;
            budget.ApprovedBy = actor.Username ?? "";

            // One approved budget per year: two of them means Budget vs Actual has to guess.
            var superseded = await _context.Budgets
                .Where(b => b.Year == budget.Year && b.BudgetId != budgetId &&
                            b.Status == BudgetStatuses.Approved)
                .ToListAsync();

            foreach (var old in superseded) old.Status = BudgetStatuses.Archived;

            await _context.SaveChangesAsync();

            return await GetBudgetAsync(budgetId);
        }

        public async Task<bool> DeleteBudgetAsync(int budgetId)
        {
            var budget = await _context.Budgets.FirstOrDefaultAsync(b => b.BudgetId == budgetId);
            if (budget is null) return false;

            if (string.Equals(budget.Status, BudgetStatuses.Approved, StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    $"'{budget.Name}' is the approved budget for {budget.Year} and reports are " +
                    "being run against it. Archive it instead.");
            }

            _context.Budgets.Remove(budget);
            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<BudgetView?> SetLineAsync(
            int budgetId, int accountId, int month, decimal amount, string notes)
        {
            var budget = await _context.Budgets
                .Include(b => b.Lines)
                .FirstOrDefaultAsync(b => b.BudgetId == budgetId);

            if (budget is null) return null;

            if (month is < 1 or > 12) throw new ValidationException("The month must be between 1 and 12.");
            if (amount < 0m) throw new ValidationException("A budgeted amount cannot be negative.");

            await EnsureBudgetableAsync(accountId);

            var line = budget.Lines.FirstOrDefault(l => l.AccountId == accountId && l.Month == month);

            if (amount == 0m)
            {
                if (line is not null) _context.BudgetLines.Remove(line);
            }
            else if (line is null)
            {
                _context.BudgetLines.Add(new BudgetLine
                {
                    BudgetId = budgetId,
                    AccountId = accountId,
                    Month = month,
                    Amount = amount,
                    Notes = Clean(notes)
                });
            }
            else
            {
                line.Amount = amount;
                line.Notes = Clean(notes);
            }

            await _context.SaveChangesAsync();

            return await GetBudgetAsync(budgetId);
        }

        public async Task<BudgetView?> SpreadAsync(int budgetId, int accountId, decimal annualAmount)
        {
            var budget = await _context.Budgets
                .Include(b => b.Lines)
                .FirstOrDefaultAsync(b => b.BudgetId == budgetId);

            if (budget is null) return null;

            if (annualAmount < 0m) throw new ValidationException("A budgeted amount cannot be negative.");

            await EnsureBudgetableAsync(accountId);

            var existing = budget.Lines.Where(l => l.AccountId == accountId).ToList();
            _context.BudgetLines.RemoveRange(existing);

            if (annualAmount > 0m)
            {
                var monthly = Math.Round(annualAmount / 12m, 2, MidpointRounding.AwayFromZero);

                // The remainder goes on December rather than being lost, so twelve months add
                // up to the annual figure exactly.
                var remainder = annualAmount - (monthly * 12m);

                for (var month = 1; month <= 12; month++)
                {
                    _context.BudgetLines.Add(new BudgetLine
                    {
                        BudgetId = budgetId,
                        AccountId = accountId,
                        Month = month,
                        Amount = month == 12 ? monthly + remainder : monthly,
                        Notes = "Spread evenly across the year"
                    });
                }
            }

            await _context.SaveChangesAsync();

            return await GetBudgetAsync(budgetId);
        }

        public async Task<bool> RemoveLineAsync(int budgetLineId)
        {
            var line = await _context.BudgetLines.FirstOrDefaultAsync(l => l.BudgetLineId == budgetLineId);
            if (line is null) return false;

            _context.BudgetLines.Remove(line);
            await _context.SaveChangesAsync();

            return true;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Only revenue and expense accounts can be budgeted.
        ///
        /// Budgeting a bank balance or a payable is a category error: those are consequences of
        /// the plan, not the plan itself, and offering them makes Budget vs Actual meaningless.
        /// </summary>
        private async Task EnsureBudgetableAsync(int accountId)
        {
            var account = await _context.Accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.AccountId == accountId)
                ?? throw new ValidationException($"No account with id {accountId} exists.");

            if (!AccountTypes.IsTemporary(account.AccountType))
            {
                throw new ValidationException(
                    $"{account.AccountName} is {account.AccountType.ToLowerInvariant()}. " +
                    "Only revenue and expense accounts can be budgeted - the rest follow from them.");
            }

            if (!account.IsActive)
            {
                throw new ValidationException($"{account.AccountName} is inactive.");
            }
        }

        private static BudgetView ToView(Budget b, bool includeLines) => new()
        {
            BudgetId = b.BudgetId,
            Name = b.Name,
            Year = b.Year,
            Status = b.Status,
            Notes = b.Notes,
            ApprovedBy = b.ApprovedBy,
            ApprovedAt = b.ApprovedAt,
            CreatedAt = b.CreatedAt,
            TotalBudgeted = b.Lines.Sum(l => l.Amount),
            LineCount = b.Lines.Count,

            Lines = includeLines
                ? b.Lines
                    .OrderBy(l => l.Account.AccountCode)
                    .ThenBy(l => l.Month)
                    .Select(l => new BudgetLineView
                    {
                        BudgetLineId = l.BudgetLineId,
                        AccountId = l.AccountId,
                        AccountCode = l.Account?.AccountCode ?? "",
                        AccountName = l.Account?.AccountName ?? "",
                        AccountType = l.Account?.AccountType ?? "",
                        Month = l.Month,
                        Amount = l.Amount,
                        Notes = l.Notes
                    })
                    .ToList()
                : new List<BudgetLineView>()
        };

        private static string Clean(string? value) => (value ?? "").Trim();
    }
}
