$MonsterServer = "db68433.public.databaseasp.net"
$MonsterUser = "db68433"
$MonsterPass = $env:FITCORE_TENANTA_PASSWORD  # set this env var; never hard-code
$MonsterDB = "db68433"
$LocalServer = "(localdb)\mssqllocaldb"
$LocalDB = "TenantErp"

Write-Host "Starting Migration..." -ForegroundColor Yellow

# Test MonsterASP
$MonsterConn = "Server=$MonsterServer;Database=$MonsterDB;User Id=$MonsterUser;Password=$MonsterPass;Encrypt=False;MultipleActiveResultSets=True;"
$conn = New-Object System.Data.SqlClient.SqlConnection
$conn.ConnectionString = $MonsterConn

try {
    $conn.Open()
    Write-Host "OK: MonsterASP Connected" -ForegroundColor Green

    # Drop and create table
    $dropSql = "IF OBJECT_ID('dbo.Members', 'U') IS NOT NULL DROP TABLE dbo.Members;"
    $cmd = New-Object System.Data.SqlClient.SqlCommand($dropSql, $conn)
    $cmd.ExecuteNonQuery() | Out-Null

    $createSql = "CREATE TABLE dbo.Members (MemberId INT IDENTITY(1,1) PRIMARY KEY, FirstName NVARCHAR(100) NOT NULL, LastName NVARCHAR(100) NOT NULL, Phone NVARCHAR(20) NOT NULL DEFAULT '', Email NVARCHAR(100) NOT NULL DEFAULT '', Status NVARCHAR(20) NOT NULL DEFAULT 'Active', JoinDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(), CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE());"
    $cmd = New-Object System.Data.SqlClient.SqlCommand($createSql, $conn)
    $cmd.ExecuteNonQuery() | Out-Null

    Write-Host "OK: Table Created on MonsterASP" -ForegroundColor Green
    $conn.Close()
}
catch {
    Write-Host "ERROR: MonsterASP - $_" -ForegroundColor Red
    exit 1
}

# Read from LocalDB
$LocalConn = "Server=$LocalServer;Database=$LocalDB;Trusted_Connection=True;Encrypt=false;TrustServerCertificate=True;"
$srcConn = New-Object System.Data.SqlClient.SqlConnection
$srcConn.ConnectionString = $LocalConn
$members = @()

try {
    $srcConn.Open()
    $sql = "SELECT FirstName, LastName, Phone, Email, Status, JoinDate, CreatedAt FROM Members"
    $cmd = New-Object System.Data.SqlClient.SqlCommand($sql, $srcConn)
    $reader = $cmd.ExecuteReader()

    while ($reader.Read()) {
        $members += @{
            FN = $reader["FirstName"]
            LN = $reader["LastName"]
            PH = $reader["Phone"]
            EM = $reader["Email"]
            ST = $reader["Status"]
            JD = $reader["JoinDate"]
            CA = $reader["CreatedAt"]
        }
    }
    $srcConn.Close()
    Write-Host "OK: Read $($members.Count) members from LocalDB" -ForegroundColor Green
}
catch {
    Write-Host "WARNING: LocalDB empty or error: $_" -ForegroundColor Yellow
}

# Insert to MonsterASP
$destConn = New-Object System.Data.SqlClient.SqlConnection
$destConn.ConnectionString = $MonsterConn

try {
    $destConn.Open()

    $count = 0
    foreach ($m in $members) {
        $fn = $m.FN -replace "'", "''"
        $ln = $m.LN -replace "'", "''"
        $ph = $m.PH -replace "'", "''"
        $em = $m.EM -replace "'", "''"
        $st = $m.ST -replace "'", "''"

        $insertSql = "INSERT INTO dbo.Members (FirstName, LastName, Phone, Email, Status, JoinDate, CreatedAt) VALUES ('$fn', '$ln', '$ph', '$em', '$st', '$($m.JD)', '$($m.CA)');"
        $cmd = New-Object System.Data.SqlClient.SqlCommand($insertSql, $destConn)
        $cmd.ExecuteNonQuery() | Out-Null
        $count++
    }

    Write-Host "OK: Inserted $count members to MonsterASP" -ForegroundColor Green
    $destConn.Close()
}
catch {
    Write-Host "ERROR: Insert failed - $_" -ForegroundColor Red
    exit 1
}

# Verify
$verifyConn = New-Object System.Data.SqlClient.SqlConnection
$verifyConn.ConnectionString = $MonsterConn

try {
    $verifyConn.Open()
    $sql = "SELECT COUNT(*) as cnt FROM dbo.Members"
    $cmd = New-Object System.Data.SqlClient.SqlCommand($sql, $verifyConn)
    $result = $cmd.ExecuteScalar()
    Write-Host "OK: Verified $result members on MonsterASP (db68433)" -ForegroundColor Green
    $verifyConn.Close()
}
catch {
    Write-Host "WARNING: Verify failed - $_" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "SUCCESS: Migration Complete!" -ForegroundColor Green
Write-Host "Database: db68433.public.databaseasp.net" -ForegroundColor Cyan
Write-Host "Next: Run dotnet run" -ForegroundColor Cyan
