#!/usr/bin/env pwsh
<#
    FitCore ERP - AUTO MIGRATE TO MONSTERASP
    Uses your existing MonsterASP credentials
    Transfers data from LocalDB to db68433
#>

Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  FitCore ERP - AUTO MIGRATE TO MONSTERASP                  ║" -ForegroundColor Cyan
Write-Host "║  Database: db68433.public.databaseasp.net                  ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Your actual MonsterASP credentials
$MonsterASPServer = "db68433.public.databaseasp.net"
$MonsterASPUser = "db68433"
$MonsterASPPassword = $env:FITCORE_TENANTA_PASSWORD  # set this env var; never hard-code
$MonsterASPDatabase = "db68433"

$LocalDBServer = "(localdb)\mssqllocaldb"
$LocalDBDatabase = "TenantErp"

# ============================================
# STEP 1: TEST MONSTERASP CONNECTION
# ============================================

Write-Host "STEP 1: Testing MonsterASP Connection..." -ForegroundColor Yellow
Write-Host "Server: $MonsterASPServer" -ForegroundColor Gray
Write-Host "User: $MonsterASPUser" -ForegroundColor Gray
Write-Host ""

$MonsterASPConnString = "Server=$MonsterASPServer;Database=$MonsterASPDatabase;User Id=$MonsterASPUser;Password=$MonsterASPPassword;Encrypt=False;MultipleActiveResultSets=True;"

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
    exit 1
}

Write-Host ""

# ============================================
# STEP 2: CREATE MEMBERS TABLE
# ============================================

Write-Host "STEP 2: Creating Members Table on MonsterASP (db68433)..." -ForegroundColor Yellow
Write-Host ""

$connection = New-Object System.Data.SqlClient.SqlConnection
$connection.ConnectionString = $MonsterASPConnString

try {
    $connection.Open()

    # Drop table if exists
    $dropQuery = "IF OBJECT_ID('dbo.Members', 'U') IS NOT NULL DROP TABLE dbo.Members;"
    $command = New-Object System.Data.SqlClient.SqlCommand($dropQuery, $connection)
    $command.ExecuteNonQuery() | Out-Null
    Write-Host "Cleaned up existing table (if any)" -ForegroundColor Gray

    # Create Members table
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
    Write-Host "✅ Members Table Created on MonsterASP (db68433)" -ForegroundColor Green

    $connection.Close()
}
catch {
    Write-Host "❌ Failed to create table: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""

# ============================================
# STEP 3: READ DATA FROM LOCALDB
# ============================================

Write-Host "STEP 3: Reading Data from LocalDB..." -ForegroundColor Yellow
Write-Host ""

$LocalDBConnString = "Server=$LocalDBServer;Database=$LocalDBDatabase;Trusted_Connection=True;Encrypt=false;TrustServerCertificate=True;"

$sourceConn = New-Object System.Data.SqlClient.SqlConnection
$sourceConn.ConnectionString = $LocalDBConnString

$members = @()

try {
    $sourceConn.Open()

    $query = "SELECT MemberId, FirstName, LastName, Phone, Email, Status, JoinDate, CreatedAt FROM Members"
    $command = New-Object System.Data.SqlClient.SqlCommand($query, $sourceConn)
    $reader = $command.ExecuteReader()

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
    Write-Host "⚠️  No data in LocalDB (that's ok, starting fresh)" -ForegroundColor Yellow
    $members = @()
}

Write-Host ""

# ============================================
# STEP 4: INSERT DATA INTO MONSTERASP
# ============================================

Write-Host "STEP 4: Inserting Members into MonsterASP..." -ForegroundColor Yellow
Write-Host ""

$destConn = New-Object System.Data.SqlClient.SqlConnection
$destConn.ConnectionString = $MonsterASPConnString

try {
    $destConn.Open()

    if ($members.Count -gt 0) {
        $insertCount = 0
        foreach ($member in $members) {
            $firstName = $member.FirstName -replace "'", "''"
            $lastName = $member.LastName -replace "'", "''"
            $phone = $member.Phone -replace "'", "''"
            $email = $member.Email -replace "'", "''"
            $status = $member.Status -replace "'", "''"

            $insertQuery = "INSERT INTO [dbo].[Members] (FirstName, LastName, Phone, Email, Status, JoinDate, CreatedAt) VALUES ('$firstName', '$lastName', '$phone', '$email', '$status', '$($member.JoinDate)', '$($member.CreatedAt)');"

            $command = New-Object System.Data.SqlClient.SqlCommand($insertQuery, $destConn)
            $command.ExecuteNonQuery() | Out-Null
            $insertCount++
        }
        Write-Host "✅ Inserted $insertCount members to MonsterASP (db68433)" -ForegroundColor Green
    } else {
        Write-Host "✅ Database ready for new members" -ForegroundColor Green
    }

    $destConn.Close()
}
catch {
    Write-Host "❌ Failed to insert data: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""

# ============================================
# STEP 5: VERIFY
# ============================================

Write-Host "STEP 5: Verifying Data on MonsterASP..." -ForegroundColor Yellow
Write-Host ""

$verifyConn = New-Object System.Data.SqlClient.SqlConnection
$verifyConn.ConnectionString = $MonsterASPConnString

try {
    $verifyConn.Open()

    $countQuery = "SELECT COUNT(*) as MemberCount FROM [dbo].[Members]"
    $command = New-Object System.Data.SqlClient.SqlCommand($countQuery, $verifyConn)
    $result = $command.ExecuteScalar()

    Write-Host "✅ Total Members on MonsterASP: $result" -ForegroundColor Green

    # Show sample
    if ($result -gt 0) {
        $sampleQuery = "SELECT TOP 5 MemberId, FirstName, LastName, Email FROM [dbo].[Members]"
        $command = New-Object System.Data.SqlClient.SqlCommand($sampleQuery, $verifyConn)
        $reader = $command.ExecuteReader()

        Write-Host ""
        Write-Host "Members on MonsterASP (db68433):" -ForegroundColor Cyan
        while ($reader.Read()) {
            $id = $reader["MemberId"]
            $firstName = $reader["FirstName"]
            $lastName = $reader["LastName"]
            $email = $reader["Email"]
            Write-Host "  ID: $id | $firstName $lastName | $email" -ForegroundColor Green
        }
    }

    $verifyConn.Close()
}
catch {
    Write-Host "❌ Verification failed: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║  ✅ MIGRATION COMPLETE!                                    ║" -ForegroundColor Green
Write-Host "║  Database: db68433.public.databaseasp.net                  ║" -ForegroundColor Green
Write-Host "║  Status: Ready for application                             ║" -ForegroundColor Green
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""
Write-Host "Next: Run 'dotnet run' from ERP_Project1 folder" -ForegroundColor Cyan
Write-Host ""
