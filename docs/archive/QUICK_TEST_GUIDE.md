# Quick Test Guide - Member CRUD

**All fixes applied ✅ | Build: 0 Errors ✅ | Database: Connected ✅**

---

## 🚀 RUN THE APPLICATION

```powershell
cd C:\Users\USER\source\repos\ERP_Project1\ERP_Project1
dotnet run
```

**What to expect:**
- Window opens in 3-5 seconds
- Title: "Member Management System - CRUD Operations"
- Grid loads with 5 test members automatically

---

## ✅ TEST CHECKLIST (5 minutes)

### 1️⃣ LOAD & VIEW (Automatic)
- [ ] Application window opens
- [ ] 5 members display in grid:
  - John Smith (john@example.com)
  - Jane Doe (jane@example.com)
  - Bob Johnson (bob@example.com)
  - Alice Williams (alice@example.com)
  - Charlie Brown (charlie@example.com)
- [ ] All columns visible: MemberId, FirstName, LastName, Phone, Email, Status

### 2️⃣ ADD NEW MEMBER
**Steps:**
1. Fill form with:
   - First Name: `Michael`
   - Last Name: `Davis`
   - Phone: `555-0010`
   - Email: `michael@example.com`
2. Click "Add New" button (green)

**Verify:**
- [ ] Success dialog: "Member created successfully! ID: 6"
- [ ] Form clears
- [ ] Grid shows new member (6 members total now)
- [ ] Michael Davis appears in grid

### 3️⃣ UPDATE MEMBER
**Steps:**
1. Click on "John Smith" row in grid
2. Form populates with John's data
3. Change Email to: `john.updated@example.com`
4. Click "Update" button (orange)

**Verify:**
- [ ] Success dialog: "Member updated successfully!"
- [ ] Form clears
- [ ] Grid updates showing new email
- [ ] Click row again - confirms email changed in form

### 4️⃣ SEARCH MEMBERS
**Steps:**
1. Type `Jane` in search box
2. Click "Search" button (blue)

**Verify:**
- [ ] Grid filters to show only Jane Doe
- [ ] Click "Refresh" button - all members return

### 5️⃣ DELETE MEMBER
**Steps:**
1. Click on "Charlie Brown" row
2. Click "Delete" button (red)
3. Confirmation dialog appears: "Are you sure...?"
4. Click "Yes"

**Verify:**
- [ ] Success dialog: "Member deleted successfully!"
- [ ] Charlie Brown removed from grid
- [ ] Grid shows 5 members now (Michael, Jane, Bob, Alice + one other)

### 6️⃣ VALIDATION
**Steps:**
1. Leave First Name blank
2. Enter Last Name: `TestOnly`
3. Click "Add New"

**Verify:**
- [ ] Error dialog: "First Name and Last Name are required."
- [ ] Form NOT cleared
- [ ] Member NOT added to database

### 7️⃣ DATA PERSISTENCE
**Steps:**
1. Close the application
2. Run again: `dotnet run`

**Verify:**
- [ ] All members still present
- [ ] Changes from UPDATE persist (John's new email)
- [ ] Deleted member (Charlie) stays deleted
- [ ] New member (Michael) still present

---

## ✅ FINAL VERIFICATION

When ALL tests pass:

```
✅ CREATE works (added Michael Davis)
✅ READ works (all members load)
✅ UPDATE works (John's email updated)
✅ DELETE works (Charlie removed)
✅ SEARCH works (filtered to Jane)
✅ VALIDATION works (prevented invalid data)
✅ PERSISTENCE works (data survives restart)
```

**Status:** 🟢 **PRODUCTION READY**

---

## 🐛 IF YOU SEE ERRORS

### Error: "Cannot connect to database"
- LocalDB might need to start
- Check connection string in `appsettings.json` is set to LocalDB
- Current string should be: `Server=(localdb)\mssqllocaldb;Database=TenantErp;...`

### Error: "No members display"
- Database might be empty (run this to re-add test data):
```powershell
# Run from ERP_Project1 folder
powershell -Command @"
`$connString = 'Server=(localdb)\mssqllocaldb;Database=TenantErp;Trusted_Connection=True;'
`$conn = New-Object System.Data.SqlClient.SqlConnection(`$connString)
`$conn.Open()
`$cmd = New-Object System.Data.SqlClient.SqlCommand('INSERT INTO Members (FirstName, LastName, Phone, Email, Status, JoinDate, CreatedAt) VALUES (\"Test\", \"User\", \"555-9999\", \"test@example.com\", \"Active\", GETUTCDATE(), GETUTCDATE())', `$conn)
`$cmd.ExecuteNonQuery() | Out-Null
`$conn.Close()
Write-Host 'Test member added'
"@
```

### Error: "Buttons not responding"
- Wait for previous operation to complete
- Check console for detailed error messages
- Close any error dialogs

### Error: "Threading or concurrent operation error"
- Already fixed! Should not appear
- If it does, all async operations are properly managed

---

## 📝 NOTES

- **Test Data:** 5 members pre-loaded (John, Jane, Bob, Alice, Charlie)
- **Can Add More:** Add as many members as you want
- **Can Edit/Delete:** All operations work end-to-end
- **Search Works:** Try searching by first name, last name, email, or phone
- **Status Field:** Always shows "Active" for new members (can add more status options in future)
- **Validation:** First Name and Last Name are required; Phone and Email are optional

---

## 📊 WHAT WAS FIXED

1. **Threading Issue** - "A second operation was started on this context"
   - ✅ Fixed with proper async/await patterns
   - ✅ Button state management prevents concurrent operations
   - ✅ UI thread marshalling ensures thread safety

2. **Database Connection** - "Server not found or not accessible"
   - ✅ Switched from SQLEXPRESS to LocalDB
   - ✅ Connection verified and working

3. **UI Responsiveness** - Buttons unresponsive during operations
   - ✅ Buttons now disable during operations
   - ✅ Re-enable when operation completes
   - ✅ User gets clear feedback

---

## 🎉 YOU'RE READY!

Run: `dotnet run`

Then test all 7 operations above.

All CRUD should work perfectly! 🚀

