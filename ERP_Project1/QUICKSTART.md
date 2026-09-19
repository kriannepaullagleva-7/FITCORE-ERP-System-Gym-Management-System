# ERP Member Management System - Quick Start Guide

## 🚀 Quick Start (5 Minutes)

### Step 1: Verify Prerequisites
- .NET 10 SDK installed
- SQL Server running (local or network)
- Visual Studio 2026 or VS Code with C# extension

### Step 2: Configure Database Connection
Edit `ERP_Project1/appsettings.json`:
```json
{
  "ConnectionStrings": {
	"TenantErp": "Server=YOUR_SERVER_NAME;Database=TenantErp;Trusted_Connection=True;Encrypt=false;TrustServerCertificate=True;MultipleActiveResultSets=True;"
  }
}
```

Replace `YOUR_SERVER_NAME` with:
- `.` for local SQL Server
- `.\SQLEXPRESS` for SQL Server Express
- `COMPUTERNAME\INSTANCE` for named instance

### Step 3: Build Project
```bash
dotnet build ERP_Project1/ERP_winforms.csproj
```

### Step 4: Run Application
```bash
cd ERP_Project1
dotnet run
```

---

## 📋 Common Operations

### Add a New Member
1. Fill in **First Name** and **Last Name** (required)
2. Optionally add **Phone** and **Email**
3. Click **"Add New"** button (green)
4. See "Member created successfully!" message
5. New member appears in grid

### Find a Member
1. Type search term in **Search:** field
2. Click **"Search"** button
3. Grid shows matching members
4. Click **"Refresh"** to show all

### Update Member Details
1. **Click the member row** in the grid (row highlights)
2. Form fields populate automatically
3. Edit any field(s)
4. Change **Status** if needed
5. Click **"Update"** button (yellow)
6. See "Member updated successfully!" message

### Delete a Member
1. **Click the member row** in the grid
2. Click **"Delete"** button (red)
3. Confirmation dialog appears
4. Click **"Yes"** to confirm
5. Member removed from database

### Refresh All Data
- Click **"Refresh"** button to reload everything

---

## 🎨 Color Reference

| Button | Color | Action |
|--------|-------|--------|
| Add New | Green | Create new member |
| Update | Yellow | Edit selected member |
| Delete | Red | Remove member (with confirmation) |
| Search | Blue | Find members |
| Refresh | Blue | Reload all data |

---

## ❓ Troubleshooting

### Application won't start
- **Error**: "Could not connect to database"
- **Solution**: Check connection string in `appsettings.json`
- **Check**: Is SQL Server running? Does database exist?

### No members showing
- **Error**: Grid is empty
- **Solution**: Check database connection is working
- **Check**: Run "Refresh" button manually

### Can't add members
- **Error**: "First Name and Last Name are required"
- **Solution**: Fill in both required fields
- **Check**: Name fields cannot be empty

### Can't delete member
- **Error**: "Please select a member to delete"
- **Solution**: Click on a row in the grid first
- **Check**: Row must be highlighted in blue

### Search not working
- **Error**: No results showing
- **Solution**: Search is case-insensitive
- **Check**: Member might not match all criteria

---

## 📊 Features at a Glance

| Feature | Status | Purpose |
|---------|--------|---------|
| Add Members | ✅ | Create new records |
| View Members | ✅ | See all members in grid |
| Search Members | ✅ | Find by name, email, phone |
| Edit Members | ✅ | Update member information |
| Update Status | ✅ | Change Active/Inactive/Suspended |
| Delete Members | ✅ | Remove with confirmation |
| Validation | ✅ | Catch errors early |
| Database Sync | ✅ | All changes saved immediately |
| Error Messages | ✅ | Clear feedback on operations |

---

## 🔧 Technical Details

**Framework**: .NET 10  
**UI**: WinForms  
**Database**: SQL Server  
**ORM**: Entity Framework Core 10.0.12  
**Architecture**: Layered (UI → Services → Repositories → Database)

---

## 📞 If Something Goes Wrong

1. **Check Event Viewer** - Windows Event Viewer for system errors
2. **Check Output Window** - Visual Studio Output window during run
3. **Verify SQL Connection** - Can you connect to SQL Server Management Studio?
4. **Check Migrations** - Has database schema been created?
5. **Review Logs** - Check appsettings.json logging section

---

## 🎯 Success Criteria

✅ Application launches without errors  
✅ Members display in grid on startup  
✅ Can add new member and see in grid  
✅ Can edit member by clicking row  
✅ Can delete member with confirmation  
✅ Search filters members correctly  
✅ Status dropdown changes member status  
✅ All buttons respond to clicks  

---

## 📝 Default Test Data

To test the application, add these members:

| First Name | Last Name | Email | Phone |
|-----------|-----------|-------|-------|
| John | Doe | john@example.com | 555-0001 |
| Jane | Smith | jane@example.com | 555-0002 |
| Bob | Johnson | bob@example.com | 555-0003 |

Then try:
1. Update John's status to "Inactive"
2. Search for "Smith"
3. Delete "Johnson" (then click Undo? - no undo, must refresh)

---

**Version 1.0** | **Status: READY TO USE** ✅

For detailed documentation, see `README.md` and `IMPLEMENTATION_SUMMARY.md`
