using System.Reflection;
using Microsoft.EntityFrameworkCore;
using ERP_domain.entities;
using ERP_infrastructure.services;
using ERP_infrastructure.tenant;

namespace ERP_infrastructure.data
{
    public class TenantErpDbContext : DbContext
    {
        private readonly ICurrentUserAccessor? _actor;
        private readonly IBranchContext? _branch;

        /// <summary>
        /// The accessor is optional so the design-time factories, the WinForms host and the
        /// tests construct this context exactly as they did before. Without one, changes are
        /// still saved; they are simply not attributed, which is the correct behaviour for a
        /// migration or a fixture.
        ///
        /// It is taken as a constructor argument rather than registered as an interceptor on
        /// DbContextOptions because those options are cached per connection string for the life
        /// of the process - an interceptor attached there would outlive the request and could
        /// attribute one tenant's write to another tenant's user.
        /// </summary>
        public TenantErpDbContext(
            DbContextOptions<TenantErpDbContext> options,
            ICurrentUserAccessor? actor = null,
            IBranchContext? branch = null)
            : base(options)
        {
            _actor = actor;
            _branch = branch;
        }

        /// <summary>
        /// The branch every query on this context is narrowed to, or null for the whole company.
        ///
        /// Public because the global query filters below close over it: EF reads it from the
        /// context instance when it builds each query, which is what lets one cached model serve
        /// a request scoped to Branch A and the next one scoped to Branch B.
        /// </summary>
        public int? CurrentBranchId => _branch?.BranchId;

        public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

        /// <summary>
        /// The company's branches. Empty on a Micro or Small tenant, which has none, and
        /// deliberately not itself branch-scoped - it is the list you choose a branch from.
        /// </summary>
        public DbSet<Branch> Branches => Set<Branch>();

        // Existing DbSets
        public DbSet<Customer> Customers => Set<Customer>();
        public DbSet<Supplier> Suppliers => Set<Supplier>();
        public DbSet<Product> Products => Set<Product>();
        public DbSet<Inventory> Inventories => Set<Inventory>();
        public DbSet<StockMovement> StockMovements => Set<StockMovement>();

        // New FITCORE DbSets
        public DbSet<Member> Members => Set<Member>();
        public DbSet<MembershipPlan> MembershipPlans => Set<MembershipPlan>();
        public DbSet<Subscription> Subscriptions => Set<Subscription>();
        public DbSet<Sale> Sales => Set<Sale>();
        public DbSet<SaleItem> SaleItems => Set<SaleItem>();
        public DbSet<Payment> Payments => Set<Payment>();

        // Workforce DbSets
        public DbSet<Employee> Employees => Set<Employee>();
        public DbSet<Payroll> Payrolls => Set<Payroll>();
        public DbSet<Attendance> Attendances => Set<Attendance>();
        public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();

        // Purchasing and returns - the two transactions that make inventory and sales
        // financially complete rather than merely quantitative.
        public DbSet<Purchase> Purchases => Set<Purchase>();
        public DbSet<PurchaseItem> PurchaseItems => Set<PurchaseItem>();
        public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();
        public DbSet<SaleReturn> SaleReturns => Set<SaleReturn>();
        public DbSet<SaleReturnItem> SaleReturnItems => Set<SaleReturnItem>();

        // Finance Management. Every financial consequence of every operational event above
        // ends up as a journal entry against an account, which is what makes the statements
        // derivable from one table rather than assembled by adding up each module separately.
        public DbSet<Account> Accounts => Set<Account>();
        public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
        public DbSet<JournalEntryLine> JournalEntryLines => Set<JournalEntryLine>();
        public DbSet<FinancialPeriod> FinancialPeriods => Set<FinancialPeriod>();
        public DbSet<Budget> Budgets => Set<Budget>();
        public DbSet<BudgetLine> BudgetLines => Set<BudgetLine>();
        public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
        public DbSet<BankTransaction> BankTransactions => Set<BankTransaction>();
        public DbSet<Expense> Expenses => Set<Expense>();

        // Membership and administration supporting records.
        public DbSet<MemberNote> MemberNotes => Set<MemberNote>();
        public DbSet<TenantSetting> TenantSettings => Set<TenantSetting>();

        /// <summary>
        /// Stamps <see cref="IAuditable.UpdatedAt"/> on every record being changed.
        ///
        /// Doing it here rather than in each service means no write path can forget it, and
        /// every row touched by the same SaveChanges gets an identical timestamp instead of
        /// a spread of times taken as each service happened to run.
        /// </summary>
        private void StampAuditFields()
        {
            var changed = ChangeTracker.Entries<IAuditable>()
                .Where(e => e.State == EntityState.Modified)
                .ToList();

            if (changed.Count == 0) return;

            var now = DateTime.UtcNow;

            foreach (var entry in changed)
            {
                entry.Entity.UpdatedAt = now;

                // CreatedAt is set once when the row is written and must survive every later
                // edit, so it is explicitly excluded from the update.
                entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
            }
        }

        /// <summary>
        /// Resolved once per context and only when something actually needs it. Null means
        /// "asked, and this tenant has no branches", which is every Micro and Small company.
        /// </summary>
        private int? _primaryBranchId;
        private bool _primaryBranchResolved;

        /// <summary>
        /// Puts a branch on every branch-scoped row being inserted.
        ///
        /// Two cases, and the second is the one worth stating. A scoped caller - anybody whose
        /// account is bound to a branch, or an Admin who has picked one - writes to that branch,
        /// and cannot write anywhere else, because this runs after the service has built the
        /// entity and overrides nothing it was allowed to set. An unscoped caller on a company
        /// that *has* branches falls to the primary one: an Admin looking at the whole company
        /// is not a reason to write a record that belongs to no branch and therefore appears in
        /// no branch's books.
        ///
        /// On a tenant with no branches at all both cases resolve to null, which is exactly what
        /// every row on a single-site company carries. Nothing about that case changes.
        /// </summary>
        private void StampBranch()
        {
            var pending = ChangeTracker.Entries<IBranchScoped>()
                .Where(e => e.State == EntityState.Added && e.Entity.BranchId is null)
                .ToList();

            if (pending.Count == 0) return;

            var branchId = CurrentBranchId ?? ResolvePrimaryBranchId();
            if (branchId is null) return;

            foreach (var entry in pending)
            {
                entry.Entity.BranchId = branchId;
            }
        }

        private int? ResolvePrimaryBranchId()
        {
            if (_primaryBranchResolved) return _primaryBranchId;

            _primaryBranchResolved = true;

            _primaryBranchId = Branches
                .AsNoTracking()
                .Where(b => b.IsPrimary && b.IsActive)
                .Select(b => (int?)b.BranchId)
                .FirstOrDefault();

            return _primaryBranchId;
        }

        /// <summary>
        /// Guards against the audit write auditing itself, which would not terminate.
        /// </summary>
        private bool _writingAudit;

        public override int SaveChanges()
        {
            StampAuditFields();
            StampBranch();

            if (_writingAudit || _actor is null) return base.SaveChanges();

            var drafts = AuditCapture.Collect(ChangeTracker, _actor.Current);
            var result = base.SaveChanges();

            if (drafts.Count > 0) WriteAudit(drafts);

            return result;
        }

        public override async Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            StampAuditFields();
            StampBranch();

            if (_writingAudit || _actor is null)
            {
                return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            }

            // Collected before saving, while the tracker still holds original values and the
            // rows being deleted still exist.
            var drafts = AuditCapture.Collect(ChangeTracker, _actor.Current);

            var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

            if (drafts.Count > 0)
            {
                // Finalised afterwards, because an inserted row has no identity value until the
                // insert has run.
                _writingAudit = true;
                try
                {
                    AuditEvents.AddRange(AuditCapture.Finalise(drafts));
                    await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
                }
                finally
                {
                    _writingAudit = false;
                }
            }

            return result;
        }

        private void WriteAudit(List<AuditCapture.Draft> drafts)
        {
            _writingAudit = true;
            try
            {
                AuditEvents.AddRange(AuditCapture.Finalise(drafts));
                base.SaveChanges();
            }
            finally
            {
                _writingAudit = false;
            }
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Existing configurations...
            builder.ConfigureAuditEvents();
            ConfigureProducts(builder);
            ConfigureSuppliers(builder);
            ConfigureCustomers(builder);
            ConfigureInventory(builder);
            ConfigureStockMovements(builder);

            // New FITCORE configurations
            ConfigureMembers(builder);
            ConfigureMembershipPlans(builder);
            ConfigureSubscriptions(builder);
            ConfigureSales(builder);
            ConfigureSaleItems(builder);
            ConfigurePayments(builder);

            // Workforce configurations
            ConfigureEmployees(builder);
            ConfigurePayrolls(builder);
            ConfigureAttendances(builder);
            ConfigureLeaveRequests(builder);

            // Purchasing and returns
            ConfigurePurchases(builder);
            ConfigureSupplierPayments(builder);
            ConfigureSaleReturns(builder);

            // Finance Management
            ConfigureAccounts(builder);
            ConfigureJournal(builder);
            ConfigureFinancialPeriods(builder);
            ConfigureBudgets(builder);
            ConfigureBanking(builder);
            ConfigureExpenses(builder);

            // Supporting records
            ConfigureMemberNotes(builder);
            ConfigureTenantSettings(builder);

            // Branching. Configured last, because the filter sweep has to see every entity type
            // the configurations above have already registered.
            ConfigureBranches(builder);
            ApplyBranchFilters(builder);
        }

        private void ConfigureBranches(ModelBuilder builder)
        {
            builder.Entity<Branch>(entity =>
            {
                entity.HasKey(b => b.BranchId);

                entity.Property(b => b.Code).IsRequired().HasMaxLength(20);
                entity.Property(b => b.Name).IsRequired().HasMaxLength(150);
                entity.Property(b => b.Address).HasMaxLength(250);
                entity.Property(b => b.Phone).HasMaxLength(40);
                entity.Property(b => b.Email).HasMaxLength(150);

                // Unique within the tenant, which is the whole company: there is no CompanyId
                // column here because the database *is* the company.
                entity.HasIndex(b => b.Code).IsUnique();
            });
        }

        private static readonly MethodInfo BranchFilterMethod =
            typeof(TenantErpDbContext).GetMethod(
                nameof(ApplyBranchFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

        /// <summary>
        /// Puts the branch filter on every entity that declares itself branch-scoped.
        ///
        /// Done by sweeping the model rather than listing the types, for the same reason the
        /// audit sweep is: a list maintained by hand is a list that will one day be missing the
        /// entity somebody added last week, and a missing branch filter is one branch reading
        /// another's takings. Implementing <see cref="IBranchScoped"/> is the whole opt-in.
        /// </summary>
        private void ApplyBranchFilters(ModelBuilder builder)
        {
            foreach (var entityType in builder.Model.GetEntityTypes())
            {
                if (entityType.BaseType is not null) continue;
                if (!typeof(IBranchScoped).IsAssignableFrom(entityType.ClrType)) continue;

                BranchFilterMethod
                    .MakeGenericMethod(entityType.ClrType)
                    .Invoke(this, new object[] { builder });
            }
        }

        /// <summary>
        /// The filter itself.
        ///
        /// It closes over <see cref="CurrentBranchId"/> on this context instance rather than
        /// over a captured constant, so EF compiles it into a parameter read per query. One
        /// cached model therefore serves a request scoped to Branch A and the next one scoped to
        /// Branch B, which matters because the options - and with them the model - are cached
        /// per connection string for the life of the process.
        ///
        /// Unscoped means no narrowing at all, including the rows that belong to no branch. A
        /// scoped caller sees only their own branch, and in particular does *not* see the
        /// unassigned rows: those belong to the company, and lending them to whichever branch
        /// happened to ask would double-count them in every branch comparison.
        /// </summary>
        private void ApplyBranchFilter<TEntity>(ModelBuilder builder)
            where TEntity : class, IBranchScoped
        {
            builder.Entity<TEntity>()
                .HasQueryFilter(e => CurrentBranchId == null || e.BranchId == CurrentBranchId);

            builder.Entity<TEntity>().HasIndex(e => e.BranchId);
        }

        private void ConfigureMembers(ModelBuilder builder)
        {
            builder.Entity<Member>(entity =>
            {
                entity.HasKey(x => x.MemberId);
                entity.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
                entity.Property(x => x.LastName).HasMaxLength(100).IsRequired();
                entity.Property(x => x.Phone).HasMaxLength(20).IsRequired().HasDefaultValue("");
                entity.Property(x => x.Email).HasMaxLength(100).IsRequired().HasDefaultValue("");
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("Active");
                entity.Property(x => x.SuspensionReason).HasMaxLength(300).IsRequired().HasDefaultValue("");
                entity.Property(x => x.Address).HasMaxLength(300).IsRequired().HasDefaultValue("");
                entity.Property(x => x.EmergencyContactName).HasMaxLength(150).IsRequired().HasDefaultValue("");
                entity.Property(x => x.EmergencyContactPhone).HasMaxLength(30).IsRequired().HasDefaultValue("");
                entity.Property(x => x.JoinDate).IsRequired().HasDefaultValueSql("GETUTCDATE()");
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                // The roll is filtered by status on nearly every screen and report.
                entity.HasIndex(x => x.Status);
            });
        }

        private void ConfigureMembershipPlans(ModelBuilder builder)
        {
            builder.Entity<MembershipPlan>(entity =>
            {
                entity.HasKey(x => x.PlanId);
                entity.Property(x => x.PlanName).HasMaxLength(100).IsRequired();
                entity.Property(x => x.Price).HasPrecision(18, 2);
                entity.Property(x => x.Description).HasMaxLength(500);
            });
        }

        private void ConfigureSubscriptions(ModelBuilder builder)
        {
            builder.Entity<Subscription>(entity =>
            {
                entity.HasKey(x => x.SubscriptionId);
                entity.Property(x => x.Status).HasMaxLength(20);
                entity.Property(x => x.WalkInName).HasMaxLength(150);
                entity.Property(x => x.WalkInPhone).HasMaxLength(20);

                entity.HasOne(x => x.Member)
                    .WithMany(x => x.Subscriptions)
                    .HasForeignKey(x => x.MemberId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.Plan)
                    .WithMany(x => x.Subscriptions)
                    .HasForeignKey(x => x.PlanId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }

        private void ConfigureSales(ModelBuilder builder)
        {
            builder.Entity<Sale>(entity =>
            {
                entity.HasKey(x => x.SaleId);
                entity.Property(x => x.Subtotal).HasPrecision(18, 2);
                entity.Property(x => x.Discount).HasPrecision(18, 2);
                entity.Property(x => x.TotalAmount).HasPrecision(18, 2);
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("Completed");
                entity.Property(x => x.Notes).HasMaxLength(300).IsRequired().HasDefaultValue("");

                entity.Property(x => x.ProcessedBy).HasMaxLength(150).IsRequired().HasDefaultValue("");
                entity.Property(x => x.WalkInName).HasMaxLength(150);
                entity.Property(x => x.AmountTendered).HasPrecision(18, 2);
                entity.Property(x => x.ChangeGiven).HasPrecision(18, 2);

                entity.HasOne(x => x.Member)
                    .WithMany(x => x.Sales)
                    .HasForeignKey(x => x.MemberId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.Restrict);

                // Staff attribution is optional and must never block removing an employee,
                // so the sale keeps its history with the cashier cleared.
                entity.HasOne(x => x.CashierEmployee)
                    .WithMany()
                    .HasForeignKey(x => x.CashierEmployeeId)
                    .OnDelete(DeleteBehavior.SetNull);

                // Sales are almost always listed newest first, or filtered to a date range.
                entity.HasIndex(x => x.SaleDate);
            });
        }

        private void ConfigureSaleItems(ModelBuilder builder)
        {
            builder.Entity<SaleItem>(entity =>
            {
                entity.HasKey(x => x.SaleItemId);
                entity.Property(x => x.UnitPrice).HasPrecision(18, 2);

                // Four places, matching the weighted average cost it is copied from.
                entity.Property(x => x.UnitCost).HasPrecision(18, 4);

                entity.HasOne(x => x.Sale)
                    .WithMany(x => x.Items)
                    .HasForeignKey(x => x.SaleId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(x => x.Product)
                    .WithMany()
                    .HasForeignKey(x => x.ProductId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }

        private void ConfigurePayments(ModelBuilder builder)
        {
            builder.Entity<Payment>(entity =>
            {
                entity.HasKey(x => x.PaymentId);
                entity.Property(x => x.Amount).HasPrecision(18, 2);
                entity.Property(x => x.Method).HasMaxLength(50).IsRequired().HasDefaultValue("Cash");
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("Completed");
                entity.Property(x => x.ReferenceNo).HasMaxLength(60).IsRequired().HasDefaultValue("");
                entity.Property(x => x.Notes).HasMaxLength(300).IsRequired().HasDefaultValue("");

                // Rows written before this column existed default to General; the migration
                // reclassifies them from what they are attached to.
                entity.Property(x => x.Category)
                      .HasMaxLength(20).IsRequired()
                      .HasDefaultValue(PaymentCategories.General);

                entity.Property(x => x.ProcessedBy).HasMaxLength(150).IsRequired().HasDefaultValue("");
                entity.Property(x => x.WalkInName).HasMaxLength(150);
                entity.Property(x => x.AmountTendered).HasPrecision(18, 2);
                entity.Property(x => x.ChangeGiven).HasPrecision(18, 2);

                // Reports group takings by what they settle.
                entity.HasIndex(x => x.Category);
                entity.Property(x => x.PaymentDate).IsRequired().HasDefaultValueSql("GETUTCDATE()");
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                // A member cannot be deleted out from under their payment history: takings have
                // to stay reconcilable after somebody leaves the gym. MemberService refuses the
                // delete and offers to archive instead; this is the database saying the same
                // thing, so a direct write cannot get around it. A walk-in payment carries no
                // member at all, so the FK is optional.
                //
                // The subscription and sale legs stay NoAction to avoid multiple cascade paths
                // into Payments, which SQL Server rejects. SaleService clears those links itself.
                entity.HasOne(x => x.Member)
                    .WithMany(x => x.Payments)
                    .HasForeignKey(x => x.MemberId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.Subscription)
                    .WithMany(x => x.Payments)
                    .HasForeignKey(x => x.SubscriptionId)
                    .OnDelete(DeleteBehavior.NoAction);

                // Member -> Sale and Member -> Payment already cascade, so cascading here too
                // would give SQL Server two paths into Payments. SaleService clears the link
                // itself before deleting a sale.
                entity.HasOne(x => x.Sale)
                    .WithMany(x => x.Payments)
                    .HasForeignKey(x => x.SaleId)
                    .OnDelete(DeleteBehavior.NoAction);

                // Payment reports and the outstanding-balance figures filter on date.
                entity.HasIndex(x => x.PaymentDate);
            });
        }

        private void ConfigureProducts(ModelBuilder builder)
        {
            builder.Entity<Product>(entity =>
            {
                entity.HasKey(x => x.ProductId);
                entity.Property(x => x.ProductCode).HasMaxLength(50).IsRequired();
                entity.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
                entity.Property(x => x.Category).HasMaxLength(50).IsRequired().HasDefaultValue("Other");
                entity.Property(x => x.CostPrice).HasPrecision(18, 2);
                entity.Property(x => x.UnitPrice).HasPrecision(18, 2);

                // ProductService already refuses a duplicate code. The index makes the
                // database enforce it too, which closes the race between two concurrent
                // creates that both pass the service check.
                entity.HasIndex(x => x.ProductCode).IsUnique();
            });
        }

        private void ConfigureSuppliers(ModelBuilder builder)
        {
            builder.Entity<Supplier>(entity =>
            {
                entity.HasKey(x => x.SupplierId);
                entity.Property(x => x.SupplierCode).HasMaxLength(50).IsRequired();
                entity.Property(x => x.SupplierName).HasMaxLength(200).IsRequired();
                entity.Property(x => x.ContactPerson).HasMaxLength(150);
                entity.Property(x => x.ContactNumber).HasMaxLength(30);
                entity.Property(x => x.EmailAddress).HasMaxLength(200);
                entity.Property(x => x.Address).HasMaxLength(300);
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                // A supplier code identifies one supplier; two rows sharing it makes a purchase
                // history impossible to attribute.
                entity.HasIndex(x => x.SupplierCode).IsUnique();
            });
        }

        /// <summary>
        /// Customer had no configuration at all, so every string column was nvarchar(max) and
        /// the code was not unique. Both are fixed here.
        /// </summary>
        private void ConfigureCustomers(ModelBuilder builder)
        {
            builder.Entity<Customer>(entity =>
            {
                entity.HasKey(x => x.CustomerId);
                entity.Property(x => x.CustomerCode).HasMaxLength(50).IsRequired();
                entity.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();
                entity.Property(x => x.ContactNumber).HasMaxLength(30);
                entity.Property(x => x.EmailAddress).HasMaxLength(200);
                entity.Property(x => x.Address).HasMaxLength(300);
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                entity.HasIndex(x => x.CustomerCode).IsUnique();
            });
        }

        private void ConfigureInventory(ModelBuilder builder)
        {
            builder.Entity<Inventory>(entity =>
            {
                entity.HasKey(x => x.InventoryId);

                // The quantity is its own concurrency token.
                //
                // Selling stock is read-check-write: read the quantity, refuse if it is too
                // low, subtract. Two tills doing that at the same moment both read the same
                // figure, both pass the check and both write what they calculated - and under
                // read-committed, which is what an ordinary transaction gets, the second write
                // simply overwrites the first. The gym sells six of the last five, and the two
                // stock movements both claim to have started from the same balance, which
                // breaks the chain that is supposed to explain the quantity on hand.
                //
                // Marking the column as a token puts "...and the quantity is still what I read"
                // into the UPDATE's WHERE clause. The loser affects no rows, EF raises
                // DbUpdateConcurrencyException, and the sale's transaction rolls back - so the
                // second caller is refused instead of silently winning.
                //
                // This is a concurrency token rather than a rowversion on purpose: it needs no
                // column, and therefore no migration against three live tenant databases, and
                // it works identically on SQL Server and on the SQLite the tests run against.
                entity.Property(x => x.QuantityOnHand).HasPrecision(18, 2).IsConcurrencyToken();

                entity.Property(x => x.ReorderLevel).HasPrecision(18, 2);

                // Cost is carried to four places, not two. Two would round every receipt's
                // effect on the weighted average, and a few hundred receipts of that error
                // compounds into an inventory valuation that is visibly wrong.
                entity.Property(x => x.AverageCost).HasPrecision(18, 4);
                entity.Property(x => x.LastUnitCost).HasPrecision(18, 4);

                // Stock is one row per product. Without this a second row can appear and
                // half the stock becomes invisible to whichever query reads the other one.
                entity.HasIndex(x => x.ProductId).IsUnique();

                entity.HasOne(x => x.Product)
                    .WithMany()
                    .HasForeignKey(x => x.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }

        private void ConfigureStockMovements(ModelBuilder builder)
        {
            builder.Entity<StockMovement>(entity =>
            {
                entity.HasKey(x => x.StockMovementId);
                entity.Property(x => x.MovementType).HasMaxLength(20).IsRequired().HasDefaultValue("In");
                entity.Property(x => x.Quantity).HasPrecision(18, 2);
                entity.Property(x => x.BalanceBefore).HasPrecision(18, 2);
                entity.Property(x => x.BalanceAfter).HasPrecision(18, 2);
                entity.Property(x => x.Reference).HasMaxLength(60).IsRequired().HasDefaultValue("");
                entity.Property(x => x.Notes).HasMaxLength(300).IsRequired().HasDefaultValue("");
                entity.Property(x => x.PerformedBy).HasMaxLength(150).IsRequired().HasDefaultValue("");
                entity.Property(x => x.UnitCost).HasPrecision(18, 4);
                entity.Property(x => x.TotalCost).HasPrecision(18, 2);
                entity.Property(x => x.MovementDate).IsRequired().HasDefaultValueSql("GETUTCDATE()");
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                entity.HasOne(x => x.Product)
                    .WithMany()
                    .HasForeignKey(x => x.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);

                // A stock-in row must keep its supplier reference, so a supplier that has ever
                // been used cannot be removed out from under the movements that name it.
                entity.HasOne(x => x.Supplier)
                    .WithMany()
                    .HasForeignKey(x => x.SupplierId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Staff attribution is optional and must not block removing an employee.
                entity.HasOne(x => x.RecordedByEmployee)
                    .WithMany()
                    .HasForeignKey(x => x.RecordedByEmployeeId)
                    .OnDelete(DeleteBehavior.SetNull);

                // The ledger is read newest first, usually filtered to one product.
                entity.HasIndex(x => new { x.ProductId, x.MovementDate });
            });
        }

        private void ConfigureEmployees(ModelBuilder builder)
        {
            builder.Entity<Employee>(entity =>
            {
                entity.HasKey(x => x.EmployeeId);
                entity.Property(x => x.EmployeeCode).HasMaxLength(50).IsRequired();
                entity.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
                entity.Property(x => x.LastName).HasMaxLength(100).IsRequired();
                entity.Property(x => x.Position).HasMaxLength(100).IsRequired().HasDefaultValue("");
                entity.Property(x => x.Department).HasMaxLength(100).IsRequired().HasDefaultValue("");
                entity.Property(x => x.Phone).HasMaxLength(20).IsRequired().HasDefaultValue("");
                entity.Property(x => x.Email).HasMaxLength(100).IsRequired().HasDefaultValue("");
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("Active");
                entity.Property(x => x.BasicSalary).HasPrecision(18, 2);
                entity.Property(x => x.HourlyRate).HasPrecision(18, 2);
                entity.Property(x => x.HireDate).IsRequired();
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                // An employee code identifies a person within the tenant, so it must be unique.
                entity.HasIndex(x => x.EmployeeCode).IsUnique();
            });
        }

        private void ConfigurePayrolls(ModelBuilder builder)
        {
            builder.Entity<Payroll>(entity =>
            {
                entity.HasKey(x => x.PayrollId);
                entity.Property(x => x.BasicSalary).HasPrecision(18, 2);
                entity.Property(x => x.Allowances).HasPrecision(18, 2);
                entity.Property(x => x.Deductions).HasPrecision(18, 2);
                entity.Property(x => x.RegularHours).HasPrecision(18, 2);
                entity.Property(x => x.HourlyRate).HasPrecision(18, 2);
                entity.Property(x => x.RegularPay).HasPrecision(18, 2);
                entity.Property(x => x.OvertimeHours).HasPrecision(18, 2);
                entity.Property(x => x.OvertimeRate).HasPrecision(18, 2);
                entity.Property(x => x.OvertimePay).HasPrecision(18, 2);
                entity.Property(x => x.GrossPay).HasPrecision(18, 2);
                entity.Property(x => x.SssDeduction).HasPrecision(18, 2);
                entity.Property(x => x.PhilHealthDeduction).HasPrecision(18, 2);
                entity.Property(x => x.PagIbigDeduction).HasPrecision(18, 2);
                entity.Property(x => x.WithholdingTax).HasPrecision(18, 2);
                entity.Property(x => x.OtherDeductions).HasPrecision(18, 2);
                entity.Property(x => x.NetPay).HasPrecision(18, 2);
                entity.Property(x => x.SssEmployerShare).HasPrecision(18, 2);
                entity.Property(x => x.PhilHealthEmployerShare).HasPrecision(18, 2);
                entity.Property(x => x.PagIbigEmployerShare).HasPrecision(18, 2);
                entity.Property(x => x.EmployerContributions).HasPrecision(18, 2);
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("Draft");
                entity.Property(x => x.Notes).HasMaxLength(300).IsRequired().HasDefaultValue("");

                entity.Property(x => x.ApprovedBy).HasMaxLength(150).IsRequired().HasDefaultValue("");
                entity.Property(x => x.ProcessedBy).HasMaxLength(150).IsRequired().HasDefaultValue("");
                entity.Property(x => x.LastModifiedBy).HasMaxLength(150).IsRequired().HasDefaultValue("");
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                // Deleting an employee would destroy their pay history, so it is blocked.
                entity.HasOne(x => x.Employee)
                    .WithMany(e => e.Payrolls)
                    .HasForeignKey(x => x.EmployeeId)
                    .OnDelete(DeleteBehavior.Restrict);

                // The usual lookup is "this employee's runs, most recent first".
                entity.HasIndex(x => new { x.EmployeeId, x.PeriodStart });
            });
        }

        private void ConfigureAttendances(ModelBuilder builder)
        {
            builder.Entity<Attendance>(entity =>
            {
                entity.HasKey(x => x.AttendanceId);
                entity.Property(x => x.RegularHours).HasPrecision(18, 2);
                entity.Property(x => x.OvertimeHours).HasPrecision(18, 2);
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("Present");
                entity.Property(x => x.Notes).HasMaxLength(300).IsRequired().HasDefaultValue("");
                entity.Property(x => x.RecordedBy).HasMaxLength(150).IsRequired().HasDefaultValue("");
                entity.Property(x => x.ModifiedBy).HasMaxLength(150).IsRequired().HasDefaultValue("");
                entity.Property(x => x.Date).IsRequired();
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                // An employee's attendance history has to survive the employee record itself
                // being removed only through the same guard payroll history already gets -
                // in practice an employee with attendance also has payroll, which is already
                // restricted, but this keeps the rule true on its own terms too.
                entity.HasOne(x => x.Employee)
                    .WithMany(e => e.Attendances)
                    .HasForeignKey(x => x.EmployeeId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Attendance is always looked up "this employee, this date" or "this employee,
                // this period", and a person cannot be recorded twice for the same day.
                entity.HasIndex(x => new { x.EmployeeId, x.Date }).IsUnique();
            });
        }

        private void ConfigureExpenses(ModelBuilder builder)
        {
            builder.Entity<Expense>(entity =>
            {
                entity.HasKey(x => x.ExpenseId);
                entity.Property(x => x.Category).HasMaxLength(50).IsRequired().HasDefaultValue("Other");
                entity.Property(x => x.Description).HasMaxLength(300).IsRequired().HasDefaultValue("");
                entity.Property(x => x.Amount).HasPrecision(18, 2);
                entity.Property(x => x.PaymentMethod).HasMaxLength(50).IsRequired().HasDefaultValue("Cash");
                entity.Property(x => x.ReferenceNo).HasMaxLength(60).IsRequired().HasDefaultValue("");
                entity.Property(x => x.PaidTo).HasMaxLength(150).IsRequired().HasDefaultValue("");
                entity.Property(x => x.RecordedBy).HasMaxLength(150).IsRequired().HasDefaultValue("");
                entity.Property(x => x.ExpenseDate).IsRequired();
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                // Rows written before the column existed were all settled at the time they were
                // entered, so Paid is the honest default rather than leaving them as owed.
                entity.Property(x => x.Status)
                      .HasMaxLength(20).IsRequired()
                      .HasDefaultValue(ExpenseStatuses.Paid);

                // Optional link: removing an employee leaves the expense in place, unattributed.
                entity.HasOne(x => x.RecordedByEmployee)
                    .WithMany()
                    .HasForeignKey(x => x.RecordedByEmployeeId)
                    .OnDelete(DeleteBehavior.SetNull);

                // A supplier that has ever been billed cannot be removed out from under the
                // expense naming them - the same rule stock movements already impose.
                entity.HasOne(x => x.Supplier)
                    .WithMany()
                    .HasForeignKey(x => x.SupplierId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.Account)
                    .WithMany()
                    .HasForeignKey(x => x.AccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.BankAccount)
                    .WithMany()
                    .HasForeignKey(x => x.BankAccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Expense reports are almost always filtered by date, then by category.
                entity.HasIndex(x => x.ExpenseDate);
                entity.HasIndex(x => x.Category);
                entity.HasIndex(x => x.Status);
            });
        }

        // ------------------------------------------------------------------ workforce

        private void ConfigureLeaveRequests(ModelBuilder builder)
        {
            builder.Entity<LeaveRequest>(entity =>
            {
                entity.HasKey(x => x.LeaveRequestId);
                entity.Property(x => x.LeaveType).HasMaxLength(40).IsRequired()
                      .HasDefaultValue(LeaveTypes.Vacation);
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired()
                      .HasDefaultValue(LeaveStatuses.Pending);
                entity.Property(x => x.Days).HasPrecision(18, 2);
                entity.Property(x => x.Reason).HasMaxLength(300).IsRequired().HasDefaultValue("");
                entity.Property(x => x.DecisionNotes).HasMaxLength(300).IsRequired().HasDefaultValue("");
                entity.Property(x => x.RequestedBy).HasMaxLength(150).IsRequired().HasDefaultValue("");
                entity.Property(x => x.DecidedBy).HasMaxLength(150).IsRequired().HasDefaultValue("");
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                // Leave history belongs to the employee and must not be destroyed by removing
                // them - the same protection payroll and attendance already have.
                entity.HasOne(x => x.Employee)
                    .WithMany()
                    .HasForeignKey(x => x.EmployeeId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Payroll asks "what approved leave does this employee have in this period?".
                entity.HasIndex(x => new { x.EmployeeId, x.StartDate });
                entity.HasIndex(x => x.Status);
            });
        }

        // ------------------------------------------------------------------ purchasing

        private void ConfigurePurchases(ModelBuilder builder)
        {
            builder.Entity<Purchase>(entity =>
            {
                entity.HasKey(x => x.PurchaseId);
                entity.Property(x => x.PurchaseNo).HasMaxLength(40).IsRequired();
                entity.Property(x => x.Status).HasMaxLength(30).IsRequired()
                      .HasDefaultValue(PurchaseStatuses.Draft);
                entity.Property(x => x.PaymentStatus).HasMaxLength(20).IsRequired()
                      .HasDefaultValue(SettlementStatuses.Unpaid);
                entity.Property(x => x.SupplierReference).HasMaxLength(60).IsRequired().HasDefaultValue("");
                entity.Property(x => x.Notes).HasMaxLength(300).IsRequired().HasDefaultValue("");
                entity.Property(x => x.ProcessedBy).HasMaxLength(150).IsRequired().HasDefaultValue("");

                entity.Property(x => x.Subtotal).HasPrecision(18, 2);
                entity.Property(x => x.Discount).HasPrecision(18, 2);
                entity.Property(x => x.Tax).HasPrecision(18, 2);
                entity.Property(x => x.Total).HasPrecision(18, 2);
                entity.Property(x => x.AmountPaid).HasPrecision(18, 2);

                entity.Property(x => x.OrderDate).IsRequired();
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                // A supplier with purchase history cannot be deleted: the payable and the
                // stock it delivered would lose the only record of where they came from.
                entity.HasOne(x => x.Supplier)
                    .WithMany()
                    .HasForeignKey(x => x.SupplierId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => x.PurchaseNo).IsUnique();
                entity.HasIndex(x => x.OrderDate);
                entity.HasIndex(x => x.PaymentStatus);
            });

            builder.Entity<PurchaseItem>(entity =>
            {
                entity.HasKey(x => x.PurchaseItemId);
                entity.Property(x => x.Quantity).HasPrecision(18, 2);
                entity.Property(x => x.UnitCost).HasPrecision(18, 2);
                entity.Property(x => x.QuantityReceived).HasPrecision(18, 2);

                entity.HasOne(x => x.Purchase)
                    .WithMany(p => p.Items)
                    .HasForeignKey(x => x.PurchaseId)
                    .OnDelete(DeleteBehavior.Cascade);

                // A product that has ever been ordered keeps its purchase history.
                entity.HasOne(x => x.Product)
                    .WithMany()
                    .HasForeignKey(x => x.ProductId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }

        private void ConfigureSupplierPayments(ModelBuilder builder)
        {
            builder.Entity<SupplierPayment>(entity =>
            {
                entity.HasKey(x => x.SupplierPaymentId);
                entity.Property(x => x.Amount).HasPrecision(18, 2);
                entity.Property(x => x.Method).HasMaxLength(50).IsRequired().HasDefaultValue("Cash");
                entity.Property(x => x.ReferenceNo).HasMaxLength(60).IsRequired().HasDefaultValue("");
                entity.Property(x => x.Notes).HasMaxLength(300).IsRequired().HasDefaultValue("");
                entity.Property(x => x.ProcessedBy).HasMaxLength(150).IsRequired().HasDefaultValue("");
                entity.Property(x => x.PaymentDate).IsRequired();
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                entity.HasOne(x => x.Supplier)
                    .WithMany()
                    .HasForeignKey(x => x.SupplierId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Supplier -> Purchase and Supplier -> SupplierPayment are both restricted, so
                // this leg stays NoAction to keep SQL Server from seeing two paths into the
                // same table. The service clears the link before deleting a purchase.
                entity.HasOne(x => x.Purchase)
                    .WithMany()
                    .HasForeignKey(x => x.PurchaseId)
                    .OnDelete(DeleteBehavior.NoAction);

                entity.HasIndex(x => x.PaymentDate);
                entity.HasIndex(x => x.SupplierId);
            });
        }

        // ------------------------------------------------------------------ returns

        private void ConfigureSaleReturns(ModelBuilder builder)
        {
            builder.Entity<SaleReturn>(entity =>
            {
                entity.HasKey(x => x.SaleReturnId);
                entity.Property(x => x.ReturnNo).HasMaxLength(40).IsRequired();
                entity.Property(x => x.Reason).HasMaxLength(40).IsRequired()
                      .HasDefaultValue(ReturnReasons.Other);
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("Completed");
                entity.Property(x => x.RefundMethod).HasMaxLength(50).IsRequired().HasDefaultValue("Cash");
                entity.Property(x => x.Notes).HasMaxLength(300).IsRequired().HasDefaultValue("");
                entity.Property(x => x.ProcessedBy).HasMaxLength(150).IsRequired().HasDefaultValue("");
                entity.Property(x => x.Subtotal).HasPrecision(18, 2);
                entity.Property(x => x.RefundAmount).HasPrecision(18, 2);
                entity.Property(x => x.ReturnDate).IsRequired();
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                // A sale with returns against it cannot be deleted - the return would be left
                // describing nothing, and the stock it restored would be unexplained.
                entity.HasOne(x => x.Sale)
                    .WithMany()
                    .HasForeignKey(x => x.SaleId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Member -> Sale -> SaleReturn is already a path, so this one stays NoAction.
                // Null when the original sale was a walk-in.
                entity.HasOne(x => x.Member)
                    .WithMany()
                    .HasForeignKey(x => x.MemberId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.NoAction);

                entity.HasIndex(x => x.ReturnNo).IsUnique();
                entity.HasIndex(x => x.ReturnDate);
            });

            builder.Entity<SaleReturnItem>(entity =>
            {
                entity.HasKey(x => x.SaleReturnItemId);
                entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
                entity.Property(x => x.UnitCost).HasPrecision(18, 2);

                entity.HasOne(x => x.Return)
                    .WithMany(r => r.Items)
                    .HasForeignKey(x => x.SaleReturnId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(x => x.SaleItem)
                    .WithMany()
                    .HasForeignKey(x => x.SaleItemId)
                    .OnDelete(DeleteBehavior.NoAction);

                entity.HasOne(x => x.Product)
                    .WithMany()
                    .HasForeignKey(x => x.ProductId)
                    .OnDelete(DeleteBehavior.Restrict);

                // "Has this line already been returned, and how much of it?" is the question
                // asked before every return is accepted.
                entity.HasIndex(x => x.SaleItemId);
            });
        }

        // ------------------------------------------------------------------ finance

        private void ConfigureAccounts(ModelBuilder builder)
        {
            builder.Entity<Account>(entity =>
            {
                entity.HasKey(x => x.AccountId);
                entity.Property(x => x.AccountCode).HasMaxLength(20).IsRequired();
                entity.Property(x => x.AccountName).HasMaxLength(150).IsRequired();
                entity.Property(x => x.AccountType).HasMaxLength(20).IsRequired();
                entity.Property(x => x.AccountSubType).HasMaxLength(60).IsRequired().HasDefaultValue("");
                entity.Property(x => x.SystemKey).HasMaxLength(60).IsRequired().HasDefaultValue("");
                entity.Property(x => x.Description).HasMaxLength(300).IsRequired().HasDefaultValue("");
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                // Two accounts sharing a code makes a trial balance ambiguous.
                entity.HasIndex(x => x.AccountCode).IsUnique();

                // The posting rules look accounts up by system key, so exactly one account may
                // claim each role. Filtered so the many operator-created accounts, which have
                // no key, do not all collide on the empty string.
                entity.HasIndex(x => x.SystemKey)
                      .IsUnique()
                      .HasFilter("[SystemKey] <> ''");

                entity.HasOne(x => x.ParentAccount)
                    .WithMany()
                    .HasForeignKey(x => x.ParentAccountId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }

        private void ConfigureJournal(ModelBuilder builder)
        {
            builder.Entity<JournalEntry>(entity =>
            {
                entity.HasKey(x => x.JournalEntryId);
                entity.Property(x => x.EntryNo).HasMaxLength(40).IsRequired();
                entity.Property(x => x.Reference).HasMaxLength(60).IsRequired().HasDefaultValue("");
                entity.Property(x => x.Memo).HasMaxLength(300).IsRequired().HasDefaultValue("");
                entity.Property(x => x.Source).HasMaxLength(30).IsRequired()
                      .HasDefaultValue(JournalSources.Manual);
                entity.Property(x => x.SourceModule).HasMaxLength(40).IsRequired().HasDefaultValue("");
                entity.Property(x => x.SourceEntityName).HasMaxLength(60).IsRequired().HasDefaultValue("");
                entity.Property(x => x.SourceEntityId).HasMaxLength(40);
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired()
                      .HasDefaultValue(JournalStatuses.Posted);
                entity.Property(x => x.ProcessedBy).HasMaxLength(150).IsRequired().HasDefaultValue("");
                entity.Property(x => x.TotalDebit).HasPrecision(18, 2);
                entity.Property(x => x.TotalCredit).HasPrecision(18, 2);
                entity.Property(x => x.EntryDate).IsRequired();
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                entity.HasIndex(x => x.EntryNo).IsUnique();
                entity.HasIndex(x => x.EntryDate);
                entity.HasIndex(x => new { x.PeriodYear, x.PeriodMonth });

                // "Has this sale already been posted?" is asked before every automatic
                // posting, and is what makes posting idempotent rather than duplicating the
                // ledger when an operation is retried.
                entity.HasIndex(x => new { x.SourceEntityName, x.SourceEntityId });
            });

            builder.Entity<JournalEntryLine>(entity =>
            {
                entity.HasKey(x => x.JournalEntryLineId);
                entity.Property(x => x.Description).HasMaxLength(300).IsRequired().HasDefaultValue("");
                entity.Property(x => x.Debit).HasPrecision(18, 2);
                entity.Property(x => x.Credit).HasPrecision(18, 2);

                entity.HasOne(x => x.Entry)
                    .WithMany(e => e.Lines)
                    .HasForeignKey(x => x.JournalEntryId)
                    .OnDelete(DeleteBehavior.Cascade);

                // An account with postings against it cannot be deleted. Deleting one would
                // silently change every balance it ever appeared in.
                entity.HasOne(x => x.Account)
                    .WithMany()
                    .HasForeignKey(x => x.AccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                // The general ledger is read one account at a time, in date order - which the
                // entry supplies, so the index leads on the account.
                entity.HasIndex(x => x.AccountId);
                entity.HasIndex(x => x.MemberId);
                entity.HasIndex(x => x.SupplierId);
            });
        }

        private void ConfigureFinancialPeriods(ModelBuilder builder)
        {
            builder.Entity<FinancialPeriod>(entity =>
            {
                entity.HasKey(x => x.FinancialPeriodId);
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired()
                      .HasDefaultValue(PeriodStatuses.Open);
                entity.Property(x => x.ClosedBy).HasMaxLength(150).IsRequired().HasDefaultValue("");
                entity.Property(x => x.Notes).HasMaxLength(300).IsRequired().HasDefaultValue("");
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                // One row per month, or "is March closed?" has more than one answer.
                entity.HasIndex(x => new { x.Year, x.Month }).IsUnique();
            });
        }

        private void ConfigureBudgets(ModelBuilder builder)
        {
            builder.Entity<Budget>(entity =>
            {
                entity.HasKey(x => x.BudgetId);
                entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired()
                      .HasDefaultValue(BudgetStatuses.Draft);
                entity.Property(x => x.Notes).HasMaxLength(300).IsRequired().HasDefaultValue("");
                entity.Property(x => x.ApprovedBy).HasMaxLength(150).IsRequired().HasDefaultValue("");
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                entity.HasIndex(x => x.Year);
            });

            builder.Entity<BudgetLine>(entity =>
            {
                entity.HasKey(x => x.BudgetLineId);
                entity.Property(x => x.Amount).HasPrecision(18, 2);
                entity.Property(x => x.Notes).HasMaxLength(200).IsRequired().HasDefaultValue("");

                entity.HasOne(x => x.Budget)
                    .WithMany(b => b.Lines)
                    .HasForeignKey(x => x.BudgetId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(x => x.Account)
                    .WithMany()
                    .HasForeignKey(x => x.AccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                // One figure per account per month, or Budget vs Actual double-counts.
                entity.HasIndex(x => new { x.BudgetId, x.AccountId, x.Month }).IsUnique();
            });
        }

        private void ConfigureBanking(ModelBuilder builder)
        {
            builder.Entity<BankAccount>(entity =>
            {
                entity.HasKey(x => x.BankAccountId);
                entity.Property(x => x.AccountName).HasMaxLength(150).IsRequired();
                entity.Property(x => x.BankName).HasMaxLength(150).IsRequired().HasDefaultValue("");
                entity.Property(x => x.AccountNumber).HasMaxLength(60).IsRequired().HasDefaultValue("");
                entity.Property(x => x.Kind).HasMaxLength(20).IsRequired()
                      .HasDefaultValue(CashAccountKinds.Bank);
                entity.Property(x => x.Notes).HasMaxLength(300).IsRequired().HasDefaultValue("");
                entity.Property(x => x.OpeningBalance).HasPrecision(18, 2);
                entity.Property(x => x.CurrentBalance).HasPrecision(18, 2);
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                entity.HasOne(x => x.LedgerAccount)
                    .WithMany()
                    .HasForeignKey(x => x.LedgerAccountId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<BankTransaction>(entity =>
            {
                entity.HasKey(x => x.BankTransactionId);
                entity.Property(x => x.Direction).HasMaxLength(10).IsRequired()
                      .HasDefaultValue(BankTransactionDirections.In);
                entity.Property(x => x.Amount).HasPrecision(18, 2);
                entity.Property(x => x.BalanceAfter).HasPrecision(18, 2);
                entity.Property(x => x.Reference).HasMaxLength(60).IsRequired().HasDefaultValue("");
                entity.Property(x => x.Description).HasMaxLength(300).IsRequired().HasDefaultValue("");
                entity.Property(x => x.ReconciledBy).HasMaxLength(150).IsRequired().HasDefaultValue("");
                entity.Property(x => x.PerformedBy).HasMaxLength(150).IsRequired().HasDefaultValue("");
                entity.Property(x => x.TransactionDate).IsRequired();
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                entity.HasOne(x => x.BankAccount)
                    .WithMany(a => a.Transactions)
                    .HasForeignKey(x => x.BankAccountId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Reconciliation reads "this account, this period, not yet reconciled".
                entity.HasIndex(x => new { x.BankAccountId, x.TransactionDate });
                entity.HasIndex(x => x.IsReconciled);
            });
        }

        // ------------------------------------------------------------------ supporting

        private void ConfigureMemberNotes(ModelBuilder builder)
        {
            builder.Entity<MemberNote>(entity =>
            {
                entity.HasKey(x => x.MemberNoteId);
                entity.Property(x => x.Category).HasMaxLength(30).IsRequired()
                      .HasDefaultValue(MemberNoteCategories.General);
                entity.Property(x => x.Note).HasMaxLength(2000).IsRequired();
                entity.Property(x => x.Author).HasMaxLength(150).IsRequired().HasDefaultValue("");
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                // Notes are about the member and go when the member does - unlike the
                // financial history, which is why a member with payments cannot be deleted
                // in the first place.
                entity.HasOne(x => x.Member)
                    .WithMany(m => m.Notes)
                    .HasForeignKey(x => x.MemberId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(x => new { x.MemberId, x.CreatedAt });
            });
        }

        private void ConfigureTenantSettings(ModelBuilder builder)
        {
            builder.Entity<TenantSetting>(entity =>
            {
                entity.HasKey(x => x.TenantSettingId);
                entity.Property(x => x.SettingKey).HasMaxLength(100).IsRequired();
                entity.Property(x => x.Value).HasMaxLength(1000).IsRequired().HasDefaultValue("");
                entity.Property(x => x.UpdatedBy).HasMaxLength(150).IsRequired().HasDefaultValue("");
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                // One row per setting. A second row for the same key means two answers to
                // "what is the shift length?", and whichever the query happened to read wins.
                entity.HasIndex(x => x.SettingKey).IsUnique();
            });
        }
    }
}
