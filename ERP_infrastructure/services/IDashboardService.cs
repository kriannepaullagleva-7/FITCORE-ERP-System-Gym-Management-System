namespace ERP_infrastructure.services
{
    public interface IDashboardService
    {
        // Reads every counter live from the tenant database.
        Task<DashboardSummary> GetSummaryAsync();
    }
}
