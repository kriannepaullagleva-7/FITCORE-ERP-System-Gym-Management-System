# FitCore ERP - Quick Reference Guide

A fast lookup guide for developers working with the FitCore ERP system.

---

## 🚀 Quick Start

### Build & Run
```bash
# Build
cd C:\Users\USER\source\repos\ERP_Project1
dotnet build

# Run Forms App
cd ERP_Project1
dotnet run

# Run API
cd ERP_api
dotnet run --urls "https://localhost:7000"
```

### Database
```
Master: db68434.public.databaseasp.net (MasterErp)
Tenant: db68433.public.databaseasp.net (TenantErp)
```

---

## 📁 Project Structure

```
ERP_Project1/               Windows Forms (Main UI)
ERP_domain/                Entities (Models)
ERP_infrastructure/        Services + Repositories
ERP_api/                   ASP.NET Core API
ERP_UI/                    Blazor (Optional)
```

---

## 🎨 MVC Pattern Quick Reference

### MODEL (Data Layer)
```csharp
// Location: ERP_infrastructure/services/
public class MemberService {
    public async Task<Member> CreateMemberAsync(string firstName, ...) {
        // Validation
        // Database call via repository
        // Return Member
    }
}
```

### VIEW (UI Layer)
```csharp
// Location: ERP_Project1/Form1.cs
private async void btnAdd_Click(object? sender, EventArgs e) {
    var member = await _memberService.CreateMemberAsync(...);
    MessageBox.Show("Success");
    await LoadMembersAsync();  // Refresh grid
}
```

### CONTROLLER (Event Handlers)
```csharp
// Same as View file - handles user events
// Calls services and updates display
```

---

## 📊 Core Services

### 1. MemberService
```csharp
await _memberService.GetAllMembersAsync()
await _memberService.GetMemberByIdAsync(id)
await _memberService.CreateMemberAsync(firstName, lastName, phone, email)
await _memberService.UpdateMemberAsync(id, firstName, lastName, phone, email, status)
await _memberService.DeleteMemberAsync(id)  // Returns MemberDeleteResult
await _memberService.GetMemberHistoryCountsAsync(id)  // Check for related records
```

### 2. MembershipPlanService
```csharp
await _planService.GetAllPlansAsync()
await _planService.GetPlanByIdAsync(id)
await _planService.CreatePlanAsync(name, description, price)
await _planService.UpdatePlanAsync(id, ...)
await _planService.DeletePlanAsync(id)
```

### 3. SubscriptionService
```csharp
await _subService.GetAllSubscriptionsAsync()
await _subService.CreateSubscriptionAsync(memberId, planId, startDate)
await _subService.RenewSubscriptionAsync(id)
await _subService.CancelSubscriptionAsync(id)
```

### 4. Other Services
```
PaymentService      → Record and track payments
SaleService         → Create and manage sales
ProductService      → Manage product catalog
InventoryService    → Track stock levels
DashboardService    → Calculate KPIs and stats
```

---

## 🏗️ Adding a New Feature

### Step 1: Create Entity
```csharp
// ERP_domain/entities/NewEntity.cs
public class NewEntity {
    public int Id { get; set; }
    public string Name { get; set; }
    // Add properties
}
```

### Step 2: Update DbContext
```csharp
// ERP_infrastructure/data/TenantErpDbContext.cs
public DbSet<NewEntity> NewEntities { get; set; }
```

### Step 3: Create Repository
```csharp
// ERP_infrastructure/repositories/INewEntityRepository.cs
public interface INewEntityRepository {
    Task<NewEntity> GetByIdAsync(int id);
    Task<List<NewEntity>> GetAllAsync();
    Task<NewEntity> AddAsync(NewEntity entity);
    Task<NewEntity> UpdateAsync(NewEntity entity);
    Task DeleteAsync(int id);
}

// ERP_infrastructure/repositories/NewEntityRepository.cs
public class NewEntityRepository : GenericRepository<NewEntity>, INewEntityRepository {
    public NewEntityRepository(TenantErpDbContext context) : base(context) { }
}
```

### Step 4: Create Service
```csharp
// ERP_infrastructure/services/INewEntityService.cs
public interface INewEntityService {
    Task<List<NewEntity>> GetAllAsync();
    // Add methods
}

// ERP_infrastructure/services/NewEntityService.cs
public class NewEntityService : INewEntityService {
    private readonly INewEntityRepository _repository;
    
    public NewEntityService(INewEntityRepository repository) {
        _repository = repository;
    }
    
    public async Task<List<NewEntity>> GetAllAsync() {
        return await _repository.GetAllAsync();
    }
}
```

### Step 5: Register in DI
```csharp
// ERP_Project1/Program.cs
services.AddScoped<INewEntityRepository, NewEntityRepository>();
services.AddScoped<INewEntityService, NewEntityService>();
```

### Step 6: Create UI Form
```csharp
// ERP_Project1/NewEntityForm.cs
public partial class NewEntityForm : Form {
    private readonly INewEntityService _service;
    
    public NewEntityForm(INewEntityService service) {
        _service = service;
        InitializeComponent();
    }
    
    private async void Form_Load(object? sender, EventArgs e) {
        var entities = await _service.GetAllAsync();
        // Bind to grid
    }
}
```

### Step 7: Create Migration
```bash
cd ERP_infrastructure
dotnet ef migrations add "AddNewEntity" --context TenantErpDbContext
dotnet ef database update --context TenantErpDbContext
```

---

## 📝 Common Patterns

### Loading Data into Grid
```csharp
private async Task LoadDataAsync() {
    try {
        var items = await _service.GetAllAsync();
        grid.DataSource = items
            .Select(x => new {
                x.Id,
                x.Name,
                x.Description
            })
            .ToList();
    }
    catch (Exception ex) {
        ShowError("Error loading data", ex);
    }
}
```

### Adding Record
```csharp
private async void btnAdd_Click(object? sender, EventArgs e) {
    if (!ValidateForm(out var name, out var description))
        return;
    
    SetBusy(true);
    try {
        var item = await _service.CreateAsync(name, description);
        MessageBox.Show($"Created. ID: {item.Id}", "Success");
        await LoadDataAsync();
    }
    catch (ValidationException ex) {
        MessageBox.Show(ex.Message, "Validation Error");
    }
    finally {
        SetBusy(false);
    }
}
```

### Deleting Record with Validation
```csharp
private async void btnDelete_Click(object? sender, EventArgs e) {
    if (!ValidateSelection())
        return;
    
    var confirm = MessageBox.Show("Delete?", "Confirm", 
        MessageBoxButtons.YesNo, MessageBoxIcon.Question);
    if (confirm != DialogResult.Yes) return;
    
    SetBusy(true);
    try {
        var result = await _service.DeleteAsync(_selectedId);
        
        if (result == DeleteResult.Deleted) {
            MessageBox.Show("Deleted.", "Success");
            await LoadDataAsync();
        }
        else if (result == DeleteResult.HasReferences) {
            MessageBox.Show("Has related records - cannot delete.", "Info");
        }
    }
    finally {
        SetBusy(false);
    }
}
```

---

## 🎨 UI Theming

### Colors
```csharp
Color Primary = RGB(77, 130, 222);       // Azure Blue
Color PrimaryDark = Darker shade;         // Navigation
Color LightBlue = RGB(204, 224, 247);    // Baby Blue
Color PaleBlue = RGB(230, 237, 251);     // Soft Blue
Color Success = Green;
Color Warning = Orange;
Color Danger = Red;
Color Neutral = Gray;
```

### Create Components
```csharp
// Button
var btn = UiTheme.CreateButton("Click Me", x, y, UiTheme.Primary, onClick_handler);

// Label
var lbl = UiTheme.CreateLabel("Label Text", x, y, width);

// Grid
var grid = UiTheme.CreateGrid(x, y, width, height);

// Heading
var heading = UiTheme.CreateHeading("Section Title", fontSize, x, y);

// Panels
var header = UiTheme.CreateHeaderPanel(height);
var body = UiTheme.CreateBodyPanel();
```

---

## ⚙️ Database Migrations

### Create Migration
```bash
cd ERP_infrastructure

# For TenantErp
dotnet ef migrations add "MigrationName" --context TenantErpDbContext

# For MasterErp
dotnet ef migrations add "MigrationName" --context MasterErpDbContext
```

### Apply Migration
```bash
# For TenantErp
dotnet ef database update --context TenantErpDbContext

# For MasterErp (if needed)
dotnet ef database update --context MasterErpDbContext
```

### Rollback Migration
```bash
dotnet ef migrations remove --context TenantErpDbContext
```

---

## 🧪 Testing Guide

### Test Adding Member
1. Launch app → Click "Membership" → Members tab
2. Enter First Name: "John"
3. Enter Last Name: "Doe"
4. Enter Email: "john@example.com"
5. Click "Add New"
6. Verify: Success message + member appears in grid

### Test Update Member
1. Click member in grid
2. Change status to "Inactive"
3. Click "Update"
4. Verify: Member status changed in grid

### Test Delete with History Check
1. Click member with active subscriptions
2. Click "Delete"
3. Confirm delete
4. Verify: Error message explains why delete failed

### Test Search
1. Enter search term (e.g., "john")
2. Click Search or press Enter
3. Verify: Only matching members shown

---

## 🔍 Debugging Tips

### Enable EF Core Logging
```csharp
// In Program.cs
services.AddDbContext<TenantErpDbContext>(options =>
    options.UseSqlServer(connectionString)
    .LogTo(Console.WriteLine)  // Log SQL queries
    .EnableSensitiveDataLogging());
```

### Check Database Connection
```csharp
try {
    var context = serviceProvider.GetRequiredService<TenantErpDbContext>();
    context.Database.OpenConnection();
    context.Database.CloseConnection();
    Debug.WriteLine("✓ Database connection OK");
}
catch (Exception ex) {
    Debug.WriteLine($"✗ Database error: {ex.Message}");
}
```

### Inspect Grid Data
```csharp
// Before binding, check what's in the list
var items = await _service.GetAllAsync();
Debug.WriteLine($"Found {items.Count} items");
foreach (var item in items) {
    Debug.WriteLine($"  - {item.Id}: {item.Name}");
}
```

---

## 📚 File Reference

| File | Purpose |
|------|---------|
| `Program.cs` | DI configuration, entry point |
| `MainForm.cs` | Navigation shell |
| `Form1.cs` | Member CRUD |
| `MembershipForm.cs` | Membership multi-tab module |
| `UiTheme.cs` | UI styling and colors |
| `appsettings.json` | Configuration & connection strings |
| `TenantErpDbContext.cs` | Tenant database context |
| `MemberService.cs` | Member business logic |
| `MemberRepository.cs` | Member data access |

---

## 🚨 Common Errors

### "A second operation was started on this context instance"
**Cause:** Multiple forms sharing same DbContext  
**Solution:** Use separate DI scope for each form/tab  
```csharp
var scope = _scopeFactory.CreateScope();
var form = scope.ServiceProvider.GetRequiredService<MyForm>();
```

### "DbUpdateException: An error occurred while updating the entries"
**Cause:** Foreign key constraint violation or null required field  
**Solution:** Validate before saving, check relationships  

### "The property has a backing field and cannot be mapped"
**Cause:** Property not properly configured in Entity  
**Solution:** Ensure public property with getter/setter  

### "System.NullReferenceException: Object reference not set"
**Cause:** Service not injected or field not initialized  
**Solution:** Check DI registration in Program.cs  

---

## 📖 Documentation Files

| File | Content |
|------|---------|
| `CLAUDE.md` | Project overview & setup |
| `ARCHITECTURE_AND_MVC.md` | Complete architecture guide (2500+ lines) |
| `VERIFICATION_REPORT.md` | Test results and verification |
| `FINAL_VERIFICATION_SUMMARY.md` | Executive summary |
| `QUICK_REFERENCE.md` | This file |

---

## 💡 Pro Tips

1. **Use async/await** - Never block the UI thread
2. **Validate early** - Catch errors in service layer
3. **Use DI** - Never new up services
4. **Log errors** - Use Debug.WriteLine() or logger
5. **Test CRUD** - Add, Read, Update, Delete each entity
6. **Check relationships** - Ensure foreign keys before delete
7. **Use interfaces** - Depend on abstractions, not implementations
8. **Handle exceptions** - Show user-friendly messages

---

## 📞 Need Help?

- **Architecture questions:** See `ARCHITECTURE_AND_MVC.md`
- **Setup issues:** See `CLAUDE.md`
- **Testing help:** See `VERIFICATION_REPORT.md`
- **Code examples:** See source files in `ERP_Project1/` and `ERP_infrastructure/`

---

**Last Updated:** September 16, 2026  
**Version:** 1.0  
**Framework:** .NET 10.0  
**Database:** SQL Server
