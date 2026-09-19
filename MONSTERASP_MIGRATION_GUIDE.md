# 🚀 MonsterASP Migration Guide - Complete Instructions

**Goal:** Transfer TenantErp database from LocalDB to MonsterASP (db68433)

---

## 📋 YOUR MONSTERASP DATABASE DETAILS

From your SSMS connection dialog:
```
Server Name:     db68433.public.databasease.asp.net
User Name:       db68433
Password:        (shown in dialog with dots)
Database:        TenantErp (will be created)
Encryption:      Mandatory
```

---

## 🔑 STEP 1: GET YOUR PASSWORD FROM SSMS

1. **Open SSMS connection dialog** (you already have it open)
2. **Look at the Password field** - it shows dots (•••••)
3. **Copy the actual password** (not the dots) from your MonsterASP account
   - Check your MonsterASP dashboard
   - Check your MonsterASP welcome email
   - Or click the password reveal button in SSMS

**Your password should be something like:** `P@ssw0rd123` or similar

---

## ✏️ STEP 2: UPDATE appsettings.json WITH PASSWORD

### **Important:** Replace `YOUR_PASSWORD_HERE` with your actual password

Open: `C:\Users\USER\source\repos\ERP_Project1\ERP_Project1\appsettings.json`

**FIND THIS:**
```json
"ConnectionStrings": {
  "TenantErp": "Server=db68433.public.databasease.asp.net;Initial Catalog=TenantErp;Persist Security Info=False;User ID=db68433;Password=YOUR_PASSWORD_HERE;MultipleActiveResultSets=True;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
}
```

**REPLACE `YOUR_PASSWORD_HERE` with your actual MonsterASP password:**
```json
"ConnectionStrings": {
  "TenantErp": "Server=db68433.public.databasease.asp.net;Initial Catalog=TenantErp;Persist Security Info=False;User ID=db68433;Password=YourActualPasswordHere;MultipleActiveResultSets=True;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
}
```

**Example:**
```json
"ConnectionStrings": {
  "TenantErp": "Server=db68433.public.databasease.asp.net;Initial Catalog=TenantErp;Persist Security Info=False;User ID=db68433;Password=MySecurePassword123!;MultipleActiveResultSets=True;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
}
```

✅ **Save the file**

---

## 🔄 STEP 3: RUN THE MIGRATION SCRIPT

This script will:
1. ✅ Test MonsterASP connection
2. ✅ Create Members table on MonsterASP
3. ✅ Transfer all data from LocalDB to MonsterASP
4. ✅ Verify the transfer was successful

### **Open PowerShell and run:**

```powershell
cd C:\Users\USER\source\repos\ERP_Project1

# Make sure you updated appsettings.json first!

# Run the migration script
powershell -ExecutionPolicy Bypass -File Migrate_To_MonsterASP.ps1
```

### **What the script will do:**

```
Step 1: Testing MonsterASP Connection... ✅
Step 2: Creating Members Table on MonsterASP... ✅
Step 3: Reading Data from LocalDB... ✅
Step 4: Transferring Data to MonsterASP... ✅
Step 5: Verifying Data on MonsterASP... ✅

✅ MIGRATION COMPLETE - DATA ON MONSTERASP!
```

---

## ✅ STEP 4: BUILD & TEST

```powershell
# Navigate to project
cd C:\Users\USER\source\repos\ERP_Project1

# Rebuild solution
dotnet clean
dotnet build

# Should show: Build succeeded. 0 Error(s)
```

---

## 🧪 STEP 5: RUN APPLICATION WITH MONSTERASP

```powershell
cd C:\Users\USER\source\repos\ERP_Project1\ERP_Project1

# Run the application
dotnet run
```

**Expected:**
1. ✅ Application launches
2. ✅ Members grid loads with 5 test members
3. ✅ All data is now from MonsterASP (not LocalDB)

---

## ✨ STEP 6: VERIFY IN SSMS

### **In SQL Server Management Studio:**

1. **Click** the MonsterASP connection: `db68433.public.databasease.asp.net`
2. **Click** Connect
3. **Navigate:** Databases → Look for TenantErp
4. **Expand** TenantErp → Tables → dbo.Members
5. **Right-click** dbo.Members → **Select Top 1000 Rows**

**You should see your 5 members:**
```
MemberId | FirstName | LastName | Phone | Email | Status
1        | John      | Smith    | 555-0001 | john@example.com | Active
2        | Jane      | Doe      | 555-0002 | jane@example.com | Active
3        | Bob       | Johnson  | 555-0003 | bob@example.com  | Active
4        | Alice     | Williams | 555-0004 | alice@example.com| Active
5        | Charlie   | Brown    | 555-0005 | charlie@example.com| Active
```

✅ **If you see all 5 members - MIGRATION SUCCESS!**

---

## 🎯 STEP 7: TEST CRUD OPERATIONS ON MONSTERASP

Now test all operations to confirm they persist to MonsterASP:

### **Test 1: Add Member**
1. Click "Add New"
2. Fill: First Name = "Robert", Last Name = "Test"
3. Click Add New
4. ✅ Success dialog appears
5. ✅ New member in grid (6 total)
6. ✅ Check SSMS - should see it in dbo.Members table

### **Test 2: Update Member**
1. Click "John Smith" row
2. Change email to: "john.updated@test.com"
3. Click "Update"
4. ✅ Success dialog
5. ✅ Grid refreshes with new email
6. ✅ Check SSMS - should see updated email in table

### **Test 3: Delete Member**
1. Click "Charlie Brown" row
2. Click "Delete"
3. Click "Yes" to confirm
4. ✅ Success dialog
5. ✅ Member removed from grid
6. ✅ Check SSMS - member should be gone from table

---

## 📊 VERIFICATION CHECKLIST

- [ ] Password obtained from SSMS connection dialog
- [ ] appsettings.json updated with password
- [ ] Migration script ran successfully (Step 3)
- [ ] Build succeeded with 0 errors (Step 4)
- [ ] Application launches and shows members (Step 5)
- [ ] SSMS shows TenantErp database on MonsterASP (Step 6)
- [ ] SSMS shows dbo.Members table with 5 members (Step 6)
- [ ] Add Member works and persists to MonsterASP (Step 7)
- [ ] Update Member works and persists to MonsterASP (Step 7)
- [ ] Delete Member works and persists to MonsterASP (Step 7)

---

## 🆘 TROUBLESHOOTING

### **Problem: "MonsterASP Connection Failed"**

**Solutions:**
1. Verify password is correct (check SSMS dialog)
2. Verify server name: `db68433.public.databasease.asp.net`
3. Verify username: `db68433`
4. Check internet connection
5. Verify MonsterASP account is active

### **Problem: "Script won't run"**

**Solution:**
```powershell
# Set execution policy
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser

# Then run script again
powershell -ExecutionPolicy Bypass -File Migrate_To_MonsterASP.ps1
```

### **Problem: "Connection string error when running app"**

**Solutions:**
1. Make sure password in appsettings.json doesn't have special characters that need escaping
2. Verify no extra spaces in connection string
3. Check password copied correctly (no leading/trailing spaces)

### **Problem: "Members table not found on MonsterASP"**

**Solutions:**
1. Run the migration script again (Step 3)
2. Check script ran without errors
3. Verify in SSMS that table was created

### **Problem: "No members showing after migration"**

**Solutions:**
1. Check SSMS - verify data is there
2. Verify application is pointing to MonsterASP (check appsettings.json)
3. Restart application
4. Clear Visual Studio cache: Delete .vs folder

---

## 💡 IMPORTANT NOTES

1. **Keep Backup** - Don't delete LocalDB yet, keep it as backup
2. **Passwords** - Never commit appsettings.json to GitHub with passwords
3. **Encryption Required** - MonsterASP requires Encrypt=True
4. **Timeout** - Connection timeout set to 30 seconds (adjust if slow network)
5. **Multiple Connections** - Can use both LocalDB and MonsterASP simultaneously

---

## ✅ SUCCESS CRITERIA

Your migration is **COMPLETE & SUCCESSFUL** when:

```
✅ Migration script ran without errors
✅ 5 members visible in SSMS on MonsterASP
✅ Application launches and loads members
✅ All CRUD operations work
✅ Data persists to MonsterASP database
✅ Can see changes in SSMS after operations
```

---

## 🎉 YOU'RE DONE!

Once all steps complete, your FitCore ERP application is:
- ✅ Connected to MonsterASP
- ✅ Data stored on MonsterASP server
- ✅ Ready for production use
- ✅ Accessible from anywhere (not just local)

**Data is now on MonsterASP cloud database! 🚀**

---

**Need Help?** 
- Check Troubleshooting section above
- Verify each step carefully
- Check appsettings.json for correct password

