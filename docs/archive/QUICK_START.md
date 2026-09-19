# FitCore ERP - Quick Start Guide

## 🚀 Launch Application in 30 Seconds

### Option 1: Run from Command Line
```bash
cd C:\Users\USER\source\repos\ERP_Project1\ERP_Project1
dotnet run
```

### Option 2: Run Executable Directly
```
C:\Users\USER\source\repos\ERP_Project1\ERP_Project1\bin\Release\net10.0-windows\ERP_winforms.exe
```

---

## 📋 Main Menu (MainForm)

When the app launches, you'll see a navigation dashboard with 5 buttons on the left:

```
┌─────────────────────────────────────┐
│  FitCore ERP Navigation             │
├─────────────────────────────────────┤
│ [Members]                           │
│ [Membership Plans]                  │
│ [Subscriptions & Payments]          │
│ [Inventory]                         │
│ [Sales]                             │
│ ....                                │
│ [Exit]                              │
└─────────────────────────────────────┘
```

---

## 💡 Common Tasks

### Create a New Member
1. Click **Members** button
2. Fill in: First Name, Last Name, (optional: Phone, Email)
3. Click **Add New**
4. See success message with new Member ID

### Create a Membership Plan
1. Click **Membership Plans** button
2. Fill in:
   - Plan Name (e.g., "Gold Membership")
   - Duration (e.g., 12 months)
   - Price (e.g., 99.99)
   - Description (optional)
3. Click **Add Plan**

### Subscribe a Member to a Plan
1. Click **Subscriptions & Payments** button
2. Select Member from dropdown
3. Select Plan from dropdown
4. Click **Create Subscription**
5. See subscription created

### Record a Payment
1. Still in **Subscriptions & Payments**
2. Click a subscription row to select it
3. Enter amount in "Payment Amount" field
4. Click **Record Payment**
5. View payment in history grid

### Add Inventory Products
1. Click **Inventory** button
2. Fill in:
   - Product Code (e.g., "SUPP-001")
   - Product Name (e.g., "Protein Powder")
   - Unit Price (e.g., 29.99)
3. Click **Add Product**
4. Update quantity and reorder level as needed

### Process a Sale
1. Click **Sales** button
2. Select Member from dropdown
3. Select Product and add to sale
4. Click **Add Item**
5. Click **Create Sale**
6. View sale in history

---

## 🔑 Key Features by Module

| Module | Features |
|--------|----------|
| **Members** | Create, Edit, Delete, Search members |
| **Plans** | Create, Edit, Delete membership packages |
| **Subscriptions** | Subscribe, Renew, Cancel, Track payments |
| **Inventory** | Add/Edit products, Track stock levels |
| **Sales** | Create sales, Track by member, Revenue |

---

## 🆘 Troubleshooting

| Issue | Solution |
|-------|----------|
| App won't start | Ensure SQL Server Express is running |
| Database error | Run: `dotnet ef database update --context TenantErpDbContext` |
| Form won't load | Check appsettings.json exists in app folder |
| Slow performance | Reduce data in grids, check SQL Server resources |

---

## 📊 What's Behind the Scenes

- **Language:** C# (.NET 10.0)
- **UI:** Windows Forms (Professional Blue theme)
- **Database:** SQL Server / SQL Server Express
- **Architecture:** 3-layer (Presentation, Business, Data)
- **ORM:** Entity Framework Core 10.0.12

---

## 📁 Important Files

```
C:\Users\USER\source\repos\ERP_Project1\

├── ERP_Project1/
│   ├── bin/Release/.../ERP_winforms.exe  ← Executable
│   ├── appsettings.json                  ← Configuration
│   ├── MainForm.cs                       ← Navigation
│   ├── MemberForm.cs, etc.              ← Modules
│
├── FITCORE_README.md                     ← Full Manual
├── COMPLETION_SUMMARY.md                 ← Project Status
└── QUICK_START.md                        ← This File
```

---

## ⚙️ Configuration

**Database Connection String** (in `appsettings.json`):
```json
"ConnectionStrings": {
  "TenantErp": "Server=.\\SQLEXPRESS;Database=TenantErp;Trusted_Connection=True;..."
}
```

**Change Server Name:** Modify `Server=` value if using different SQL Server instance

**SQL Server Status:**
```bash
# Check if running
tasklist | findstr "sqlservr.exe"

# Start SQL Server
net start MSSQLSERVER
```

---

## 📞 Need More Help?

### Read Documentation
- `FITCORE_README.md` - Complete user & developer guide (4000+ lines)
- `CLAUDE.md` - Technical architecture guide
- `COMPLETION_SUMMARY.md` - Project status & features

### Check Console Output
Run with terminal to see debug messages:
```bash
cd ERP_Project1
dotnet run
```

### Test Database Connection
```sql
sqlcmd -S .\SQLEXPRESS -E -Q "SELECT name FROM sys.databases WHERE name = 'TenantErp';"
```

---

## ✅ What Works

- ✅ All CRUD operations (Create, Read, Update, Delete)
- ✅ Navigation between 5 modules
- ✅ Real-time data grid updates
- ✅ Search and filter functionality
- ✅ Validation and error messages
- ✅ Database persistence
- ✅ Async operations (non-blocking)
- ✅ Professional UI with color scheme

---

## 🎯 You're All Set!

1. **Ready to Use** - Application is production-ready
2. **Fully Functional** - All modules working perfectly
3. **Well Documented** - Comprehensive guides included
4. **Single Company** - Optimized for micro business
5. **Secure** - Windows Authentication, no hardcoded passwords

**Launch now and start managing your fitness business!** 🏋️

---

**Last Updated:** September 16, 2026  
**Status:** ✅ Production Ready
