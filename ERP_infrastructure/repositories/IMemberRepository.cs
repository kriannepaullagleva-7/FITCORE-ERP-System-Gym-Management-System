using ERP_domain.entities;

namespace ERP_infrastructure.repositories
{
    public interface IMemberRepository : IGenericRepository<Member>
    {
        Task<Member?> GetMemberWithSubscriptionsAsync(int memberId);

        Task<List<Member>> GetActiveMembers();

        Task<List<Member>> SearchAsync(string term);

        Task<List<Member>> GetMembersWithSubscriptionsAsync();

        // Subscriptions, payments and sales that would be destroyed with the member.
        Task<MemberHistoryCounts> GetHistoryCountsAsync(int memberId);
    }
}
