using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    public class MemberNoteView
    {
        public int MemberNoteId { get; set; }
        public int MemberId { get; set; }
        public string Category { get; set; } = "";
        public string Note { get; set; } = "";
        public bool IsPinned { get; set; }
        public string Author { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        /// <summary>True when the signed-in user wrote it, so the UI can offer an edit.</summary>
        public bool IsMine { get; set; }
    }

    /// <summary>
    /// Notes kept against a member: an injury, a dispute, why they nearly left last year.
    ///
    /// A gym's useful knowledge about a member is a sequence of observations, so each note
    /// carries its own date and author rather than being flattened into one field on the member
    /// where who said what and when is lost.
    /// </summary>
    public interface IMemberNoteService
    {
        Task<List<MemberNoteView>> GetNotesAsync(int memberId);

        Task<MemberNoteView> AddNoteAsync(int memberId, string category, string note, bool isPinned);

        /// <summary>
        /// Edits a note. Only its author may: a note is somebody's observation, and letting a
        /// colleague rewrite it would make the attribution a lie.
        /// </summary>
        Task<MemberNoteView?> UpdateNoteAsync(
            int memberNoteId, string category, string note, bool isPinned);

        Task<bool> DeleteNoteAsync(int memberNoteId);
    }

    public class MemberNoteService : IMemberNoteService
    {
        private readonly TenantErpDbContext _context;
        private readonly ICurrentUserAccessor _actor;

        public MemberNoteService(TenantErpDbContext context, ICurrentUserAccessor actor)
        {
            _context = context;
            _actor = actor;
        }

        public async Task<List<MemberNoteView>> GetNotesAsync(int memberId)
        {
            var actor = _actor.Current;

            var rows = await _context.MemberNotes
                .AsNoTracking()
                .Where(n => n.MemberId == memberId)

                // Pinned first: these are what the front desk must see before speaking to the
                // member, and burying them under six months of routine notes defeats the point.
                .OrderByDescending(n => n.IsPinned)
                .ThenByDescending(n => n.CreatedAt)
                .ToListAsync();

            return rows.Select(n => ToView(n, actor.AppUserId)).ToList();
        }

        public async Task<MemberNoteView> AddNoteAsync(
            int memberId, string category, string note, bool isPinned)
        {
            if (!await _context.Members.AnyAsync(m => m.MemberId == memberId))
            {
                throw new ValidationException($"No member with id {memberId} exists.");
            }

            var text = Clean(note);

            if (text.Length == 0) throw new ValidationException("A note needs something in it.");

            if (text.Length > 2000)
            {
                throw new ValidationException("A note cannot be longer than 2000 characters.");
            }

            var normalised = MemberNoteCategories.All.FirstOrDefault(c =>
                string.Equals(c, Clean(category), StringComparison.OrdinalIgnoreCase))
                ?? MemberNoteCategories.General;

            var actor = _actor.Current;

            var entity = new MemberNote
            {
                MemberId = memberId,
                Category = normalised,
                Note = text,
                IsPinned = isPinned,
                AuthorUserId = actor.AppUserId,
                Author = actor.DisplayName,
                CreatedAt = DateTime.UtcNow
            };

            _context.MemberNotes.Add(entity);
            await _context.SaveChangesAsync();

            return ToView(entity, actor.AppUserId);
        }

        public async Task<MemberNoteView?> UpdateNoteAsync(
            int memberNoteId, string category, string note, bool isPinned)
        {
            var entity = await _context.MemberNotes
                .FirstOrDefaultAsync(n => n.MemberNoteId == memberNoteId);

            if (entity is null) return null;

            var actor = _actor.Current;

            RequireAuthor(entity, actor, "edit");

            var text = Clean(note);
            if (text.Length == 0) throw new ValidationException("A note needs something in it.");

            entity.Category = MemberNoteCategories.All.FirstOrDefault(c =>
                string.Equals(c, Clean(category), StringComparison.OrdinalIgnoreCase))
                ?? entity.Category;

            entity.Note = text;
            entity.IsPinned = isPinned;

            await _context.SaveChangesAsync();

            return ToView(entity, actor.AppUserId);
        }

        public async Task<bool> DeleteNoteAsync(int memberNoteId)
        {
            var entity = await _context.MemberNotes
                .FirstOrDefaultAsync(n => n.MemberNoteId == memberNoteId);

            if (entity is null) return false;

            RequireAuthor(entity, _actor.Current, "delete");

            _context.MemberNotes.Remove(entity);
            await _context.SaveChangesAsync();

            return true;
        }

        /// <summary>
        /// A note belongs to whoever wrote it. An administrator may still remove one - somebody
        /// has to be able to take down something inappropriate - but a colleague may not
        /// quietly rewrite another person's observation.
        /// </summary>
        private static void RequireAuthor(MemberNote note, AuditActor actor, string verb)
        {
            if (actor.AppUserId is null) return;   // an unauthenticated context: a fixture or a tool

            if (note.AuthorUserId == actor.AppUserId) return;

            if (ErpRoles.LevelOf(actor.RoleKey) <= ErpRoles.LevelOf(ErpRoles.Admin)) return;

            throw new ForbiddenOperationException(
                $"{note.Author} wrote this note, so only they or an administrator can {verb} it.");
        }

        private static MemberNoteView ToView(MemberNote n, int? currentUserId) => new()
        {
            MemberNoteId = n.MemberNoteId,
            MemberId = n.MemberId,
            Category = n.Category,
            Note = n.Note,
            IsPinned = n.IsPinned,
            Author = string.IsNullOrWhiteSpace(n.Author) ? "—" : n.Author,
            CreatedAt = n.CreatedAt,
            UpdatedAt = n.UpdatedAt,
            IsMine = currentUserId.HasValue && n.AuthorUserId == currentUserId
        };

        private static string Clean(string? value) => (value ?? "").Trim();
    }
}
