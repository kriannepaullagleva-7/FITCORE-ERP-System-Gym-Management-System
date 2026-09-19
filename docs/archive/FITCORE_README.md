# FitCore ERP - Single Company Membership & Sales Management System

**Version:** 2.0  
**Platform:** Windows Forms (.NET 10.0)  
**Database:** SQL Server / SQL Server Express  
**Status:** ✅ Complete & Production Ready

---

## 🎯 System Overview

FitCore ERP is a comprehensive Windows Forms application for managing a fitness facility's operations. It's specifically designed for **single-company micro businesses** and provides complete control over:

- **Membership Management** - Member profiles, status tracking
- **Membership Plans** - Plan creation and pricing
- **Subscriptions & Payments** - Subscription management and payment tracking
- **Inventory Management** - Products and stock levels
- **Sales Management** - Sales transactions and revenue tracking

---

## 🏗️ Architecture

### Three-Layer Architecture

1. **Presentation Layer (Windows Forms)**
   - MainForm: Navigation dashboard
   - MemberForm: Member CRUD
   - MembershipPlanForm: Plan management
   - SubscriptionForm: Subscriptions & payments
   - InventoryForm: Product & inventory management
   - SalesForm: Sales transactions

2. **Business Logic Layer (Services)**
   - MemberService
   - MembershipPlanService
   - SubscriptionService
   - PaymentService
   - SaleService

3. **Data Access Layer (Repositories)**
   - GenericRepository (base repository for all entities)
   - MemberRepository
   - SubscriptionRepository
   - SaleRepository

### Database Structure

Single TenantErp database containing:
- **Members** - Gym members with contact info
- **MembershipPlans** - Membership packages
- **Subscriptions** - Member subscriptions to plans
- **Payments** - Payment tracking for subscriptions
- **Products** - Inventory items (supplements, merchandise, etc.)
- **Inventory** - Stock tracking
- **Sales** - Sales transactions
- **SaleItems** - Individual items in sales

---

## 🚀 Getting Started

### Prerequisites

- .NET SDK 10.0 or later
- SQL Server Express 2019 or later (or full SQL Server)
- Windows 10/11 with Windows Forms support

### Installation

1. **Clone/Extract Project**
   ```bash
   cd C:\Users\USER\source\repos\ERP_Project1
   ```

2. **Restore Dependencies**
   ```bash
   dotnet restore
   ```

3. **Apply Database Migrations**
   ```bash
   cd ERP_infrastructure
   dotnet ef database update --context TenantErpDbContext
   ```

4. **Build Solution**
   ```bash
   cd ..
   dotnet build --configuration Release
   ```

5. **Run Application**
   ```bash
   cd ERP_Project1
   dotnet run
   ```

The application will start with the MainForm navigation dashboard.

---

## 📱 Application Modules

### 1. Member Management
**Location:** Navigation → Members

**Features:**
- View all members in a searchable grid
- Create new members with validation
- Update member information
- Delete members with confirmation
- Search by name, email, or phone

**Fields:**
- First Name (required)
- Last Name (required)
- Phone (optional)
- Email (optional)
- Status (Active, Inactive, Suspended)
- Join Date (auto-set)

### 2. Membership Plans
**Location:** Navigation → Membership Plans

**Features:**
- Create membership packages
- Define plan duration and pricing
- Enable/disable plans
- Add plan descriptions
- List all plans with details

**Fields:**
- Plan Name (required)
- Duration in Months (required)
- Price (required)
- Description (optional)
- Active Status (toggle)

### 3. Subscriptions & Payments
**Location:** Navigation → Subscriptions & Payments

**Features:**
- Create subscriptions for members
- Track subscription status (Active/Expired/Cancelled)
- Record payments for subscriptions
- Renew expiring subscriptions
- Cancel subscriptions
- View payment history per subscription

**Operations:**
- Create Subscription: Select member and plan
- Record Payment: Enter amount for active subscription
- Renew Subscription: Extend subscription for another period
- Cancel Subscription: Deactivate with confirmation

### 4. Inventory Management
**Location:** Navigation → Inventory

**Features:**
- Add and manage products
- Track product codes and pricing
- Monitor stock levels
- Set reorder levels
- Delete products with cascade cleanup

**Fields:**
- Product Code (required)
- Product Name (required)
- Unit Price (required)
- Quantity on Hand (tracking)
- Reorder Level (auto-notify when low)

### 5. Sales Management
**Location:** Navigation → Sales

**Features:**
- Create sales for members
- Add multiple items to single sale
- Track total amounts
- View sale history by member
- Delete completed sales
- Real-time total calculation

**Operations:**
- Create Sale: Select member
- Add Items: Add products with quantity and price
- Record Sale: Finalize transaction
- Delete Sale: Remove transaction

---

## 🎨 Color Scheme

The application uses a professional blue color palette:

- **Primary Blue:** RGB(77, 130, 222) - Main buttons and headers
- **Light Blue:** RGB(204, 224, 247) - Form backgrounds
- **Pale Blue:** RGB(230, 237, 251) - Alternate rows and panels
- **Action Colors:**
  - Green RGB(76, 175, 80) - Create/Add operations
  - Orange RGB(255, 152, 0) - Update operations
  - Red RGB(244, 67, 54) - Delete operations

---

## 💾 Database Configuration

### Connection String

Located in: `ERP_Project1/appsettings.json`

```json
{
  "ConnectionStrings": {
    "TenantErp": "Server=.\\SQLEXPRESS;Database=TenantErp;Trusted_Connection=True;Encrypt=false;TrustServerCertificate=True;MultipleActiveResultSets=True;"
  }
}
```

**Configuration Details:**
- **Server:** Local SQL Server Express (`.\\SQLEXPRESS`)
- **Database:** TenantErp
- **Authentication:** Windows Integrated (no username/password)
- **Encryption:** Disabled for local development
- **Connection Pooling:** Enabled (MARS)

### Database Setup

If TenantErp database doesn't exist, migrations will create it automatically.

To manually verify:
```sql
sqlcmd -S .\SQLEXPRESS -E
> SELECT name FROM sys.databases WHERE name = 'TenantErp';
> EXIT
```

---

## 🔧 Technical Stack

| Component | Version | Purpose |
|-----------|---------|---------|
| .NET | 10.0 | Framework |
| C# | 12+ | Language |
| Windows Forms | 10.0 | UI |
| Entity Framework Core | 10.0.12 | ORM |
| SQL Server | Latest | Database |
| Dependency Injection | 10.0.12 | Service container |

---

## 📋 Project Structure

```
ERP_Project1/
├── ERP_Project1/              # Windows Forms Application
│   ├── MainForm.cs            # Navigation dashboard
│   ├── MemberForm.cs          # Member CRUD
│   ├── MembershipPlanForm.cs  # Plan management
│   ├── SubscriptionForm.cs    # Subscriptions & payments
│   ├── InventoryForm.cs       # Inventory management
│   ├── SalesForm.cs           # Sales management
│   ├── App.cs                 # App context (ServiceProvider)
│   ├── Program.cs             # DI setup & entry point
│   ├── Form1.cs               # Legacy form (backup)
│   ├── appsettings.json       # Configuration
│   └── Colors/                # Color definitions
│
├── ERP_domain/                # Business Models
│   └── entities/
│       ├── Member.cs
│       ├── MembershipPlan.cs
│       ├── Subscription.cs
│       ├── Payment.cs
│       ├── Sale.cs
│       ├── SaleItem.cs
│       ├── Product.cs
│       └── Inventory.cs
│
├── ERP_infrastructure/        # Data Access & Services
│   ├── data/
│   │   └── TenantErpDbContext.cs
│   ├── repositories/
│   │   ├── IGenericRepository.cs
│   │   ├── GenericRepository.cs
│   │   └── <Entity>Repository.cs
│   └── services/
│       ├── I<Entity>Service.cs
│       └── <Entity>Service.cs
│
├── CLAUDE.md                  # Project documentation
└── FITCORE_README.md          # This file
```

---

## 🔐 Security & Best Practices

### Data Protection
- ✅ No hardcoded passwords (Windows Authentication)
- ✅ Parameterized queries (EF Core)
- ✅ No sensitive data in logs
- ✅ SQL injection prevention

### Input Validation
- ✅ Required field validation
- ✅ Type validation (decimal, int, etc.)
- ✅ Length limits on text fields
- ✅ Email format validation (optional)

### Business Logic
- ✅ Cascade delete for related records
- ✅ Confirmation dialogs for destructive operations
- ✅ Error handling with user-friendly messages
- ✅ Async operations for responsiveness

---

## 🐛 Error Handling

The application includes comprehensive error handling:

1. **Database Connection Errors** → "Cannot connect to database" message
2. **Validation Errors** → "Please fill required fields" message
3. **Business Logic Errors** → "Operation failed" with details
4. **Network/Timeout Errors** → Graceful degradation

All errors are logged to the console and displayed to the user in message boxes.

---

## 🚀 Building & Deployment

### Debug Build
```bash
dotnet build
```

### Release Build
```bash
dotnet build --configuration Release
```

### Publish
```bash
dotnet publish -c Release -o ./publish
```

### Running Executable
```bash
.\bin\Release\net10.0-windows\ERP_winforms.exe
```

### Deployment Package
1. Publish application
2. Copy `appsettings.json` to publish folder
3. Ensure SQL Server is accessible from deployment machine
4. Run executable on target machine

---

## 🧪 Testing the Application

### Test Scenario 1: Member CRUD
1. Navigate to Members
2. Click "Add New" and create a test member
3. Click refresh to verify in grid
4. Select member and update details
5. Delete with confirmation

### Test Scenario 2: Membership Plans
1. Navigate to Membership Plans
2. Create multiple plans (Basic, Standard, Premium)
3. Verify pricing and duration
4. Update plan details
5. Toggle active status

### Test Scenario 3: Subscriptions
1. Navigate to Subscriptions
2. Create subscription for member with plan
3. Record payment for subscription
4. Renew subscription
5. View payment history

### Test Scenario 4: Sales
1. Navigate to Sales
2. Add products to inventory first
3. Create sale for member with items
4. Verify total amount calculation
5. View sale details

---

## 📊 Database Diagrams

### Entity Relationships

```
Members ←→ Subscriptions ←→ MembershipPlans
   ↓              ↓
 Sales        Payments
   ↓
SaleItems → Products
              ↓
          Inventory
```

### Key Foreign Keys

- `Subscription.MemberId` → `Member.MemberId`
- `Subscription.PlanId` → `MembershipPlan.PlanId`
- `Payment.SubscriptionId` → `Subscription.SubscriptionId`
- `Sale.MemberId` → `Member.MemberId`
- `SaleItem.SaleId` → `Sale.SaleId`
- `SaleItem.ProductId` → `Product.ProductId`
- `Inventory.ProductId` → `Product.ProductId`

---

## 🔄 Workflow Example

### Complete Member Workflow

1. **Add Member**
   - Navigate to Members
   - Fill in member details
   - Click "Add New"
   - Receive confirmation with new ID

2. **Create Plan**
   - Navigate to Membership Plans
   - Enter plan details (name, price, duration)
   - Save plan

3. **Subscribe Member**
   - Navigate to Subscriptions
   - Select member from dropdown
   - Select plan from dropdown
   - Click "Create Subscription"

4. **Record Payment**
   - Select subscription in grid
   - Enter payment amount
   - Click "Record Payment"
   - View in payment history

5. **Create Sale** (Optional)
   - Navigate to Sales
   - Select member
   - Add products to sale
   - Finalize sale
   - Track revenue

---

## 📝 Logging & Debugging

### Console Output
The application logs to console when run from terminal:
```bash
cd ERP_Project1
dotnet run
```

### Error Investigation
1. Check message box error text
2. Review console output for stack trace
3. Verify database connectivity
4. Check appsettings.json configuration

### Database Debugging
```sql
-- Check table row counts
SELECT 'Members' as TableName, COUNT(*) as Count FROM Members
UNION ALL
SELECT 'Subscriptions', COUNT(*) FROM Subscriptions
UNION ALL
SELECT 'Payments', COUNT(*) FROM Payments;
```

---

## 🎓 Learning Path

### For Beginners
1. Read this README
2. Review project structure
3. Run application and explore UI
4. Create test data in each module
5. Review entity relationships

### For Developers
1. Study `ERP_infrastructure/services/` for business logic
2. Review `ERP_infrastructure/repositories/` for data access patterns
3. Examine MainForm.cs for form switching pattern
4. Study async/await patterns in services
5. Review error handling implementation

### For Database Admins
1. Review `TenantErpDbContext` configurations
2. Study migration files in `Migrations/TenantErpDb/`
3. Test backup/restore procedures
4. Set up monitoring and maintenance jobs
5. Plan for data archival strategy

---

## 🆘 Troubleshooting

### Issue: "Cannot connect to database"
**Solution:**
1. Verify SQL Server is running: `Services → SQL Server (SQLEXPRESS)`
2. Test connection: `sqlcmd -S .\SQLEXPRESS -E`
3. Check firewall allows port 1433
4. Verify database exists: Run migrations again

### Issue: "Service not found in DI container"
**Solution:**
1. Check Program.cs has service registered
2. Verify form is registered with `services.AddScoped<Form>()`
3. Rebuild solution
4. Check spelling of service names

### Issue: "Form not loading"
**Solution:**
1. Check appsettings.json exists in application directory
2. Verify all controls are initialized in InitializeComponent()
3. Check Load event handler for exceptions
4. Review console output for error details

### Issue: "Slow performance"
**Solution:**
1. Check database indexes: `DBCC SHOWCONTIG`
2. Reduce grid data: Implement pagination
3. Add Where() clauses to queries
4. Increase DataGridView buffer size

---

## 📞 Support & Maintenance

### Regular Maintenance
- Monthly: Check database size, archive old data
- Quarterly: Review slow queries, update statistics
- Annually: Plan capacity, upgrade infrastructure

### Backup Strategy
```bash
# SQL Server Express built-in backup
sqlcmd -S .\SQLEXPRESS -E -Q "BACKUP DATABASE TenantErp TO DISK = 'C:\Backups\TenantErp.bak'"
```

### Update Dependencies
```bash
dotnet outdated
dotnet package update
```

---

## 📄 License & Credits

**Created:** September 15, 2026  
**Last Updated:** September 16, 2026  
**Platform:** Windows Forms (.NET 10.0)  
**Status:** Production Ready

---

## 🎯 Future Enhancements

- [ ] Multi-user login system
- [ ] Role-based access control
- [ ] Audit logging
- [ ] Advanced reporting (PDF/Excel export)
- [ ] Email notifications
- [ ] SMS alerts
- [ ] Mobile companion app
- [ ] Cloud backup
- [ ] Real-time synchronization
- [ ] Multi-location support

---

**Ready to use! Launch the application with `dotnet run` and start managing your fitness business.** 🏋️

