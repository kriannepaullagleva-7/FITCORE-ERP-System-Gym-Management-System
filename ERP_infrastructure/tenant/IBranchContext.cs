namespace ERP_infrastructure.tenant
{
    /// <summary>How the branch for the current request was decided.</summary>
    public enum BranchSource
    {
        /// <summary>No branch. The caller sees the whole company.</summary>
        None = 0,

        /// <summary>
        /// The branch claim on the token. Minted at sign-in from the account's own
        /// <c>AppUser.BranchId</c>, so it is not something the caller can choose.
        /// </summary>
        Claim = 1,

        /// <summary>
        /// The <c>X-Branch-Id</c> header, honoured only for an Admin/Owner whose account is not
        /// itself bound to a branch. This is the branch picker in the desktop topbar.
        /// </summary>
        Selection = 2
    }

    /// <summary>
    /// Which branch the current request is looking at, decided by the server.
    ///
    /// Null <see cref="BranchId"/> means the whole company: a single-site tenant, or an
    /// Admin/Owner who has not picked a branch. Anything else narrows every read and stamps
    /// every write, and neither is optional - <c>TenantErpDbContext</c> applies both to every
    /// branch-scoped entity, so there is no query in the application that can quietly escape it.
    /// </summary>
    public interface IBranchContext
    {
        int? BranchId { get; }

        BranchSource Source { get; }

        /// <summary>True when a branch is selected and the filter is therefore narrowing.</summary>
        bool IsScoped => BranchId.HasValue;

        /// <summary>
        /// True when the branch came from the account rather than from a request header, which
        /// is what makes it non-negotiable. A Manager or Staff account cannot widen its own
        /// scope by sending a different header, and cannot administer branches at all.
        /// </summary>
        bool IsBoundToAccount => Source == BranchSource.Claim;
    }

    /// <summary>
    /// Write side of <see cref="IBranchContext"/>. Only the branch resolution middleware, or a
    /// host that sets the scope explicitly, should use this.
    /// </summary>
    public interface IBranchContextSetter
    {
        void Set(int? branchId, BranchSource source);
    }

    /// <inheritdoc cref="IBranchContext" />
    public sealed class BranchContext : IBranchContext, IBranchContextSetter
    {
        public int? BranchId { get; private set; }

        public BranchSource Source { get; private set; } = BranchSource.None;

        public void Set(int? branchId, BranchSource source)
        {
            // A branch id of zero or less is not a branch. Treating it as "the whole company"
            // rather than as a branch nobody has is the fail-safe reading: a malformed header
            // must not silently scope a caller into an empty view they cannot explain.
            BranchId = branchId is > 0 ? branchId : null;
            Source = BranchId.HasValue ? source : BranchSource.None;
        }
    }

    /// <summary>
    /// The scope a host with no request has: the whole company.
    ///
    /// Registered by the infrastructure wiring with TryAdd, so the API's request-scoped
    /// resolution wins where there is one. The design-time factories, the tests and any
    /// background work fall back to this and see everything, which is what they should see.
    /// </summary>
    public sealed class NullBranchContext : IBranchContext
    {
        public int? BranchId => null;
        public BranchSource Source => BranchSource.None;
    }
}
