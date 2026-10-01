using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    /// <summary>
    /// Opening and closing the months the ledger will accept postings into.
    ///
    /// A period with no row is open. That is deliberate: closing is an opt-in control for a
    /// tenant that reports monthly, and a tenant that never uses it must not find its ledger
    /// refusing postings because nobody remembered to create a row for March.
    /// </summary>
    public interface IFinancialPeriodService
    {
        Task<List<FinancialPeriodView>> GetPeriodsAsync(int? year = null);

        Task<FinancialPeriodView?> GetPeriodAsync(int year, int month);

        /// <summary>
        /// Closes a month. Refuses to close one that is not the earliest open month with
        /// postings in it, because closing April while March is still open would let somebody
        /// change a figure that has already been reported.
        /// </summary>
        Task<FinancialPeriodView> CloseAsync(int year, int month, string notes);

        Task<FinancialPeriodView> ReopenAsync(int year, int month, string reason);

        /// <summary>Creates the twelve months of a year, all open, so they can be seen and closed.</summary>
        Task<List<FinancialPeriodView>> EnsureYearAsync(int year);
    }

    public class FinancialPeriodService : IFinancialPeriodService
    {
        private readonly TenantErpDbContext _context;
        private readonly ICurrentUserAccessor _actor;

        public FinancialPeriodService(TenantErpDbContext context, ICurrentUserAccessor actor)
        {
            _context = context;
            _actor = actor;
        }

        public async Task<List<FinancialPeriodView>> GetPeriodsAsync(int? year = null)
        {
            var target = year ?? DateTime.UtcNow.Year;

            await EnsureYearAsync(target);

            var periods = await _context.FinancialPeriods
                .AsNoTracking()
                .Where(p => p.Year == target)
                .OrderBy(p => p.Month)
                .ToListAsync();

            // What has been posted into each month, in one grouped query rather than twelve.
            var activity = await _context.JournalEntries
                .AsNoTracking()
                .Where(e => e.PeriodYear == target && e.Status == JournalStatuses.Posted)
                .GroupBy(e => e.PeriodMonth)
                .Select(g => new { Month = g.Key, Count = g.Count(), Total = g.Sum(e => e.TotalDebit) })
                .ToListAsync();

            return periods.Select(p =>
            {
                var view = ToView(p);
                var posted = activity.FirstOrDefault(a => a.Month == p.Month);

                view.EntryCount = posted?.Count ?? 0;
                view.TotalPosted = posted?.Total ?? 0m;

                return view;
            }).ToList();
        }

        public async Task<FinancialPeriodView?> GetPeriodAsync(int year, int month)
        {
            var period = await _context.FinancialPeriods
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Year == year && p.Month == month);

            return period is null ? null : ToView(period);
        }

        public async Task<FinancialPeriodView> CloseAsync(int year, int month, string notes)
        {
            ValidateMonth(month);

            await EnsureYearAsync(year);

            var period = await _context.FinancialPeriods
                .FirstOrDefaultAsync(p => p.Year == year && p.Month == month)
                ?? throw new ValidationException($"There is no period for {month:00}/{year}.");

            if (!period.IsOpen)
            {
                throw new ValidationException($"{period.DisplayName} is already closed.");
            }

            // Closing out of order would let somebody keep editing an earlier month that has
            // already been reported on, which defeats the point of closing anything.
            var earlierOpen = await _context.FinancialPeriods
                .AsNoTracking()
                .Where(p => p.Status == PeriodStatuses.Open &&
                            (p.Year < year || (p.Year == year && p.Month < month)))
                .OrderBy(p => p.Year).ThenBy(p => p.Month)
                .FirstOrDefaultAsync();

            if (earlierOpen is not null)
            {
                var hasPostings = await _context.JournalEntries.AnyAsync(e =>
                    e.PeriodYear == earlierOpen.Year && e.PeriodMonth == earlierOpen.Month &&
                    e.Status == JournalStatuses.Posted);

                if (hasPostings)
                {
                    throw new ValidationException(
                        $"{earlierOpen.DisplayName} is still open and has postings in it. " +
                        "Close the months in order, or those figures can still change after " +
                        $"{period.DisplayName} has been reported.");
                }
            }

            var actor = _actor.Current;

            period.Status = PeriodStatuses.Closed;
            period.ClosedAt = DateTime.UtcNow;
            period.ClosedByUserId = actor.AppUserId;
            period.ClosedBy = actor.Username ?? "";
            period.Notes = (notes ?? "").Trim();

            await _context.SaveChangesAsync();

            return ToView(period);
        }

        public async Task<FinancialPeriodView> ReopenAsync(int year, int month, string reason)
        {
            ValidateMonth(month);

            var period = await _context.FinancialPeriods
                .FirstOrDefaultAsync(p => p.Year == year && p.Month == month)
                ?? throw new ValidationException($"There is no period for {month:00}/{year}.");

            if (period.IsOpen)
            {
                throw new ValidationException($"{period.DisplayName} is already open.");
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ValidationException(
                    "Reopening a closed period changes figures that have already been reported. " +
                    "Say why, so the reason is on record.");
            }

            var actor = _actor.Current;

            period.Status = PeriodStatuses.Open;
            period.ClosedAt = null;
            period.ClosedByUserId = null;
            period.ClosedBy = "";
            period.Notes = $"Reopened by {actor.Username}: {reason.Trim()}";

            await _context.SaveChangesAsync();

            return ToView(period);
        }

        public async Task<List<FinancialPeriodView>> EnsureYearAsync(int year)
        {
            if (year < 2000 || year > 2100)
            {
                throw new ValidationException("The year must be between 2000 and 2100.");
            }

            var existing = await _context.FinancialPeriods
                .Where(p => p.Year == year)
                .Select(p => p.Month)
                .ToListAsync();

            var added = false;

            for (var month = 1; month <= 12; month++)
            {
                if (existing.Contains(month)) continue;

                var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);

                _context.FinancialPeriods.Add(new FinancialPeriod
                {
                    Year = year,
                    Month = month,
                    StartDate = start,
                    EndDate = start.AddMonths(1).AddDays(-1),
                    Status = PeriodStatuses.Open,
                    CreatedAt = DateTime.UtcNow
                });

                added = true;
            }

            if (added) await _context.SaveChangesAsync();

            var periods = await _context.FinancialPeriods
                .AsNoTracking()
                .Where(p => p.Year == year)
                .OrderBy(p => p.Month)
                .ToListAsync();

            return periods.Select(ToView).ToList();
        }

        private static void ValidateMonth(int month)
        {
            if (month is < 1 or > 12)
            {
                throw new ValidationException("The month must be between 1 and 12.");
            }
        }

        private static FinancialPeriodView ToView(FinancialPeriod p) => new()
        {
            FinancialPeriodId = p.FinancialPeriodId,
            Year = p.Year,
            Month = p.Month,
            DisplayName = p.DisplayName,
            StartDate = p.StartDate,
            EndDate = p.EndDate,
            Status = p.Status,
            ClosedAt = p.ClosedAt,
            ClosedBy = p.ClosedBy,
            Notes = p.Notes
        };
    }
}
