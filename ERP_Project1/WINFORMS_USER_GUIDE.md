# Member CRUD WinForms Application - User Guide

## Prerequisites

- .NET 10 Runtime installed
- SQL Server Express (.\\SQLEXPRESS) running
- TenantErp database (auto-created on first run)

## How to Run the WinForms Application

### Method 1: From Visual Studio Community 2026

1. Open the solution: `ERP_Project1.slnx`
2. Right-click on `ERP_Project1` (WinForms project) in Solution Explorer
3. Select **"Set as Startup Project"**
4. Press **F5** or click **"Run"** button
5. The WinForms application will launch

### Method 2: From PowerShell/Command Line

```powershell
cd "C:\Users\USER\source\repos\ERP_Project1\ERP_Project1"
dotnet run
```

### Method 3: Direct Executable (After Publishing)

```powershell
cd "C:\Users\USER\source\repos\ERP_Project1\ERP_Project1\bin\Release\net10.0-windows"
.\ERP_Project1.exe
```

## Application Features

### 1. Member Management Window

The application opens with a professional blue/teal themed window displaying:

- **Title Bar**: "Member Management System - CRUD Operations"
- **Window Size**: 1200 x 700 pixels
- **Search Panel**: Quick search functionality
- **Member Form**: Input fields for member details
- **Data Grid**: List of all members in the database
- **Status Bar**: Shows operation results

### 2. Search & Filter Section

Located at the top of the form:

- **Search Box**: Enter any part of member's name, email, or phone
- **Search Button**: Applies the search filter
- **Refresh Button**: Reloads all members from database

**Search searches across:**
- First Name
- Last Name
- Email
- Phone Number

**Example:** Type "John" to find all members with John in their first name

### 3. Member Information Form

Located below the search section:

#### Input Fields:
- **First Name** *(Required)* - Max 100 characters
- **Last Name** *(Required)* - Max 100 characters
- **Phone** *(Optional)* - Max 20 characters
- **Email** *(Optional)* - Max 100 characters
- **Status** *(Dropdown)* - Active / Inactive / Suspended

#### Action Buttons:
- **Add New** (Green button) - Creates a new member
- **Update** (Orange button) - Modifies selected member
- **Delete** (Red button) - Removes selected member

### 4. Members Data Grid

Located at the bottom of the form:

- **Click any row to select a member** - Automatically loads member data into the form
- **Blue header** shows column names
- **Alternating row colors** for easy reading
- **Read-only** - Cannot edit directly in grid (use form controls)

## Step-by-Step Operations

### Adding a New Member

1. **Clear previous data** (optional): Click the "Refresh" button
2. **Enter First Name**: Type the member's first name (required)
3. **Enter Last Name**: Type the member's last name (required)
4. **Enter Phone** (optional): Type the phone number
5. **Enter Email** (optional): Type the email address
6. **Select Status**: Choose from dropdown (defaults to "Active")
7. **Click "Add New"** button
8. **Success message** appears showing the new Member ID
9. **Grid refreshes** automatically with the new member

**Validation:**
- If First Name or Last Name is empty, you'll get an error
- Phone and Email are optional
- Status defaults to "Active" if not changed

### Viewing All Members

1. Click the **"Refresh"** button to reload all members
2. The grid displays all members from the database
3. Each row shows: ID, First Name, Last Name, Phone, Email, Join Date, Status, Created Date

### Selecting & Viewing Member Details

1. **Click on any row in the grid** (click the row number on the left)
2. The member's data automatically loads into the form fields:
   - First Name appears in txtFirstName
   - Last Name appears in txtLastName
   - Phone appears in txtPhone
   - Email appears in txtEmail
   - Status appears in cmbStatus dropdown

### Editing a Member

1. **Click on the member's row in the grid** to select them
2. **Modify the fields** you want to change:
   - Change First Name
   - Change Last Name
   - Change Phone
   - Change Email
   - Change Status (dropdown)
3. **Click "Update"** button
4. **Success message** confirms the update
5. **Grid refreshes** showing the updated information

**Important:**
- You must select a member first (click a grid row)
- First Name and Last Name are required
- Status must be a valid option

### Deleting a Member

1. **Click on the member's row in the grid** to select them
2. **Click the "Delete"** button (red)
3. **Confirmation dialog** appears asking "Are you sure?"
4. **Click "Yes"** to confirm deletion
5. **Success message** confirms deletion
6. **Grid refreshes** automatically (member removed)
7. **Form clears** for the next operation

**Important:**
- Deletion is permanent
- All related subscriptions and sales are automatically deleted
- Cannot undo once deleted

### Searching Members

1. **Type in the Search box**: Enter any search term
   - Examples: "John", "Smith", "john@email.com", "555-1234"
2. **Click "Search"** button
3. **Grid updates** showing only matching members
4. **Search is case-insensitive** (works with any case)
5. **To clear the search**: Leave search box empty and click "Search"

**Search Behavior:**
- Searches across FirstName, LastName, Email, and Phone
- Partial matches work (e.g., "Jo" finds "John")
- Case-insensitive
- Empty search shows all members

## Data Fields & Constraints

| Field | Type | Required | Max Length | Notes |
|-------|------|----------|-----------|-------|
| MemberId | Integer | Auto | - | Auto-generated by database |
| FirstName | Text | Yes | 100 | Cannot be empty |
| LastName | Text | Yes | 100 | Cannot be empty |
| Phone | Text | No | 20 | Defaults to empty string |
| Email | Text | No | 100 | Defaults to empty string |
| Status | Text | No | 20 | Values: Active, Inactive, Suspended |
| JoinDate | DateTime | Auto | - | Set to current UTC time |
| CreatedAt | DateTime | Auto | - | Set to current UTC time |

## Status Values

Members can have one of three statuses:

- **Active** (Default) - Member is currently active
- **Inactive** - Member is no longer active
- **Suspended** - Member is temporarily suspended

Change status by selecting from the "Status" dropdown when adding or editing.

## Color Scheme

The application uses a professional blue/teal color palette:

- **Title Bar**: Azure Blue - Shows the main title
- **Panels**: Light Blue - Input and search areas
- **Buttons**:
  - **Green (Add)**: Create new member
  - **Orange (Update)**: Modify existing member
  - **Red (Delete)**: Remove member
  - **Blue (Search/Refresh)**: Navigation actions
- **Grid Headers**: Light Blue with bold text
- **Grid Rows**: White with alternating Pale Blue

## Error Messages & Solutions

### "First Name and Last Name are required"
**Cause**: Tried to add/update without entering required fields
**Solution**: Enter both First Name and Last Name before clicking Add or Update

### "Please select a member to update"
**Cause**: Clicked Update without selecting a member from the grid
**Solution**: Click on a member's row in the grid to select them first

### "Please select a member to delete"
**Cause**: Clicked Delete without selecting a member
**Solution**: Click on a member's row to select them first

### Database Connection Errors
**Cause**: SQL Server not running or database doesn't exist
**Solution**:
1. Verify SQL Server Express is running (Services.msc)
2. Check connection string in appsettings.json
3. Database will auto-create on first successful connection

### "Error creating member: [exception details]"
**Cause**: Database constraint violation or connection issue
**Solution**:
1. Check that Phone and Email fields are not too long
2. Verify database connection
3. Try refreshing and trying again

## Database Location

- **Server**: `.\\SQLEXPRESS` (Local SQL Server Express)
- **Database**: `TenantErp`
- **Table**: `Members`

To view the database directly in SQL Server Management Studio:

```sql
SELECT * FROM TenantErp.dbo.Members;
```

## Keyboard Shortcuts

| Action | Shortcut |
|--------|----------|
| Add New Member | Click "Add New" button |
| Select Row | Click row number in grid |
| Refresh Data | Click "Refresh" button |
| Search | Click "Search" button |
| Update | Click "Update" button |
| Delete | Click "Delete" button |

## Performance Tips

- **Large Member Lists**: Use search to filter before editing
- **Refresh**: Click Refresh regularly to ensure you have latest data
- **Multiple Edits**: Add or update multiple members efficiently by clearing form and continuing

## Troubleshooting

### Application Won't Start
- Check .NET 10 is installed: `dotnet --version`
- Verify Visual Studio 2026 has required workloads installed
- Check event viewer for detailed error messages

### Can't Add Member
- Verify First Name and Last Name are filled
- Check database connection is working
- Review error message for specific constraint violation

### Updates Not Saving
- Ensure you clicked "Update" button (not just edited fields)
- Verify member was selected from grid first
- Check database is accessible

### Grid Not Showing Data
- Click "Refresh" button
- Check database connection
- Try searching to see if data exists

## Support Information

If you encounter issues:

1. Check the error message details carefully
2. Review the logs in Output window (Visual Studio)
3. Verify database connectivity
4. Check that all required fields are filled
5. Ensure member is selected before updating/deleting

## Additional Features

### Cascading Delete
When a member is deleted:
- All associated subscriptions are automatically deleted
- All associated sales records are automatically deleted
- Database referential integrity is maintained

### Auto-Populated Fields
When creating a member:
- JoinDate: Automatically set to current UTC time
- CreatedAt: Automatically set to current UTC time
- Status: Defaults to "Active"

### Data Persistence
All data is saved directly to SQL Server database:
- Changes persist after application closes
- Multiple users can access same database
- Full ACID compliance

---

**Version**: 1.0  
**Last Updated**: 2026-09-15  
**Framework**: .NET 10  
**Database**: SQL Server Express (.\\SQLEXPRESS)
