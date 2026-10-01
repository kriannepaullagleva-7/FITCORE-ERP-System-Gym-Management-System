namespace ERP_domain.entities
{
    /// <summary>
    /// One site of a multi-branch company.
    ///
    /// A branch lives in the <em>tenant</em> database, not the master one. It is the company's
    /// own operational data - who works where, which till took which money - and the master
    /// database holds the registry of companies, not the inside of any of them.
    ///
    /// Branching is a Medium-tier feature. A Micro or Small tenant has no Branch rows at all,
    /// every record it writes carries a null <see cref="IBranchScoped.BranchId"/>, and the
    /// branch filter never narrows anything. Nothing about the single-site case changes.
    /// </summary>
    public class Branch : IAuditable
    {
        public int BranchId { get; set; }

        /// <summary>Short stable code, unique within the company. Shown in the branch picker.</summary>
        public required string Code { get; set; }

        public required string Name { get; set; }

        public string Address { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// The branch an unassigned record belongs to, and the one a new user is placed in when
        /// no other is named. Exactly one branch per tenant carries this.
        /// </summary>
        public bool IsPrimary { get; set; }

        /// <summary>
        /// A closed branch keeps all of its history and can still be reported on, but is not
        /// offered as a destination for new records. Branches are never deleted once they have
        /// traded - see BranchService, which refuses it for the same reason a member who has
        /// paid cannot be deleted.
        /// </summary>
        public bool IsActive { get; set; } = true;

        public DateTime OpenedOn { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>
    /// Implemented by every entity whose rows belong to one branch.
    ///
    /// This is the whole of the branch contract. <c>TenantErpDbContext</c> finds every entity
    /// type implementing it and does two things automatically: it applies a global query filter
    /// so a scoped caller cannot read another branch's rows, and it stamps the branch onto new
    /// rows on the way in. Neither is something a service, a repository or a controller has to
    /// remember, which is what makes the filtering server-side in the only sense that matters -
    /// there is no query path that can opt out of it by accident.
    ///
    /// Deliberately nullable. Null means "belongs to the company rather than to any one branch",
    /// which is what every row on a single-site tenant is, and what a record written before the
    /// company branched stays until it is assigned.
    /// </summary>
    public interface IBranchScoped
    {
        int? BranchId { get; set; }
    }
}
