using System.Net.Http.Json;

namespace ERP_Project1.Api
{
    /// <summary>
    /// Finance Management over HTTP: the chart of accounts, the ledger, the statements,
    /// periods, budgets and banking.
    ///
    /// One service for the module rather than eight, mirroring the controller. None of it
    /// knows anything about a database - every balance below was computed on the server from
    /// posted journal entries.
    /// </summary>
    public class FinanceApiService : ApiServiceBase
    {
        private const string BasePath = "api/finance";

        public FinanceApiService(HttpClient http, IApiFailureSink? failures = null)
            : base(http, failures) { }

        // ------------------------------------------------------------------ overview

        public Task<ApiResult<FinanceOverviewDto>> GetOverviewAsync(
            DateTime? from = null, DateTime? to = null) =>
            SendAsync<FinanceOverviewDto>(() => Http.GetAsync(QueryString.Build(
                $"{BasePath}/overview",
                ("from", QueryString.Date(from)), ("to", QueryString.Date(to)))));

        public Task<ApiResult<PostOutstandingResultDto>> PostOutstandingAsync(DateTime? from = null) =>
            SendAsync<PostOutstandingResultDto>(() => Http.PostAsync(QueryString.Build(
                $"{BasePath}/post-outstanding", ("from", QueryString.Date(from))), null));

        // ------------------------------------------------------------------ accounts

        public Task<ApiResult<List<AccountDto>>> GetAccountsAsync(
            string? accountType = null, bool includeInactive = true, bool withBalances = true) =>
            SendAsync<List<AccountDto>>(() => Http.GetAsync(QueryString.Build(
                $"{BasePath}/accounts",
                ("accountType", accountType),
                ("includeInactive", includeInactive ? "true" : "false"),
                ("withBalances", withBalances ? "true" : "false"))));

        public Task<ApiResult<List<string>>> GetAccountTypesAsync() =>
            SendAsync<List<string>>(() => Http.GetAsync($"{BasePath}/accounts/types"));

        public Task<ApiResult<AccountDto>> CreateAccountAsync(CreateAccountDto dto) =>
            SendAsync<AccountDto>(() => Http.PostAsJsonAsync($"{BasePath}/accounts", dto));

        public Task<ApiResult<AccountDto>> UpdateAccountAsync(int id, UpdateAccountDto dto) =>
            SendAsync<AccountDto>(() => Http.PutAsJsonAsync($"{BasePath}/accounts/{id}", dto));

        public Task<ApiResult<object>> DeleteAccountAsync(int id) =>
            SendAsync<object>(() => Http.DeleteAsync($"{BasePath}/accounts/{id}"));

        // ------------------------------------------------------------------ journal

        public Task<ApiResult<List<JournalEntryDto>>> GetJournalAsync(
            DateTime? from = null, DateTime? to = null,
            string? source = null, string? status = null, int? accountId = null) =>
            SendAsync<List<JournalEntryDto>>(() => Http.GetAsync(QueryString.Build(
                $"{BasePath}/journal",
                ("from", QueryString.Date(from)),
                ("to", QueryString.Date(to)),
                ("source", source),
                ("status", status),
                ("accountId", accountId?.ToString()))));

        public Task<ApiResult<List<string>>> GetJournalSourcesAsync() =>
            SendAsync<List<string>>(() => Http.GetAsync($"{BasePath}/journal/sources"));

        public Task<ApiResult<JournalEntryDto>> GetJournalEntryAsync(int id) =>
            SendAsync<JournalEntryDto>(() => Http.GetAsync($"{BasePath}/journal/{id}"));

        public Task<ApiResult<JournalEntryDto>> CreateJournalEntryAsync(CreateJournalEntryDto dto) =>
            SendAsync<JournalEntryDto>(() => Http.PostAsJsonAsync($"{BasePath}/journal", dto));

        public Task<ApiResult<JournalEntryDto>> UpdateJournalEntryAsync(
            int id, UpdateJournalEntryDto dto) =>
            SendAsync<JournalEntryDto>(() => Http.PutAsJsonAsync($"{BasePath}/journal/{id}", dto));

        public Task<ApiResult<JournalEntryDto>> PostJournalEntryAsync(int id) =>
            SendAsync<JournalEntryDto>(() => Http.PostAsync($"{BasePath}/journal/{id}/post", null));

        public Task<ApiResult<JournalEntryDto>> ReverseJournalEntryAsync(int id, string reason) =>
            SendAsync<JournalEntryDto>(() => Http.PostAsJsonAsync(
                $"{BasePath}/journal/{id}/reverse", new ReasonDto { Reason = reason }));

        public Task<ApiResult<object>> VoidJournalDraftAsync(int id) =>
            SendAsync<object>(() => Http.DeleteAsync($"{BasePath}/journal/{id}"));

        // ------------------------------------------------------------------ ledger

        public Task<ApiResult<GeneralLedgerDto>> GetLedgerAsync(
            int accountId, DateTime? from = null, DateTime? to = null) =>
            SendAsync<GeneralLedgerDto>(() => Http.GetAsync(QueryString.Build(
                $"{BasePath}/ledger/{accountId}",
                ("from", QueryString.Date(from)), ("to", QueryString.Date(to)))));

        // ------------------------------------------------------------------ statements

        public Task<ApiResult<TrialBalanceDto>> GetTrialBalanceAsync(DateTime? asOf = null) =>
            SendAsync<TrialBalanceDto>(() => Http.GetAsync(QueryString.Build(
                $"{BasePath}/reports/trial-balance", ("asOf", QueryString.Date(asOf)))));

        public Task<ApiResult<IncomeStatementDto>> GetIncomeStatementAsync(
            DateTime? from = null, DateTime? to = null) =>
            SendAsync<IncomeStatementDto>(() => Http.GetAsync(QueryString.Build(
                $"{BasePath}/reports/income-statement",
                ("from", QueryString.Date(from)), ("to", QueryString.Date(to)))));

        public Task<ApiResult<BalanceSheetDto>> GetBalanceSheetAsync(DateTime? asOf = null) =>
            SendAsync<BalanceSheetDto>(() => Http.GetAsync(QueryString.Build(
                $"{BasePath}/reports/balance-sheet", ("asOf", QueryString.Date(asOf)))));

        public Task<ApiResult<CashFlowDto>> GetCashFlowAsync(
            DateTime? from = null, DateTime? to = null) =>
            SendAsync<CashFlowDto>(() => Http.GetAsync(QueryString.Build(
                $"{BasePath}/reports/cash-flow",
                ("from", QueryString.Date(from)), ("to", QueryString.Date(to)))));

        public Task<ApiResult<AgedBalanceDto>> GetReceivablesAsync(DateTime? asOf = null) =>
            SendAsync<AgedBalanceDto>(() => Http.GetAsync(QueryString.Build(
                $"{BasePath}/receivables", ("asOf", QueryString.Date(asOf)))));

        public Task<ApiResult<AgedBalanceDto>> GetPayablesAsync(DateTime? asOf = null) =>
            SendAsync<AgedBalanceDto>(() => Http.GetAsync(QueryString.Build(
                $"{BasePath}/payables", ("asOf", QueryString.Date(asOf)))));

        // ------------------------------------------------------------------ periods

        public Task<ApiResult<List<FinancialPeriodDto>>> GetPeriodsAsync(int? year = null) =>
            SendAsync<List<FinancialPeriodDto>>(() => Http.GetAsync(QueryString.Build(
                $"{BasePath}/periods", ("year", year?.ToString()))));

        public Task<ApiResult<FinancialPeriodDto>> ClosePeriodAsync(int year, int month, string notes) =>
            SendAsync<FinancialPeriodDto>(() => Http.PostAsJsonAsync(
                $"{BasePath}/periods/{year}/{month}/close", new ClosePeriodDto { Notes = notes }));

        public Task<ApiResult<FinancialPeriodDto>> ReopenPeriodAsync(int year, int month, string reason) =>
            SendAsync<FinancialPeriodDto>(() => Http.PostAsJsonAsync(
                $"{BasePath}/periods/{year}/{month}/reopen", new ReasonDto { Reason = reason }));

        // ------------------------------------------------------------------ budgets

        public Task<ApiResult<List<BudgetDto>>> GetBudgetsAsync(int? year = null) =>
            SendAsync<List<BudgetDto>>(() => Http.GetAsync(QueryString.Build(
                $"{BasePath}/budgets", ("year", year?.ToString()))));

        public Task<ApiResult<BudgetDto>> GetBudgetAsync(int id) =>
            SendAsync<BudgetDto>(() => Http.GetAsync($"{BasePath}/budgets/{id}"));

        public Task<ApiResult<BudgetVarianceDto>> GetBudgetVarianceAsync(int id, int? month = null) =>
            SendAsync<BudgetVarianceDto>(() => Http.GetAsync(QueryString.Build(
                $"{BasePath}/budgets/{id}/variance", ("month", month?.ToString()))));

        public Task<ApiResult<BudgetDto>> CreateBudgetAsync(CreateBudgetDto dto) =>
            SendAsync<BudgetDto>(() => Http.PostAsJsonAsync($"{BasePath}/budgets", dto));

        public Task<ApiResult<BudgetDto>> UpdateBudgetAsync(int id, CreateBudgetDto dto) =>
            SendAsync<BudgetDto>(() => Http.PutAsJsonAsync($"{BasePath}/budgets/{id}", dto));

        public Task<ApiResult<BudgetDto>> ApproveBudgetAsync(int id) =>
            SendAsync<BudgetDto>(() => Http.PostAsync($"{BasePath}/budgets/{id}/approve", null));

        public Task<ApiResult<BudgetDto>> SetBudgetLineAsync(int id, SetBudgetLineDto dto) =>
            SendAsync<BudgetDto>(() => Http.PutAsJsonAsync($"{BasePath}/budgets/{id}/lines", dto));

        public Task<ApiResult<BudgetDto>> SpreadBudgetAsync(int id, SpreadBudgetDto dto) =>
            SendAsync<BudgetDto>(() => Http.PostAsJsonAsync($"{BasePath}/budgets/{id}/spread", dto));

        public Task<ApiResult<object>> DeleteBudgetAsync(int id) =>
            SendAsync<object>(() => Http.DeleteAsync($"{BasePath}/budgets/{id}"));

        // ------------------------------------------------------------------ banking

        public Task<ApiResult<List<BankAccountDto>>> GetBankAccountsAsync(bool includeInactive = true) =>
            SendAsync<List<BankAccountDto>>(() => Http.GetAsync(QueryString.Build(
                $"{BasePath}/bank-accounts",
                ("includeInactive", includeInactive ? "true" : "false"))));

        public Task<ApiResult<BankAccountDto>> CreateBankAccountAsync(CreateBankAccountDto dto) =>
            SendAsync<BankAccountDto>(() => Http.PostAsJsonAsync($"{BasePath}/bank-accounts", dto));

        public Task<ApiResult<BankAccountDto>> UpdateBankAccountAsync(
            int id, UpdateBankAccountDto dto) =>
            SendAsync<BankAccountDto>(() => Http.PutAsJsonAsync($"{BasePath}/bank-accounts/{id}", dto));

        public Task<ApiResult<object>> DeleteBankAccountAsync(int id) =>
            SendAsync<object>(() => Http.DeleteAsync($"{BasePath}/bank-accounts/{id}"));

        public Task<ApiResult<List<BankTransactionDto>>> GetBankTransactionsAsync(
            int? bankAccountId = null, DateTime? from = null, DateTime? to = null,
            bool? reconciled = null) =>
            SendAsync<List<BankTransactionDto>>(() => Http.GetAsync(QueryString.Build(
                $"{BasePath}/bank-transactions",
                ("bankAccountId", bankAccountId?.ToString()),
                ("from", QueryString.Date(from)),
                ("to", QueryString.Date(to)),
                ("reconciled", reconciled?.ToString()?.ToLowerInvariant()))));

        public Task<ApiResult<BankTransactionDto>> RecordBankTransactionAsync(
            int bankAccountId, RecordBankTransactionDto dto) =>
            SendAsync<BankTransactionDto>(() => Http.PostAsJsonAsync(
                $"{BasePath}/bank-accounts/{bankAccountId}/transactions", dto));

        public Task<ApiResult<object>> DeleteBankTransactionAsync(int id) =>
            SendAsync<object>(() => Http.DeleteAsync($"{BasePath}/bank-transactions/{id}"));

        public Task<ApiResult<ReconciliationDto>> GetReconciliationAsync(
            int bankAccountId, DateTime? from = null, DateTime? to = null) =>
            SendAsync<ReconciliationDto>(() => Http.GetAsync(QueryString.Build(
                $"{BasePath}/bank-accounts/{bankAccountId}/reconciliation",
                ("from", QueryString.Date(from)), ("to", QueryString.Date(to)))));

        public Task<ApiResult<BankTransactionDto>> SetReconciledAsync(int id, bool reconciled) =>
            SendAsync<BankTransactionDto>(() => Http.PatchAsJsonAsync(
                $"{BasePath}/bank-transactions/{id}/reconciled",
                new SetReconciledDto { IsReconciled = reconciled }));

        public Task<ApiResult<object>> ReconcileManyAsync(IEnumerable<int> ids) =>
            SendAsync<object>(() => Http.PostAsJsonAsync(
                $"{BasePath}/bank-transactions/reconcile",
                new ReconcileDto { BankTransactionIds = ids.ToList() }));
    }

    /// <summary>Business Intelligence: KPIs and charts, all computed on the server.</summary>
    public class AnalyticsApiService : ApiServiceBase
    {
        private const string BasePath = "api/bi";

        public AnalyticsApiService(HttpClient http, IApiFailureSink? failures = null)
            : base(http, failures) { }

        public Task<ApiResult<AnalyticsViewDto>> GetKpiDashboardAsync(
            DateTime? from = null, DateTime? to = null) =>
            SendAsync<AnalyticsViewDto>(() => Http.GetAsync(Range($"{BasePath}/kpi", from, to)));

        /// <summary>
        /// One analytics area by key - membership, sales, payments, inventory, workforce,
        /// finance or profitability. Every one answers the same shape, which is why the desktop
        /// needs a single screen rather than seven.
        /// </summary>
        public Task<ApiResult<AnalyticsViewDto>> GetAreaAsync(
            string area, DateTime? from = null, DateTime? to = null) =>
            SendAsync<AnalyticsViewDto>(() => Http.GetAsync(Range($"{BasePath}/{area}", from, to)));

        private static string Range(string path, DateTime? from, DateTime? to) =>
            QueryString.Build(path, ("from", QueryString.Date(from)), ("to", QueryString.Date(to)));
    }

    /// <summary>The settings a tenant administers for itself, under System Administration.</summary>
    public class SettingsApiService : ApiServiceBase
    {
        private const string BasePath = "api/settings";

        public SettingsApiService(HttpClient http, IApiFailureSink? failures = null)
            : base(http, failures) { }

        public Task<ApiResult<List<SettingDto>>> GetAllAsync() =>
            SendAsync<List<SettingDto>>(() => Http.GetAsync(BasePath));

        public Task<ApiResult<List<SettingDto>>> UpdateAsync(Dictionary<string, string> values) =>
            SendAsync<List<SettingDto>>(() => Http.PutAsJsonAsync(
                BasePath, new UpdateSettingsDto { Values = values }));

        public Task<ApiResult<SettingDto>> ResetAsync(string key) =>
            SendAsync<SettingDto>(() => Http.PostAsync(
                $"{BasePath}/{Uri.EscapeDataString(key)}/reset", null));
    }
}
