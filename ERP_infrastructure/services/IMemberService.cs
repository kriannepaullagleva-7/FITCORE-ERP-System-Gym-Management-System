using ERP_domain.entities;
using ERP_infrastructure.repositories;

namespace ERP_infrastructure.services
{
    // The member -> subscription/payment/sale foreign keys cascade, so deleting a member
    // with history would take that history with it. The service refuses instead and the
    // caller is expected to retire the member by setting their status to Inactive.
    public enum MemberDeleteResult
    {
        Deleted,
        NotFound,
        HasHistory
    }

    public interface IMemberService
    {
        Task<Member?> GetMemberByIdAsync(int id);
        Task<List<Member>> GetAllMembersAsync();
        Task<Member> CreateMemberAsync(string firstName, string lastName, string phone, string email);
        Task<Member?> UpdateMemberAsync(int id, string firstName, string lastName, string phone, string email, string status);
        Task<MemberDeleteResult> DeleteMemberAsync(int id);

        /// <summary>Retires a member without destroying their subscriptions or payments.</summary>
        Task<Member?> ArchiveMemberAsync(int id);

        /// <summary>Returns an archived member to active use.</summary>
        Task<Member?> RestoreMemberAsync(int id);
        Task<MemberHistoryCounts> GetMemberHistoryCountsAsync(int id);
        Task<Member?> GetMemberWithSubscriptionsAsync(int memberId);
        Task<List<Member>> GetActiveMembersAsync();
        Task<List<Member>> SearchMembersAsync(string term);

        // Members joined to their current plan, with membership status and expiry resolved.
        Task<List<MembershipView>> GetMembershipOverviewAsync();
    }
}
