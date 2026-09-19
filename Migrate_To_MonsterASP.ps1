#!/usr/bin/env pwsh
<#
    FitCore ERP - Migrate from LocalDB to MonsterASP
    This script transfers the TenantErp database from LocalDB to MonsterASP
#>

Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  FitCore ERP - Migrate to MonsterASP Database              ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# ============================================
# CONFIGURATION - UPDATE THESE WITH YOUR INFO
# ============================================

$MonsterASPServer = "db68433.public.databasease.asp.net"
$MonsterASPUser = "db68433"
$MonsterASPPassword = "YOUR_PASSWORD_HERE"  # <-- CHANGE THIS TO YOUR PASSWORD
$MonsterASPDatabase = "TenantErp"

$LocalDBServer = "(localdb)\mssqllocaldb"
$LocalDBDatabase = "TenantErp"

# ============================================
# TEST MONSTERASP CONNECTION
# ============================================

Write-Host "Step 1: Testing MonsterASP Connection..." -ForegroundColor Yellow
Write-Host "Server: $MonsterASPServer" -ForegroundColor Gray
Write-Host "User: $MonsterASPUser" -ForegroundColor Gray
Write-Host ""

$MonsterASPConnString = "Server=$MonsterASPServer;Initial Catalog=$MonsterASPDatabase;Persist Security Info=False;User ID=$MonsterASPUser;Password=$MonsterASPPassword;MultipleActiveResultSets=True;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"

$connection = New-Object System.Data.SqlClient.SqlConnection
$connection.ConnectionString = $MonsterASPConnString

try {
    $connection.Open()
    Write-Host "✅ MonsterASP Connection Successful!" -ForegroundColor Green
    $connection.Close()
}
catch {
    Write-Host "❌ MonsterASP Connection Failed!" -ForegroundColor Red
    Write-Host "Error: $_" -ForegroundColor Red
    Write-Host ""
    Write-Host "TROUBLESHOOTING:" -ForegroundColor Yellow
    Write-Host "1. Check your password is correct (replace YOUR_PASSWORD_HERE)" -ForegroundColor Gray
    Write-Host "2. Verify the server name: db68433.public.databasease.asp.net" -ForegroundColor Gray
    Write-Host "3. Verify the username: db68433" -ForegroundColor Gray
    Write-Host "4. Check your internet connection" -ForegroundColor Gray
    exit 1
}

Write-Host ""

# ============================================
# CREATE MEMBERS TABLE ON MONSTERASP
# ============================================

Write-Host "Step 2: Creating Members Table on MonsterASP..." -ForegroundColor Yellow
Write-Host ""

$connection = New-Object System.Data.SqlClient.SqlConnection
$connection.ConnectionString = $MonsterASPConnString

try {
    $connection.Open()

    # Drop table if exists
    $dropQuery = "IF OBJECT_ID('dbo.Members', 'U') IS NOT NULL DROP TABLE dbo.Members;"
    $command = New-Object System.Data.SqlClient.SqlCommand($dropQuery, $connection)
    $command.ExecuteNonQuery() | Out-Null
    Write-Host "Dropped existing Members table (if any)" -ForegroundColor Gray

    # Create table
    $createQuery = @"
CREATE TABLE [dbo].[Members] (
    [MemberId] INT IDENTITY(1,1) PRIMARY KEY,
    [FirstName] NVARCHAR(100) NOT NULL,
    [LastName] NVARCHAR(100) NOT NULL,
    [Phone] NVARCHAR(20) NOT NULL DEFAULT '',
    [Email] NVARCHAR(100) NOT NULL DEFAULT '',
    [Status] NVARCHAR(20) NOT NULL DEFAULT 'Active',
    [JoinDate] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);
"@

    $command = New-Object System.Data.SqlClient.SqlCommand($createQuery, $connection)
    $command.ExecuteNonQuery() | Out-Null
    Write-Host "✅ Members Table Created on MonsterASP" -ForegroundColor Green

    $connection.Close()
}
catch {
    Write-Host "❌ Failed to create table: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""

# ============================================
# GET DATA FROM LOCALDB
# ============================================

Write-Host "Step 3: Reading Data from LocalDB..." -ForegroundColor Yellow
Write-Host ""

$LocalDBConnString = "Server=$LocalDBServer;Database=$LocalDBDatabase;Trusted_Connection=True;Encrypt=false;TrustServerCertificate=True;"

$sourceConn = New-Object System.Data.SqlClient.SqlConnection
$sourceConn.ConnectionString = $LocalDBConnString

try {
    $sourceConn.Open()

    $query = "SELECT MemberId, FirstName, LastName, Phone, Email, Status, JoinDate, CreatedAt FROM Members"
    $command = New-Object System.Data.SqlClient.SqlCommand($query, $sourceConn)
    $reader = $command.ExecuteReader()

    $members = @()
    while ($reader.Read()) {
        $members += @{
            FirstName = $reader["FirstName"]
            LastName = $reader["LastName"]
            Phone = $reader["Phone"]
            Email = $reader["Email"]
            Status = $reader["Status"]
            JoinDate = $reader["JoinDate"]
            CreatedAt = $reader["CreatedAt"]
        }
    }

    Write-Host "✅ Read $($members.Count) members from LocalDB" -ForegroundColor Green

    $sourceConn.Close()
}
catch {
    Write-Host "❌ Failed to read from LocalDB: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""

# ============================================
# TRANSFER DATA TO MONSTERASP
# ============================================

Write-Host "Step 4: Transferring Data to MonsterASP..." -ForegroundColor Yellow
Write-Host ""

$destConn = New-Object System.Data.SqlClient.SqlConnection
$destConn.ConnectionString = $MonsterASPConnString

try {
    $destConn.Open()

    $insertCount = 0
    foreach ($member in $members) {
        $firstName = $member.FirstName -replace "'", "''"  # Escape quotes
        $lastName = $member.LastName -replace "'", "''"
        $phone = $member.Phone -replace "'", "''"
        $email = $member.Email -replace "'", "''"
        $status = $member.Status -replace "'", "''"

        $insertQuery = "INSERT INTO [dbo].[Members] (FirstName, LastName, Phone, Email, Status, JoinDate, CreatedAt) VALUES ('$firstName', '$lastName', '$phone', '$email', '$status', '$($member.JoinDate)', '$($member.CreatedAt)');"

        $command = New-Object System.Data.SqlClient.SqlCommand($insertQuery, $destConn)
        $command.ExecuteNonQuery() | Out-Null
        $insertCount++
    }

    Write-Host "✅ Inserted $insertCount members to MonsterASP" -ForegroundColor Green

    $destConn.Close()
}
catch {
    Write-Host "❌ Failed to transfer data: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""

# ============================================
# VERIFY DATA ON MONSTERASP
# ============================================

Write-Host "Step 5: Verifying Data on MonsterASP..." -ForegroundColor Yellow
Write-Host ""

$verifyConn = New-Object System.Data.SqlClient.SqlConnection
$verifyConn.ConnectionString = $MonsterASPConnString

try {
    $verifyConn.Open()

    $countQuery = "SELECT COUNT(*) as MemberCount FROM [dbo].[Members]"
    $command = New-Object System.Data.SqlClient.SqlCommand($countQuery, $verifyConn)
    $result = $command.ExecuteScalar()

    Write-Host "✅ Total Members on MonsterASP: $result" -ForegroundColor Green

    # Show sample data
    $sampleQuery = "SELECT TOP 5 MemberId, FirstName, LastName, Email FROM [dbo].[Members]"
    $command = New-Object System.Data.SqlClient.SqlCommand($sampleQuery, $verifyConn)
    $reader = $command.ExecuteReader()

    Write-Host ""
    Write-Host "Sample Members from MonsterASP:" -ForegroundColor Cyan
    while ($reader.Read()) {
        $id = $reader["MemberId"]
        $firstName = $reader["FirstName"]
        $lastName = $reader["LastName"]
        $email = $reader["Email"]
        Write-Host "  ID: $id | Name: $firstName $lastName | Email: $email" -ForegroundColor Green
    }

    $verifyConn.Close()
}
catch {
    Write-Host "❌ Verification failed: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║  ✅ MIGRATION COMPLETE - DATA ON MONSTERASP!               ║" -ForegroundColor Green
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Cyan
Write-Host "1. Update appsettings.json with your password" -ForegroundColor Gray
Write-Host "2. Run: dotnet run" -ForegroundColor Gray
Write-Host "3. Test CRUD operations" -ForegroundColor Gray
Write-Host "4. All data now persists on MonsterASP!" -ForegroundColor Gray
Write-Host ""
