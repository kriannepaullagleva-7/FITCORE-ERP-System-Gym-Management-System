using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.repositories
{
    public class MemberRepository
        : GenericRepository<Member>, IMemberRepository
    {
        public MemberRepository(TenantErpDbContext context)
            : base(context)
        {
        }

        // Get one member with subscriptions and plans
        public async Task<Member?> GetMemberWithSubscriptionsAsync(
            int memberId)
        {
            return await _dbSet
                .Include(m => m.Subscriptions)
                .ThenInclude(s => s.Plan)
                .FirstOrDefaultAsync(m => m.MemberId == memberId);
        }

        // Get all active members
        public async Task<List<Member>> GetActiveMembers()
        {
            return await _dbSet
                .Where(m => m.Status == "Active")
                .ToListAsync();
        }

        // Case-insensitive match across the fields shown in the member grid.
        public async Task<List<Member>> SearchAsync(string term)
        {
            if (string.IsNullOrWhiteSpace(term))
                return await GetAllAsync();

            var pattern = $"%{term.Trim()}%";

            return await _dbSet
                .Where(m =>
                    EF.Functions.Like(m.FirstName, pattern) ||
                    EF.Functions.Like(m.LastName, pattern) ||
                    EF.Functions.Like(m.Email, pattern) ||
                    EF.Functions.Like(m.Phone, pattern))
                .OrderBy(m => m.MemberId)
                .ToListAsync();
        }

        // The member -> subscription/payment/sale foreign keys all cascade, so deleting a
        // member would silently take their whole history with them. Callers check this
        // first and refuse the delete instead.
        public async Task<MemberHistoryCounts> GetHistoryCountsAsync(int memberId)
        {
            return new MemberHistoryCounts
            {
                Subscriptions = await _context.Subscriptions.CountAsync(s => s.MemberId == memberId),
                Payments = await _context.Payments.CountAsync(p => p.MemberId == memberId),
                Sales = await _context.Sales.CountAsync(s => s.MemberId == memberId)
            };
        }

        // Every member together with their subscription history, used to derive
        // the membership status and expiry shown in the Membership module.
        public async Task<List<Member>> GetMembersWithSubscriptionsAsync()
        {
            return await _dbSet
                .Include(m => m.Subscriptions)
                .ThenInclude(s => s.Plan)
                .OrderBy(m => m.MemberId)
                .ToListAsync();
        }
    }
}
