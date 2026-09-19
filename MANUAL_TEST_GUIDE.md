# FitCore ERP - Complete Manual Testing Guide

**Date:** September 16, 2026  
**Status:** Build: ✅ SUCCESS (0 errors)  
**Database:** ✅ Ready (All migrations applied)  

---

## 🎯 PURPOSE

This guide provides step-by-step instructions to verify that ALL features of the FitCore ERP application work correctly.

---

## ✅ PRE-LAUNCH CHECKLIST

### Before Running the Application

- [ ] Build succeeded: `dotnet build` returned 0 errors
- [ ] Database exists: `TenantErp` on local SQL Server Express
- [ ] Migrations applied: All up to date
- [ ] appsettings.json configured: Located in `ERP_Project1` folder
- [ ] No orphaned .vs folder: Cache cleared

---

## 🚀 LAUNCHING THE APPLICATION

### Step 1: Open Terminal

```bash
cd C:\Users\USER\source\repos\ERP_Project1\ERP_Project1
```

### Step 2: Run Application

```bash
dotnet run
```

### Expected Result:
- ✅ Window opens titled "FitCore ERP - Member Management"
- ✅ Status bar shows "Loaded X members"
- ✅ Members grid displays (may be empty if database has no data)
- ✅ No error dialogs appear

**If you see any error:** Note the exact message and check the Troubleshooting section below.

---

## 🧪 FUNCTIONAL TESTING

### TEST 1: UI Loads Correctly

**Steps:**
1. Application starts with no errors
2. Window is visible and responsive
3. All controls are visible:
   - Search textbox and buttons
   - Member form fields (First Name, Last Name, Phone, Email, Status)
   - Action buttons (Add New, Update, Delete)
   - Members grid

**Pass Criteria:** ✅ All UI elements visible and clickable

---

### TEST 2: Add Member (CREATE)

**Steps:**
1. In the form on the left, fill in:
   - First Name: `John`
   - Last Name: `Smith`
   - Phone: `555-0123`
   - Email: `john.smith@example.com`
   - Status: `Active` (default)

2. Click **"Add New"** button (green)

**Expected Result:**
- ✅ Success dialog appears: "Member created! ID: 1"
- ✅ Form clears automatically
- ✅ Grid updates to show the new member
- ✅ Status bar shows "Loaded X members" (count increases)

**Pass Criteria:** ✅ Member added and visible in grid

---

### TEST 3: Read Members (READ)

**Steps:**
1. Application shows grid with members loaded

**Expected Result:**
- ✅ Grid displays all members from database
- ✅ Can scroll through members
- ✅ Status bar shows correct count

**Pass Criteria:** ✅ All members display correctly

---

### TEST 4: Search Members (READ with Filter)

**Steps:**
1. Type search term in "Search:" textbox
   - Example: `john`
2. Click **"Search"** button (blue)

**Expected Result:**
- ✅ Grid updates to show only matching members
- ✅ Status bar shows "Found X members"
- ✅ Only members matching "john" appear

**Variations to test:**
- [ ] Search by first name: `john`
- [ ] Search by last name: `smith`
- [ ] Search by email: `smith@example.com`
- [ ] Search with no results: `xyz123`

**Pass Criteria:** ✅ Search filters correctly for all fields

---

### TEST 5: Update Member (UPDATE)

**Steps:**
1. Click a member row in the grid to select
2. Form populates with member details
3. Change one field:
   - Email: Change to `john.updated@example.com`
4. Click **"Update"** button (orange)

**Expected Result:**
- ✅ Success dialog: "Member updated!"
- ✅ Form clears
- ✅ Grid updates to show new email
- ✅ Member data persists in database

**Variations to test:**
- [ ] Update First Name
- [ ] Update Last Name
- [ ] Update Phone
- [ ] Update Email
- [ ] Update Status

**Pass Criteria:** ✅ All fields update correctly

---

### TEST 6: Delete Member (DELETE)

**Steps:**
1. Click a member row to select
2. Click **"Delete"** button (red)
3. Confirmation dialog appears
4. Click **"Yes"** to confirm

**Expected Result:**
- ✅ Confirmation dialog appears
- ✅ Success dialog: "Member deleted!"
- ✅ Form clears
- ✅ Grid updates to remove member
- ✅ Member count decreases

**Pass Criteria:** ✅ Member deleted and removed from grid

---

### TEST 7: Refresh (Read All Again)

**Steps:**
1. Click **"Refresh"** button (light blue)

**Expected Result:**
- ✅ Grid reloads all members
- ✅ Search box clears
- ✅ Form clears
- ✅ No errors occur

**Pass Criteria:** ✅ Refresh reloads all data

---

### TEST 8: Validation

**Steps:**
1. Try to add member with empty First Name:
   - Leave First Name blank
   - Enter Last Name: `TestOnly`
   - Click "Add New"

**Expected Result:**
- ✅ Validation error: "First Name and Last Name are required."
- ✅ Member NOT added
- ✅ Form not cleared

**Variations:**
- [ ] Add with empty Last Name
- [ ] Add with empty First and Last Name

**Pass Criteria:** ✅ Validation prevents invalid data

---

### TEST 9: Data Persistence

**Steps:**
1. Add member: `TestPersist LastName`
2. Click "Refresh"
3. Close application
4. Run application again: `dotnet run`

**Expected Result:**
- ✅ Application loads without errors
- ✅ Grid shows member from previous session
- ✅ Data persisted to database

**Pass Criteria:** ✅ Data survives application restart

---

### TEST 10: Error Handling

**Steps:**
1. Try operations with network temporarily disconnected (if testing with remote DB):
   - Click "Add New"
   - Or click "Search"

**Expected Result:**
- ✅ Application shows error dialog
- ✅ Application remains responsive
- ✅ No crashes occur

**Pass Criteria:** ✅ Errors handled gracefully

---

## 📊 TEST RESULTS SUMMARY

| Test | Feature | Status | Notes |
|------|---------|--------|-------|
| 1 | UI Load | ✅ Pass / ❌ Fail | |
| 2 | Add Member | ✅ Pass / ❌ Fail | |
| 3 | Read Members | ✅ Pass / ❌ Fail | |
| 4 | Search | ✅ Pass / ❌ Fail | |
| 5 | Update | ✅ Pass / ❌ Fail | |
| 6 | Delete | ✅ Pass / ❌ Fail | |
| 7 | Refresh | ✅ Pass / ❌ Fail | |
| 8 | Validation | ✅ Pass / ❌ Fail | |
| 9 | Persistence | ✅ Pass / ❌ Fail | |
| 10 | Error Handling | ✅ Pass / ❌ Fail | |

---

## 🆘 TROUBLESHOOTING

### Problem: "Cannot connect to database"

**Solution:**
1. Verify SQL Server Express is running:
   - Windows Services → SQL Server (SQLEXPRESS)
2. Verify connection string in appsettings.json
3. Test connection with SSMS
4. Check firewall allows port 1433

---

### Problem: "An element with the same key but a different value already exists"

**Solution:**
1. This is a Visual Studio cache issue
2. Delete `.vs` folder: `rmdir /s .vs`
3. Delete `bin` and `obj` folders
4. Run: `dotnet clean`
5. Run: `dotnet build`

---

### Problem: Application won't start / crashes immediately

**Solution:**
1. Check console output for error message
2. Verify appsettings.json is valid JSON
3. Verify database connection string is correct
4. Check that all NuGet packages restored: `dotnet restore`

---

### Problem: Database empty but should have data

**Solution:**
1. Run migrations: `dotnet ef database update --context TenantErpDbContext`
2. Verify database exists in SSMS
3. Query database: `SELECT COUNT(*) FROM Members;`

---

### Problem: Buttons don't respond

**Solution:**
1. Wait for operation to complete (buttons disable during DB operations)
2. Close any error dialogs
3. Restart application

---

## ✅ SUCCESS CRITERIA

Your application is **READY FOR PRODUCTION** when:

- ✅ Build completes with 0 errors
- ✅ Application starts without crash
- ✅ Can add members
- ✅ Can view all members
- ✅ Can search members
- ✅ Can update members
- ✅ Can delete members
- ✅ Data persists after restart
- ✅ Validation prevents invalid data
- ✅ Errors display gracefully

---

## 📝 TEST EXECUTION LOG

**Date Tested:** _______________

**Tester Name:** _______________

**Results:**

All 10 tests passed: [ ] Yes [ ] No

If No, which tests failed: ___________________________

---

## 🚀 NEXT STEPS

Once ALL tests pass:

1. Your application is verified working
2. You can add more data manually
3. You can deploy to production
4. You can integrate with your MonsterASP database

---

**Good luck! Your application is ready to use!** 🎉

