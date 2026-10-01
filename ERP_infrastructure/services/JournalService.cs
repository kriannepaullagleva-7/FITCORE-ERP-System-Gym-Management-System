using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    /// <summary>
    /// The ledger. Every rule that makes a journal entry valid lives here and nowhere else.
    ///
    /// Three of them matter enough to name:
    ///
    ///   - **An entry must balance.** Debits and credits are checked against each other before
    ///     anything is written, so an unbalanced entry never reaches the database and the trial
    ///     balance cannot be made to disagree with itself by ordinary use.
    ///   - **A posted entry is immutable.** Correcting one means reversing it. That keeps the
    ///     ledger a record of what happened rather than of what somebody currently believes.
    ///   - **A closed period refuses new postings.** Which is the point of closing one.
    /// </summary>
    public class JournalService : IJournalService
    {
        private readonly TenantErpDbContext _context;
        private readonly ICurrentUserAccessor _actor;

        public JournalService(TenantErpDbContext context, ICurrentUserAccessor actor)
        {
            _context = context;
            _actor = actor;
        }

        // ------------------------------------------------------------------ reading

        public async Task<List<JournalEntryView>> GetEntriesAsync(
            DateTime? fromUtc = null,
            DateTime? toUtc = null,
            string? source = null,
            string? status = null,
            int? accountId = null,
            int take = 500)
        {
            var query = _context.JournalEntries
                .AsNoTracking()
                .Include(e => e.Lines).ThenInclude(l => l.Account)
                .AsQueryable();

            if (fromUtc.HasValue) query = query.Where(e => e.EntryDate >= fromUtc.Value);
            if (toUtc.HasValue) query = query.Where(e => e.EntryDate < toUtc.Value);

            if (!string.IsNullOrWhiteSpace(source))
            {
                query = query.Where(e => e.Source == source);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(e => e.Status == status);
            }

            if (accountId.HasValue)
            {
                query = query.Where(e => e.Lines.Any(l => l.AccountId == accountId.Value));
            }

            var rows = await query
                .OrderByDescending(e => e.EntryDate)
                .ThenByDescending(e => e.JournalEntryId)
                .Take(Math.Clamp(take, 1, 5000))
                .ToListAsync();

            return rows.Select(ToView).ToList();
        }

        public async Task<JournalEntryView?> GetEntryAsync(int journalEntryId)
        {
            var entry = await _context.JournalEntries
                .AsNoTracking()
                .Include(e => e.Lines).ThenInclude(l => l.Account)
                .FirstOrDefaultAsync(e => e.JournalEntryId == journalEntryId);

            return entry is null ? null : ToView(entry);
        }

        public async Task<JournalEntryView?> GetEntryForSourceAsync(string entityName, string entityId)
        {
            var entry = await _context.JournalEntries
                .AsNoTracking()
                .Include(e => e.Lines).ThenInclude(l => l.Account)
                .FirstOrDefaultAsync(e =>
                    e.SourceEntityName == entityName &&
                    e.SourceEntityId == entityId &&
                    e.Status != JournalStatuses.Void);

            return entry is null ? null : ToView(entry);
        }

        // ------------------------------------------------------------------ writing

        public async Task<JournalEntryView> CreateEntryAsync(
            DateTime entryDate,
            string memo,
            IEnumerable<JournalLineRequest> lines,
            string source = JournalSources.Manual,
            string sourceModule = "",
            string sourceEntityName = "",
            string? sourceEntityId = null,
            string reference = "",
            bool post = true)
        {
            var requested = (lines ?? Enumerable.Empty<JournalLineRequest>()).ToList();

            var date = entryDate == default ? DateTime.UtcNow : entryDate;

            var prepared = await ValidateLinesAsync(requested);

            var totalDebit = prepared.Sum(l => l.Debit);
            var totalCredit = prepared.Sum(l => l.Credit);

            if (totalDebit != totalCredit)
            {
                throw new ValidationException(
                    $"The entry does not balance: debits total {totalDebit:N2} and credits " +
                    $"total {totalCredit:N2}. Every journal entry must have equal debits and credits.");
            }

            if (totalDebit <= 0m)
            {
                throw new ValidationException("A journal entry must move a non-zero amount.");
            }

            if (post) await EnsurePeriodIsOpenAsync(date);

            var actor = _actor.Current;

            var entry = new JournalEntry
            {
                EntryNo = await NextEntryNumberAsync(),
                EntryDate = date,
                Reference = Clean(reference),
                Memo = Clean(memo),
                Source = JournalSources.All.FirstOrDefault(s =>
                    string.Equals(s, source, StringComparison.OrdinalIgnoreCase)) ?? JournalSources.Manual,
                SourceModule = Clean(sourceModule),
                SourceEntityName = Clean(sourceEntityName),
                SourceEntityId = string.IsNullOrWhiteSpace(sourceEntityId) ? null : sourceEntityId.Trim(),
                Status = post ? JournalStatuses.Posted : JournalStatuses.Draft,
                PostedAt = post ? DateTime.UtcNow : null,
                TotalDebit = totalDebit,
                TotalCredit = totalCredit,
                PeriodYear = date.Year,
                PeriodMonth = date.Month,
                ProcessedByUserId = actor.AppUserId,
                ProcessedBy = actor.Username ?? "",
                CreatedAt = DateTime.UtcNow,
                Lines = prepared
            };

            _context.JournalEntries.Add(entry);
            await _context.SaveChangesAsync();

            return (await GetEntryAsync(entry.JournalEntryId))!;
        }

        public async Task<JournalEntryView?> PostEntryAsync(int journalEntryId)
        {
            var entry = await _context.JournalEntries
                .Include(e => e.Lines)
                .FirstOrDefaultAsync(e => e.JournalEntryId == journalEntryId);

            if (entry is null) return null;

            if (!string.Equals(entry.Status, JournalStatuses.Draft, StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    $"Entry {entry.EntryNo} is {entry.Status.ToLowerInvariant()} and cannot be posted again.");
            }

            if (entry.TotalDebit != entry.TotalCredit)
            {
                throw new ValidationException(
                    $"Entry {entry.EntryNo} does not balance and cannot be posted.");
            }

            await EnsurePeriodIsOpenAsync(entry.EntryDate);

            entry.Status = JournalStatuses.Posted;
            entry.PostedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return await GetEntryAsync(journalEntryId);
        }

        public async Task<JournalEntryView> ReverseEntryAsync(int journalEntryId, string reason)
        {
            var original = await _context.JournalEntries
                .Include(e => e.Lines)
                .FirstOrDefaultAsync(e => e.JournalEntryId == journalEntryId)
                ?? throw new ValidationException($"No journal entry with id {journalEntryId} exists.");

            if (!JournalStatuses.AffectsBalances(original.Status))
            {
                throw new ValidationException(
                    $"Entry {original.EntryNo} is {original.Status.ToLowerInvariant()}, so there is " +
                    "nothing in the ledger to reverse.");
            }

            if (original.ReversedByEntryId.HasValue)
            {
                throw new ValidationException($"Entry {original.EntryNo} has already been reversed.");
            }

            var today = DateTime.UtcNow;

            // The reversal is dated today rather than on the original's date. Back-dating it
            // would silently change a period that may already have been reported, which is
            // exactly what reversing rather than editing exists to avoid. If the original's
            // period is still open the two net out there anyway.
            var date = await IsPeriodOpenAsync(original.EntryDate) ? original.EntryDate : today;

            var actor = _actor.Current;

            var mirrored = original.Lines
                .OrderBy(l => l.LineNumber)
                .Select((l, index) => new JournalEntryLine
                {
                    LineNumber = index + 1,
                    AccountId = l.AccountId,
                    Description = l.Description,

                    // The whole trick: the two sides swap.
                    Debit = l.Credit,
                    Credit = l.Debit,

                    MemberId = l.MemberId,
                    SupplierId = l.SupplierId,
                    EmployeeId = l.EmployeeId
                })
                .ToList();

            var reversal = new JournalEntry
            {
                EntryNo = await NextEntryNumberAsync(),
                EntryDate = date,
                Reference = original.Reference,
                Memo = $"Reversal of {original.EntryNo}" +
                       (string.IsNullOrWhiteSpace(reason) ? "" : $" - {Clean(reason)}"),
                Source = JournalSources.Reversal,
                SourceModule = original.SourceModule,
                SourceEntityName = original.SourceEntityName,

                // Deliberately not carrying the source id across. It is what makes posting
                // idempotent, and a reversal sharing it would make the original look already
                // reversed to a later lookup - or worse, look already posted.
                SourceEntityId = null,

                Status = JournalStatuses.Posted,
                PostedAt = today,
                TotalDebit = original.TotalCredit,
                TotalCredit = original.TotalDebit,
                PeriodYear = date.Year,
                PeriodMonth = date.Month,
                ReversesEntryId = original.JournalEntryId,
                ProcessedByUserId = actor.AppUserId,
                ProcessedBy = actor.Username ?? "",
                CreatedAt = today,
                Lines = mirrored
            };

            _context.JournalEntries.Add(reversal);
            await _context.SaveChangesAsync();

            // The original stays Posted. It is still in the ledger - that is the whole
            // difference between reversing an entry and deleting one - and the two entries
            // net to zero between them. Marking it as no longer counting would leave the
            // reversal standing alone and put every account it touched out by the original
            // amount, in the opposite direction.
            original.ReversedByEntryId = reversal.JournalEntryId;
            await _context.SaveChangesAsync();

            return (await GetEntryAsync(reversal.JournalEntryId))!;
        }

        public async Task<JournalEntryView?> UpdateDraftAsync(
            int journalEntryId,
            DateTime entryDate,
            string memo,
            string reference,
            IEnumerable<JournalLineRequest> lines)
        {
            var entry = await _context.JournalEntries
                .Include(e => e.Lines)
                .FirstOrDefaultAsync(e => e.JournalEntryId == journalEntryId);

            if (entry is null) return null;

            if (!string.Equals(entry.Status, JournalStatuses.Draft, StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    $"Entry {entry.EntryNo} has been posted. A posted entry is never edited - " +
                    "reverse it and write the correction, so both stay on record.");
            }

            if (!JournalSources.IsEditable(entry.Source))
            {
                throw new ValidationException(
                    $"Entry {entry.EntryNo} was raised automatically by {entry.SourceModule}. " +
                    "Correct it in the module that produced it rather than in the ledger.");
            }

            var prepared = await ValidateLinesAsync(
                (lines ?? Enumerable.Empty<JournalLineRequest>()).ToList());

            var totalDebit = prepared.Sum(l => l.Debit);
            var totalCredit = prepared.Sum(l => l.Credit);

            if (totalDebit != totalCredit)
            {
                throw new ValidationException(
                    $"The entry does not balance: debits total {totalDebit:N2} and credits " +
                    $"total {totalCredit:N2}.");
            }

            _context.JournalEntryLines.RemoveRange(entry.Lines);

            var date = entryDate == default ? entry.EntryDate : entryDate;

            entry.EntryDate = date;
            entry.PeriodYear = date.Year;
            entry.PeriodMonth = date.Month;
            entry.Memo = Clean(memo);
            entry.Reference = Clean(reference);
            entry.TotalDebit = totalDebit;
            entry.TotalCredit = totalCredit;
            entry.Lines = prepared;

            await _context.SaveChangesAsync();

            return await GetEntryAsync(journalEntryId);
        }

        public async Task<bool> VoidDraftAsync(int journalEntryId)
        {
            var entry = await _context.JournalEntries
                .FirstOrDefaultAsync(e => e.JournalEntryId == journalEntryId);

            if (entry is null) return false;

            if (!string.Equals(entry.Status, JournalStatuses.Draft, StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    $"Entry {entry.EntryNo} has been posted and cannot be discarded. Reverse it instead.");
            }

            entry.Status = JournalStatuses.Void;
            await _context.SaveChangesAsync();

            return true;
        }

        // ------------------------------------------------------------------ ledger views

        public async Task<GeneralLedgerView?> GetLedgerAsync(
            int accountId, DateTime? fromUtc, DateTime? toUtc)
        {
            var account = await _context.Accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.AccountId == accountId);

            if (account is null) return null;

            var range = ReportRange.Resolve(fromUtc, toUtc);

            // The opening balance is everything posted before the window. For a revenue or
            // expense account it is shown too rather than forced to zero: an operator looking
            // at March wants to know what February left behind, and suppressing it would make
            // the running balance start from nowhere.
            var opening = await _context.JournalEntryLines
                .AsNoTracking()
                .Where(l => l.AccountId == accountId &&
                            l.Entry.Status == JournalStatuses.Posted &&
                            l.Entry.EntryDate < range.FromUtc)
                .GroupBy(_ => 1)
                .Select(g => new { Debit = g.Sum(l => l.Debit), Credit = g.Sum(l => l.Credit) })
                .FirstOrDefaultAsync();

            var openingBalance = AccountTypes.BalanceOf(
                account.AccountType, opening?.Debit ?? 0m, opening?.Credit ?? 0m);

            var lines = await _context.JournalEntryLines
                .AsNoTracking()
                .Where(l => l.AccountId == accountId &&
                            l.Entry.Status == JournalStatuses.Posted &&
                            l.Entry.EntryDate >= range.FromUtc &&
                            l.Entry.EntryDate < range.ToUtc)
                .OrderBy(l => l.Entry.EntryDate)
                .ThenBy(l => l.JournalEntryId)
                .ThenBy(l => l.LineNumber)
                .Select(l => new
                {
                    l.JournalEntryId,
                    l.Entry.EntryNo,
                    l.Entry.EntryDate,
                    l.Entry.Reference,
                    l.Entry.Source,
                    l.Entry.Memo,
                    l.Description,
                    l.Debit,
                    l.Credit
                })
                .ToListAsync();

            var view = new GeneralLedgerView
            {
                AccountId = account.AccountId,
                AccountCode = account.AccountCode,
                AccountName = account.AccountName,
                AccountType = account.AccountType,
                FromUtc = range.FromUtc,
                ToUtc = range.ToUtc,
                OpeningBalance = openingBalance
            };

            var running = openingBalance;
            var debitNormal = AccountTypes.IsDebitNormal(account.AccountType);

            foreach (var line in lines)
            {
                running += debitNormal ? line.Debit - line.Credit : line.Credit - line.Debit;

                view.Rows.Add(new LedgerRow
                {
                    JournalEntryId = line.JournalEntryId,
                    EntryNo = line.EntryNo,
                    EntryDate = line.EntryDate,
                    Reference = line.Reference,
                    Description = string.IsNullOrWhiteSpace(line.Description) ? line.Memo : line.Description,
                    Source = line.Source,
                    Debit = line.Debit,
                    Credit = line.Credit,
                    Balance = running
                });
            }

            view.TotalDebit = lines.Sum(l => l.Debit);
            view.TotalCredit = lines.Sum(l => l.Credit);
            view.ClosingBalance = running;

            return view;
        }

        public async Task<TrialBalanceView> GetTrialBalanceAsync(DateTime? asOfUtc = null)
        {
            var asOf = (asOfUtc?.Date ?? DateTime.UtcNow.Date).AddDays(1);

            var totals = await _context.JournalEntryLines
                .AsNoTracking()
                .Where(l => l.Entry.Status == JournalStatuses.Posted && l.Entry.EntryDate < asOf)
                .GroupBy(l => l.AccountId)
                .Select(g => new
                {
                    AccountId = g.Key,
                    Debit = g.Sum(l => l.Debit),
                    Credit = g.Sum(l => l.Credit)
                })
                .ToListAsync();

            var accounts = await _context.Accounts
                .AsNoTracking()
                .OrderBy(a => a.AccountCode)
                .Select(a => new { a.AccountId, a.AccountCode, a.AccountName, a.AccountType })
                .ToListAsync();

            var byAccount = totals.ToDictionary(t => t.AccountId);

            var view = new TrialBalanceView { AsOfUtc = asOf.AddDays(-1) };

            foreach (var account in accounts)
            {
                if (!byAccount.TryGetValue(account.AccountId, out var total)) continue;

                var balance = AccountTypes.BalanceOf(account.AccountType, total.Debit, total.Credit);
                if (balance == 0m) continue;

                // A trial balance shows one column per account, on whichever side the balance
                // actually falls - which for a contra-revenue account is the debit side even
                // though it is a revenue account.
                var onDebitSide = AccountTypes.IsDebitNormal(account.AccountType)
                    ? balance > 0m
                    : balance < 0m;

                var magnitude = Math.Abs(balance);

                view.Rows.Add(new TrialBalanceRow
                {
                    AccountId = account.AccountId,
                    AccountCode = account.AccountCode,
                    AccountName = account.AccountName,
                    AccountType = account.AccountType,
                    Debit = onDebitSide ? magnitude : 0m,
                    Credit = onDebitSide ? 0m : magnitude
                });
            }

            view.TotalDebit = view.Rows.Sum(r => r.Debit);
            view.TotalCredit = view.Rows.Sum(r => r.Credit);

            return view;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Checks every line and turns it into the entity, rounding the amounts on the way in.
        ///
        /// Rounding here rather than at the call site is what keeps an entry balanced: two
        /// amounts that agree to four places can disagree to two, and a half-centavo difference
        /// is enough to make a trial balance wrong forever.
        /// </summary>
        private async Task<List<JournalEntryLine>> ValidateLinesAsync(List<JournalLineRequest> requested)
        {
            if (requested.Count < 2)
            {
                throw new ValidationException(
                    "A journal entry needs at least two lines - something debited and something credited.");
            }

            var accountIds = requested.Select(l => l.AccountId).Distinct().ToList();

            var accounts = await _context.Accounts
                .AsNoTracking()
                .Where(a => accountIds.Contains(a.AccountId))
                .ToDictionaryAsync(a => a.AccountId);

            var prepared = new List<JournalEntryLine>();
            var lineNumber = 1;

            foreach (var line in requested)
            {
                if (!accounts.TryGetValue(line.AccountId, out var account))
                {
                    throw new ValidationException($"No account with id {line.AccountId} exists.");
                }

                if (!account.IsActive)
                {
                    throw new ValidationException(
                        $"Account {account.AccountCode} {account.AccountName} is inactive and " +
                        "cannot be posted to.");
                }

                var debit = Math.Round(Math.Max(0m, line.Debit), 2, MidpointRounding.AwayFromZero);
                var credit = Math.Round(Math.Max(0m, line.Credit), 2, MidpointRounding.AwayFromZero);

                if (debit > 0m && credit > 0m)
                {
                    throw new ValidationException(
                        $"Line {lineNumber} debits and credits the same account at once. " +
                        "A line is one or the other.");
                }

                if (debit == 0m && credit == 0m) continue;   // an empty row the operator left behind

                prepared.Add(new JournalEntryLine
                {
                    LineNumber = lineNumber++,
                    AccountId = line.AccountId,
                    Description = Clean(line.Description),
                    Debit = debit,
                    Credit = credit,
                    MemberId = line.MemberId,
                    SupplierId = line.SupplierId,
                    EmployeeId = line.EmployeeId
                });
            }

            if (prepared.Count < 2)
            {
                throw new ValidationException(
                    "A journal entry needs at least two lines with an amount on them.");
            }

            return prepared;
        }

        private async Task<bool> IsPeriodOpenAsync(DateTime date)
        {
            var period = await _context.FinancialPeriods
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Year == date.Year && p.Month == date.Month);

            // No row means the period was never closed. A tenant that does not use period
            // closing is therefore unaffected rather than locked out of its own ledger.
            return period is null || period.IsOpen;
        }

        private async Task EnsurePeriodIsOpenAsync(DateTime date)
        {
            if (await IsPeriodOpenAsync(date)) return;

            throw new ValidationException(
                $"{date:MMMM yyyy} has been closed and will not accept new postings. " +
                "Reopen the period, or date the entry into an open one.");
        }

        /// <summary>
        /// The next running number.
        ///
        /// Derived from the highest existing number rather than a counter row, so restoring a
        /// database or importing history cannot produce a collision the unique index then
        /// refuses. Two simultaneous entries would still race; the unique index catches that,
        /// and a journal is not a high-concurrency table.
        /// </summary>
        private async Task<string> NextEntryNumberAsync()
        {
            var last = await _context.JournalEntries
                .AsNoTracking()
                .OrderByDescending(e => e.JournalEntryId)
                .Select(e => e.EntryNo)
                .FirstOrDefaultAsync();

            var next = 1;

            if (!string.IsNullOrWhiteSpace(last) &&
                int.TryParse(last.Replace("JE-", "", StringComparison.OrdinalIgnoreCase), out var parsed))
            {
                next = parsed + 1;
            }
            else
            {
                next = await _context.JournalEntries.CountAsync() + 1;
            }

            return $"JE-{next:D6}";
        }

        private static JournalEntryView ToView(JournalEntry entry) => new()
        {
            JournalEntryId = entry.JournalEntryId,
            EntryNo = entry.EntryNo,
            EntryDate = entry.EntryDate,
            Reference = entry.Reference,
            Memo = entry.Memo,
            Source = entry.Source,
            SourceModule = entry.SourceModule,
            SourceEntityName = entry.SourceEntityName,
            SourceEntityId = entry.SourceEntityId,
            Status = entry.Status,
            TotalDebit = entry.TotalDebit,
            TotalCredit = entry.TotalCredit,
            PeriodYear = entry.PeriodYear,
            PeriodMonth = entry.PeriodMonth,
            ProcessedBy = entry.ProcessedBy,
            ReversedByEntryId = entry.ReversedByEntryId,
            ReversesEntryId = entry.ReversesEntryId,
            CreatedAt = entry.CreatedAt,

            IsEditable = JournalSources.IsEditable(entry.Source) &&
                         string.Equals(entry.Status, JournalStatuses.Draft, StringComparison.OrdinalIgnoreCase),

            Summary = BuildSummary(entry),

            Lines = entry.Lines
                .OrderBy(l => l.LineNumber)
                .Select(l => new JournalLineView
                {
                    JournalEntryLineId = l.JournalEntryLineId,
                    LineNumber = l.LineNumber,
                    AccountId = l.AccountId,
                    AccountCode = l.Account?.AccountCode ?? "",
                    AccountName = l.Account?.AccountName ?? "",
                    AccountType = l.Account?.AccountType ?? "",
                    Description = l.Description,
                    Debit = l.Debit,
                    Credit = l.Credit,
                    MemberId = l.MemberId,
                    SupplierId = l.SupplierId,
                    EmployeeId = l.EmployeeId
                })
                .ToList()
        };

        /// <summary>
        /// "Cash on Hand → Product Sales", so a ledger list reads as what moved where rather
        /// than as a row of identical memos.
        /// </summary>
        private static string BuildSummary(JournalEntry entry)
        {
            var debits = entry.Lines
                .Where(l => l.Debit > 0m)
                .Select(l => l.Account?.AccountName ?? "")
                .Where(n => n.Length > 0)
                .Distinct()
                .ToList();

            var credits = entry.Lines
                .Where(l => l.Credit > 0m)
                .Select(l => l.Account?.AccountName ?? "")
                .Where(n => n.Length > 0)
                .Distinct()
                .ToList();

            if (debits.Count == 0 && credits.Count == 0) return entry.Memo;

            static string Join(List<string> names) =>
                names.Count <= 2 ? string.Join(", ", names) : $"{names[0]} +{names.Count - 1} more";

            return $"{Join(debits)} → {Join(credits)}";
        }

        private static string Clean(string? value) => (value ?? "").Trim();
    }
}
