using ERP_api.DTOs;
using ERP_api.Infrastructure;
using ERP_domain.entities;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP_api.Controllers
{
    /// <summary>
    /// Finance Management: the chart of accounts, the ledger, the statements and everything
    /// built on them.
    ///
    /// One controller for the module rather than eight, because these are not eight resources -
    /// they are one set of books read eight ways, and splitting them would mean eight
    /// controllers all needing the same tier and role rules. Each action carries its own
    /// subfeature rule, so what a caller may reach inside Finance is still decided one screen
    /// at a time.
    ///
    /// Every route is tenant-scoped and carries no company id: the books belong to whichever
    /// tenant the signed token names.
    /// </summary>
    [ApiController]
    [Route("api/finance")]
    [Produces("application/json")]
    [Authorize]
    [RequireModule(ErpModules.Finance)]
    public class FinanceController : ControllerBase
    {
        private readonly IAccountService _accounts;
        private readonly IJournalService _journal;
        private readonly IFinanceReportService _reports;
        private readonly IFinancialPeriodService _periods;
        private readonly IBudgetService _budgets;
        private readonly IBankingService _banking;
        private readonly IFinancePostingService _posting;

        public FinanceController(
            IAccountService accounts,
            IJournalService journal,
            IFinanceReportService reports,
            IFinancialPeriodService periods,
            IBudgetService budgets,
            IBankingService banking,
            IFinancePostingService posting)
        {
            _accounts = accounts;
            _journal = journal;
            _reports = reports;
            _periods = periods;
            _budgets = budgets;
            _banking = banking;
            _posting = posting;
        }

        // ================================================================== overview

        [HttpGet("overview")]
        [RequireSubmodule(ErpModules.Sub.FinanceOverview)]
        [ProducesResponseType(typeof(FinanceOverview), StatusCodes.Status200OK)]
        public async Task<ActionResult<FinanceOverview>> GetOverview(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            var overview = await _reports.GetOverviewAsync(from, to);

            // How much has happened that the ledger has not caught up with. Shown on the
            // overview rather than discovered when a statement looks wrong.
            overview.UnpostedCount = await _posting.CountOutstandingAsync();

            return Ok(overview);
        }

        /// <summary>
        /// Posts everything that completed while automatic posting was off, or that failed at
        /// the time. Safe to run repeatedly - anything already posted is skipped.
        /// </summary>
        [HttpPost("post-outstanding")]
        [RequireSubmodule(ErpModules.Sub.JournalEntries)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<object>> PostOutstanding([FromQuery] DateTime? from)
        {
            var posted = await _posting.PostOutstandingAsync(from);

            return Ok(new
            {
                Posted = posted,
                Remaining = await _posting.CountOutstandingAsync(from)
            });
        }

        // ================================================================== chart of accounts

        [HttpGet("accounts")]
        [RequireSubmodule(ErpModules.Sub.ChartOfAccounts)]
        [ProducesResponseType(typeof(IEnumerable<AccountView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<AccountView>>> GetAccounts(
            [FromQuery] string? accountType,
            [FromQuery] bool includeInactive = true,
            [FromQuery] bool withBalances = true)
        {
            return Ok(await _accounts.GetAccountsAsync(accountType, includeInactive, withBalances));
        }

        [HttpGet("accounts/types")]
        [RequireSubmodule(ErpModules.Sub.ChartOfAccounts)]
        [ProducesResponseType(typeof(IEnumerable<string>), StatusCodes.Status200OK)]
        public ActionResult<IEnumerable<string>> GetAccountTypes() => Ok(AccountTypes.All);

        [HttpGet("accounts/{id:int}")]
        [RequireSubmodule(ErpModules.Sub.ChartOfAccounts)]
        [ProducesResponseType(typeof(AccountView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<AccountView>> GetAccount(int id)
        {
            var account = await _accounts.GetAccountAsync(id);
            return account is null ? NotFound() : Ok(account);
        }

        [HttpPost("accounts")]
        [RequireSubmodule(ErpModules.Sub.ChartOfAccounts)]
        [ProducesResponseType(typeof(AccountView), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<AccountView>> CreateAccount([FromBody] CreateAccountDto dto)
        {
            var account = await _accounts.CreateAccountAsync(
                dto.AccountCode, dto.AccountName, dto.AccountType,
                dto.AccountSubType, dto.Description, dto.ParentAccountId);

            return CreatedAtAction(nameof(GetAccount), new { id = account.AccountId }, account);
        }

        [HttpPut("accounts/{id:int}")]
        [RequireSubmodule(ErpModules.Sub.ChartOfAccounts)]
        [ProducesResponseType(typeof(AccountView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<AccountView>> UpdateAccount(
            int id, [FromBody] UpdateAccountDto dto)
        {
            var account = await _accounts.UpdateAccountAsync(
                id, dto.AccountCode, dto.AccountName, dto.AccountType,
                dto.AccountSubType, dto.Description, dto.ParentAccountId, dto.IsActive);

            return account is null ? NotFound() : Ok(account);
        }

        [HttpDelete("accounts/{id:int}")]
        [RequireSubmodule(ErpModules.Sub.ChartOfAccounts)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteAccount(int id)
        {
            var deleted = await _accounts.DeleteAccountAsync(id);
            return deleted ? NoContent() : NotFound();
        }

        // ================================================================== journal

        [HttpGet("journal")]
        [RequireSubmodule(ErpModules.Sub.JournalEntries)]
        [ProducesResponseType(typeof(IEnumerable<JournalEntryView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<JournalEntryView>>> GetJournal(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string? source,
            [FromQuery] string? status,
            [FromQuery] int? accountId,
            [FromQuery] int take = 500)
        {
            return Ok(await _journal.GetEntriesAsync(from, to, source, status, accountId, take));
        }

        [HttpGet("journal/sources")]
        [RequireSubmodule(ErpModules.Sub.JournalEntries)]
        [ProducesResponseType(typeof(IEnumerable<string>), StatusCodes.Status200OK)]
        public ActionResult<IEnumerable<string>> GetJournalSources() => Ok(JournalSources.All);

        [HttpGet("journal/{id:int}")]
        [RequireSubmodule(ErpModules.Sub.JournalEntries)]
        [ProducesResponseType(typeof(JournalEntryView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<JournalEntryView>> GetJournalEntry(int id)
        {
            var entry = await _journal.GetEntryAsync(id);
            return entry is null ? NotFound() : Ok(entry);
        }

        [HttpPost("journal")]
        [RequireSubmodule(ErpModules.Sub.JournalEntries)]
        [ProducesResponseType(typeof(JournalEntryView), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<JournalEntryView>> CreateJournalEntry(
            [FromBody] CreateJournalEntryDto dto)
        {
            var entry = await _journal.CreateEntryAsync(
                dto.EntryDate,
                dto.Memo,
                dto.Lines.Select(ToRequest),
                JournalSources.Manual,
                ErpModules.Finance,
                reference: dto.Reference,
                post: dto.Post);

            return CreatedAtAction(nameof(GetJournalEntry), new { id = entry.JournalEntryId }, entry);
        }

        [HttpPut("journal/{id:int}")]
        [RequireSubmodule(ErpModules.Sub.JournalEntries)]
        [ProducesResponseType(typeof(JournalEntryView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<JournalEntryView>> UpdateJournalEntry(
            int id, [FromBody] UpdateJournalEntryDto dto)
        {
            var entry = await _journal.UpdateDraftAsync(
                id, dto.EntryDate, dto.Memo, dto.Reference, dto.Lines.Select(ToRequest));

            return entry is null ? NotFound() : Ok(entry);
        }

        [HttpPost("journal/{id:int}/post")]
        [RequireSubmodule(ErpModules.Sub.JournalEntries)]
        [ProducesResponseType(typeof(JournalEntryView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<JournalEntryView>> PostJournalEntry(int id)
        {
            var entry = await _journal.PostEntryAsync(id);
            return entry is null ? NotFound() : Ok(entry);
        }

        /// <summary>
        /// Undoes a posted entry by writing its mirror image. The original is never edited or
        /// deleted, so the ledger shows both what was believed and the correction.
        /// </summary>
        [HttpPost("journal/{id:int}/reverse")]
        [RequireSubmodule(ErpModules.Sub.JournalEntries)]
        [ProducesResponseType(typeof(JournalEntryView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<JournalEntryView>> ReverseJournalEntry(
            int id, [FromBody] ReverseJournalEntryDto dto)
        {
            return Ok(await _journal.ReverseEntryAsync(id, dto.Reason));
        }

        [HttpDelete("journal/{id:int}")]
        [RequireSubmodule(ErpModules.Sub.JournalEntries)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> VoidJournalDraft(int id)
        {
            var voided = await _journal.VoidDraftAsync(id);
            return voided ? NoContent() : NotFound();
        }

        // ================================================================== ledger

        [HttpGet("ledger/{accountId:int}")]
        [RequireSubmodule(ErpModules.Sub.GeneralLedger)]
        [ProducesResponseType(typeof(GeneralLedgerView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<GeneralLedgerView>> GetLedger(
            int accountId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            var ledger = await _journal.GetLedgerAsync(accountId, from, to);
            return ledger is null ? NotFound() : Ok(ledger);
        }

        // ================================================================== statements

        [HttpGet("reports/trial-balance")]
        [RequireSubmodule(ErpModules.Sub.FinancialReports)]
        [ProducesResponseType(typeof(TrialBalanceView), StatusCodes.Status200OK)]
        public async Task<ActionResult<TrialBalanceView>> GetTrialBalance([FromQuery] DateTime? asOf)
        {
            return Ok(await _journal.GetTrialBalanceAsync(asOf));
        }

        [HttpGet("reports/income-statement")]
        [RequireSubmodule(ErpModules.Sub.FinancialReports)]
        [ProducesResponseType(typeof(IncomeStatementView), StatusCodes.Status200OK)]
        public async Task<ActionResult<IncomeStatementView>> GetIncomeStatement(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            return Ok(await _reports.GetIncomeStatementAsync(from, to));
        }

        [HttpGet("reports/balance-sheet")]
        [RequireSubmodule(ErpModules.Sub.FinancialReports)]
        [ProducesResponseType(typeof(BalanceSheetView), StatusCodes.Status200OK)]
        public async Task<ActionResult<BalanceSheetView>> GetBalanceSheet([FromQuery] DateTime? asOf)
        {
            return Ok(await _reports.GetBalanceSheetAsync(asOf));
        }

        [HttpGet("reports/cash-flow")]
        [RequireSubmodule(ErpModules.Sub.FinancialReports)]
        [ProducesResponseType(typeof(CashFlowView), StatusCodes.Status200OK)]
        public async Task<ActionResult<CashFlowView>> GetCashFlow(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            return Ok(await _reports.GetCashFlowAsync(from, to));
        }

        [HttpGet("receivables")]
        [RequireSubmodule(ErpModules.Sub.AccountsReceivable)]
        [ProducesResponseType(typeof(AgedBalanceView), StatusCodes.Status200OK)]
        public async Task<ActionResult<AgedBalanceView>> GetReceivables([FromQuery] DateTime? asOf)
        {
            return Ok(await _reports.GetReceivablesAsync(asOf));
        }

        [HttpGet("payables")]
        [RequireSubmodule(ErpModules.Sub.AccountsPayable)]
        [ProducesResponseType(typeof(AgedBalanceView), StatusCodes.Status200OK)]
        public async Task<ActionResult<AgedBalanceView>> GetPayables([FromQuery] DateTime? asOf)
        {
            return Ok(await _reports.GetPayablesAsync(asOf));
        }

        // ================================================================== periods

        [HttpGet("periods")]
        [RequireSubmodule(ErpModules.Sub.FinancialPeriods)]
        [ProducesResponseType(typeof(IEnumerable<FinancialPeriodView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<FinancialPeriodView>>> GetPeriods(
            [FromQuery] int? year)
        {
            return Ok(await _periods.GetPeriodsAsync(year));
        }

        [HttpPost("periods/{year:int}/{month:int}/close")]
        [RequireSubmodule(ErpModules.Sub.FinancialPeriods)]
        [ProducesResponseType(typeof(FinancialPeriodView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<FinancialPeriodView>> ClosePeriod(
            int year, int month, [FromBody] ClosePeriodDto dto)
        {
            return Ok(await _periods.CloseAsync(year, month, dto.Notes));
        }

        [HttpPost("periods/{year:int}/{month:int}/reopen")]
        [RequireSubmodule(ErpModules.Sub.FinancialPeriods)]
        [ProducesResponseType(typeof(FinancialPeriodView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<FinancialPeriodView>> ReopenPeriod(
            int year, int month, [FromBody] ReopenPeriodDto dto)
        {
            return Ok(await _periods.ReopenAsync(year, month, dto.Reason));
        }

        // ================================================================== budgets

        [HttpGet("budgets")]
        [RequireSubmodule(ErpModules.Sub.Budgets)]
        [ProducesResponseType(typeof(IEnumerable<BudgetView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<BudgetView>>> GetBudgets([FromQuery] int? year)
        {
            return Ok(await _budgets.GetBudgetsAsync(year));
        }

        [HttpGet("budgets/{id:int}")]
        [RequireSubmodule(ErpModules.Sub.Budgets)]
        [ProducesResponseType(typeof(BudgetView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<BudgetView>> GetBudget(int id)
        {
            var budget = await _budgets.GetBudgetAsync(id);
            return budget is null ? NotFound() : Ok(budget);
        }

        [HttpGet("budgets/{id:int}/variance")]
        [RequireSubmodule(ErpModules.Sub.Budgets)]
        [ProducesResponseType(typeof(BudgetVarianceView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<BudgetVarianceView>> GetBudgetVariance(
            int id, [FromQuery] int? month)
        {
            var variance = await _reports.GetBudgetVarianceAsync(id, month);
            return variance is null ? NotFound() : Ok(variance);
        }

        [HttpPost("budgets")]
        [RequireSubmodule(ErpModules.Sub.Budgets)]
        [ProducesResponseType(typeof(BudgetView), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<BudgetView>> CreateBudget([FromBody] CreateBudgetDto dto)
        {
            var budget = await _budgets.CreateBudgetAsync(dto.Name, dto.Year, dto.Notes);
            return CreatedAtAction(nameof(GetBudget), new { id = budget.BudgetId }, budget);
        }

        [HttpPut("budgets/{id:int}")]
        [RequireSubmodule(ErpModules.Sub.Budgets)]
        [ProducesResponseType(typeof(BudgetView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<BudgetView>> UpdateBudget(
            int id, [FromBody] UpdateBudgetDto dto)
        {
            var budget = await _budgets.UpdateBudgetAsync(id, dto.Name, dto.Year, dto.Notes);
            return budget is null ? NotFound() : Ok(budget);
        }

        [HttpPost("budgets/{id:int}/approve")]
        [RequireSubmodule(ErpModules.Sub.Budgets)]
        [ProducesResponseType(typeof(BudgetView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<BudgetView>> ApproveBudget(int id)
        {
            var budget = await _budgets.ApproveAsync(id);
            return budget is null ? NotFound() : Ok(budget);
        }

        [HttpPut("budgets/{id:int}/lines")]
        [RequireSubmodule(ErpModules.Sub.Budgets)]
        [ProducesResponseType(typeof(BudgetView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<BudgetView>> SetBudgetLine(
            int id, [FromBody] BudgetLineDto dto)
        {
            var budget = await _budgets.SetLineAsync(id, dto.AccountId, dto.Month, dto.Amount, dto.Notes);
            return budget is null ? NotFound() : Ok(budget);
        }

        [HttpPost("budgets/{id:int}/spread")]
        [RequireSubmodule(ErpModules.Sub.Budgets)]
        [ProducesResponseType(typeof(BudgetView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<BudgetView>> SpreadBudget(
            int id, [FromBody] SpreadBudgetDto dto)
        {
            var budget = await _budgets.SpreadAsync(id, dto.AccountId, dto.AnnualAmount);
            return budget is null ? NotFound() : Ok(budget);
        }

        [HttpDelete("budgets/{id:int}")]
        [RequireSubmodule(ErpModules.Sub.Budgets)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteBudget(int id)
        {
            var deleted = await _budgets.DeleteBudgetAsync(id);
            return deleted ? NoContent() : NotFound();
        }

        // ================================================================== banking

        [HttpGet("bank-accounts")]
        [RequireSubmodule(ErpModules.Sub.Banking)]
        [ProducesResponseType(typeof(IEnumerable<BankAccountView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<BankAccountView>>> GetBankAccounts(
            [FromQuery] bool includeInactive = true)
        {
            return Ok(await _banking.GetAccountsAsync(includeInactive));
        }

        [HttpGet("bank-accounts/{id:int}")]
        [RequireSubmodule(ErpModules.Sub.Banking)]
        [ProducesResponseType(typeof(BankAccountView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<BankAccountView>> GetBankAccount(int id)
        {
            var account = await _banking.GetAccountAsync(id);
            return account is null ? NotFound() : Ok(account);
        }

        [HttpPost("bank-accounts")]
        [RequireSubmodule(ErpModules.Sub.Banking)]
        [ProducesResponseType(typeof(BankAccountView), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<BankAccountView>> CreateBankAccount(
            [FromBody] CreateBankAccountDto dto)
        {
            var account = await _banking.CreateAccountAsync(
                dto.AccountName, dto.BankName, dto.AccountNumber, dto.Kind,
                dto.OpeningBalance, dto.Notes);

            return CreatedAtAction(nameof(GetBankAccount), new { id = account.BankAccountId }, account);
        }

        [HttpPut("bank-accounts/{id:int}")]
        [RequireSubmodule(ErpModules.Sub.Banking)]
        [ProducesResponseType(typeof(BankAccountView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<BankAccountView>> UpdateBankAccount(
            int id, [FromBody] UpdateBankAccountDto dto)
        {
            var account = await _banking.UpdateAccountAsync(
                id, dto.AccountName, dto.BankName, dto.AccountNumber,
                dto.Kind, dto.Notes, dto.IsActive);

            return account is null ? NotFound() : Ok(account);
        }

        [HttpDelete("bank-accounts/{id:int}")]
        [RequireSubmodule(ErpModules.Sub.Banking)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteBankAccount(int id)
        {
            var deleted = await _banking.DeleteAccountAsync(id);
            return deleted ? NoContent() : NotFound();
        }

        [HttpGet("bank-transactions")]
        [RequireSubmodule(ErpModules.Sub.Banking)]
        [ProducesResponseType(typeof(IEnumerable<BankTransactionView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<BankTransactionView>>> GetBankTransactions(
            [FromQuery] int? bankAccountId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] bool? reconciled,
            [FromQuery] int take = 500)
        {
            return Ok(await _banking.GetTransactionsAsync(bankAccountId, from, to, reconciled, take));
        }

        [HttpPost("bank-accounts/{id:int}/transactions")]
        [RequireSubmodule(ErpModules.Sub.Banking)]
        [ProducesResponseType(typeof(BankTransactionView), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<BankTransactionView>> RecordBankTransaction(
            int id, [FromBody] RecordBankTransactionDto dto)
        {
            var transaction = await _banking.RecordTransactionAsync(
                id, dto.TransactionDate, dto.Direction, dto.Amount, dto.Reference, dto.Description);

            return CreatedAtAction(nameof(GetBankTransactions), null, transaction);
        }

        [HttpDelete("bank-transactions/{id:int}")]
        [RequireSubmodule(ErpModules.Sub.Banking)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteBankTransaction(int id)
        {
            var deleted = await _banking.DeleteTransactionAsync(id);
            return deleted ? NoContent() : NotFound();
        }

        // ================================================================== reconciliation

        [HttpGet("bank-accounts/{id:int}/reconciliation")]
        [RequireSubmodule(ErpModules.Sub.BankReconciliation)]
        [ProducesResponseType(typeof(ReconciliationView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ReconciliationView>> GetReconciliation(
            int id, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            var reconciliation = await _banking.GetReconciliationAsync(id, from, to);
            return reconciliation is null ? NotFound() : Ok(reconciliation);
        }

        [HttpPatch("bank-transactions/{id:int}/reconciled")]
        [RequireSubmodule(ErpModules.Sub.BankReconciliation)]
        [ProducesResponseType(typeof(BankTransactionView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<BankTransactionView>> SetReconciled(
            int id, [FromBody] SetReconciledDto dto)
        {
            var transaction = await _banking.SetReconciledAsync(id, dto.IsReconciled);
            return transaction is null ? NotFound() : Ok(transaction);
        }

        [HttpPost("bank-transactions/reconcile")]
        [RequireSubmodule(ErpModules.Sub.BankReconciliation)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<object>> ReconcileMany([FromBody] ReconcileDto dto)
        {
            var count = await _banking.ReconcileManyAsync(dto.BankTransactionIds);
            return Ok(new { Reconciled = count });
        }

        // ================================================================== helpers

        private static JournalLineRequest ToRequest(JournalLineDto dto) => new()
        {
            AccountId = dto.AccountId,
            Description = dto.Description,
            Debit = dto.Debit,
            Credit = dto.Credit,
            MemberId = dto.MemberId,
            SupplierId = dto.SupplierId,
            EmployeeId = dto.EmployeeId
        };
    }
}
