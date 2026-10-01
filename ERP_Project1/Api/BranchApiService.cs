using System.Net.Http.Json;

namespace ERP_Project1.Api
{
    public class BranchDto
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
        public int EmployeeCount { get; set; }
        public int MemberCount { get; set; }

        public string StatusText => IsActive ? (IsPrimary ? "Primary" : "Open") : "Closed";

        public override string ToString() => $"{Code} - {Name}";
    }

    public class CreateBranchDto
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public DateTime? OpenedOn { get; set; }
    }

    public class UpdateBranchDto
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// Which standing records a transfer moves. Mirrors the server's own enum; transactions are
    /// deliberately absent, because moving a sale or a pay run would rewrite two branches'
    /// revenue after the fact.
    /// </summary>
    public enum BranchTransferScope
    {
        Members = 0,
        Employees = 1,
        Customers = 2,
        AllStandingRecords = 3
    }

    public class BranchTransferDto
    {
        public int FromBranchId { get; set; }
        public int ToBranchId { get; set; }
        public BranchTransferScope Scope { get; set; } = BranchTransferScope.AllStandingRecords;
    }

    public class BranchTransferResultDto
    {
        public int MembersMoved { get; set; }
        public int EmployeesMoved { get; set; }
        public int CustomersMoved { get; set; }
        public int TotalMoved { get; set; }
    }

    public class BranchSummaryDto
    {
        public int? BranchId { get; set; }
        public string BranchCode { get; set; } = "";
        public string BranchName { get; set; } = "";
        public bool IsConsolidated { get; set; }

        public int Members { get; set; }
        public int ActiveSubscriptions { get; set; }
        public int Employees { get; set; }

        public decimal MembershipRevenue { get; set; }
        public decimal SalesRevenue { get; set; }
        public decimal PaymentsCollected { get; set; }
        public decimal Expenses { get; set; }
        public decimal PayrollCost { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal NetPosition { get; set; }

        public int SaleCount { get; set; }
        public int PaymentCount { get; set; }
        public decimal ShareOfCompanyRevenue { get; set; }
    }

    public class BranchComparisonDto
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public BranchSummaryDto Company { get; set; } = new();
        public List<BranchSummaryDto> Branches { get; set; } = new();
        public string? TopBranchByRevenue { get; set; }
        public string? LowestBranchByRevenue { get; set; }
    }

    /// <summary>
    /// The branch network, under System Administration, and the branch comparison under
    /// Business Intelligence.
    ///
    /// Every call here is refused by the server unless the signed-in account is the Admin/Owner
    /// of a Medium tenant, so the client does no checking of its own beyond deciding what to
    /// draw. Note that these endpoints are not affected by the branch picker: they read across
    /// every branch on purpose, which is what makes a comparison mean anything.
    /// </summary>
    public class BranchApiService : ApiServiceBase
    {
        private const string BasePath = "api/branches";

        public BranchApiService(HttpClient http, IApiFailureSink? failures = null)
            : base(http, failures) { }

        public Task<ApiResult<List<BranchDto>>> GetAllAsync(bool includeInactive = true) =>
            SendAsync<List<BranchDto>>(() =>
                Http.GetAsync($"{BasePath}?includeInactive={includeInactive}"));

        public Task<ApiResult<BranchDto>> GetByIdAsync(int id) =>
            SendAsync<BranchDto>(() => Http.GetAsync($"{BasePath}/{id}"));

        public Task<ApiResult<BranchDto>> CreateAsync(CreateBranchDto dto) =>
            SendAsync<BranchDto>(() => Http.PostAsJsonAsync(BasePath, dto));

        public Task<ApiResult<BranchDto>> UpdateAsync(int id, UpdateBranchDto dto) =>
            SendAsync<BranchDto>(() => Http.PutAsJsonAsync($"{BasePath}/{id}", dto));

        public Task<ApiResult<object>> SetPrimaryAsync(int id) =>
            SendAsync<object>(() => Http.PostAsync($"{BasePath}/{id}/primary", null));

        public Task<ApiResult<object>> DeleteAsync(int id) =>
            SendAsync<object>(() => Http.DeleteAsync($"{BasePath}/{id}"));

        public Task<ApiResult<BranchTransferResultDto>> TransferAsync(BranchTransferDto dto) =>
            SendAsync<BranchTransferResultDto>(() =>
                Http.PostAsJsonAsync($"{BasePath}/transfer", dto));

        public Task<ApiResult<BranchComparisonDto>> CompareAsync(DateTime? from, DateTime? to) =>
            SendAsync<BranchComparisonDto>(() => Http.GetAsync(
                $"api/bi/branches?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"));
    }
}
