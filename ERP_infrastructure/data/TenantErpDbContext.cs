using Microsoft.EntityFrameworkCore;
using ERP_domain.entities;
using ERP_infrastructure.services;

namespace ERP_infrastructure.data
{
    public class TenantErpDbContext : DbContext
    {
        private readonly ICurrentUserAccessor? _actor;

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
            ICurrentUserAccessor? actor = null)
            : base(options)
        {
            _actor = actor;
        }

        public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

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

        // Workforce and cost DbSets
        public DbSet<Employee> Employees => Set<Employee>();
        public DbSet<Payroll> Payrolls => Set<Payroll>();
        public DbSet<Expense> Expenses => Set<Expense>();

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
        /// Guards against the audit write auditing itself, which would not terminate.
        /// </summary>
        private bool _writingAudit;

        public override int SaveChanges()
        {
            StampAuditFields();

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
            ConfigureInventory(builder);
            ConfigureStockMovements(builder);

            // New FITCORE configurations
            ConfigureMembers(builder);
            ConfigureMembershipPlans(builder);
            ConfigureSubscriptions(builder);
            ConfigureSales(builder);
            ConfigureSaleItems(builder);
            ConfigurePayments(builder);

            // Workforce and cost configurations
            ConfigureEmployees(builder);
            ConfigurePayrolls(builder);
            ConfigureExpenses(builder);
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
                entity.Property(x => x.JoinDate).IsRequired().HasDefaultValueSql("GETUTCDATE()");
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");
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

                entity.HasOne(x => x.Member)
                    .WithMany(x => x.Subscriptions)
                    .HasForeignKey(x => x.MemberId)
                    .OnDelete(DeleteBehavior.Cascade);

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

                entity.HasOne(x => x.Member)
                    .WithMany(x => x.Sales)
                    .HasForeignKey(x => x.MemberId)
                    .OnDelete(DeleteBehavior.Cascade);

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
                entity.Property(x => x.PaymentDate).IsRequired().HasDefaultValueSql("GETUTCDATE()");
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                // Deleting a member clears their payment history. The subscription leg would form
                // a second cascade path into Payments, which SQL Server rejects, so it is NoAction.
                entity.HasOne(x => x.Member)
                    .WithMany(x => x.Payments)
                    .HasForeignKey(x => x.MemberId)
                    .OnDelete(DeleteBehavior.Cascade);

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
            });
        }

        private void ConfigureInventory(ModelBuilder builder)
        {
            builder.Entity<Inventory>(entity =>
            {
                entity.HasKey(x => x.InventoryId);
                entity.Property(x => x.QuantityOnHand).HasPrecision(18, 2);
                entity.Property(x => x.ReorderLevel).HasPrecision(18, 2);

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
                entity.Property(x => x.MovementDate).IsRequired().HasDefaultValueSql("GETUTCDATE()");
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                entity.HasOne(x => x.Product)
                    .WithMany()
                    .HasForeignKey(x => x.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);

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
                entity.Property(x => x.OvertimeHours).HasPrecision(18, 2);
                entity.Property(x => x.OvertimeRate).HasPrecision(18, 2);
                entity.Property(x => x.OvertimePay).HasPrecision(18, 2);
                entity.Property(x => x.GrossPay).HasPrecision(18, 2);
                entity.Property(x => x.NetPay).HasPrecision(18, 2);
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("Draft");
                entity.Property(x => x.Notes).HasMaxLength(300).IsRequired().HasDefaultValue("");
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
                entity.Property(x => x.ExpenseDate).IsRequired();
                entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

                // Optional link: removing an employee leaves the expense in place, unattributed.
                entity.HasOne(x => x.RecordedByEmployee)
                    .WithMany()
                    .HasForeignKey(x => x.RecordedByEmployeeId)
                    .OnDelete(DeleteBehavior.SetNull);

                // Expense reports are almost always filtered by date.
                entity.HasIndex(x => x.ExpenseDate);
            });
        }
    }
}
