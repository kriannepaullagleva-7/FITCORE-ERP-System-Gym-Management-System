$connString = "Server=.\SQLEXPRESS;Database=TenantErp;Trusted_Connection=True;Encrypt=false;TrustServerCertificate=True;"
$connection = New-Object System.Data.SqlClient.SqlConnection
$connection.ConnectionString = $connString

try {
    $connection.Open()
    Write-Host "✅ Database connection successful!" -ForegroundColor Green

    # Check if Members table exists
    $query = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='Members'"
    $command = New-Object System.Data.SqlClient.SqlCommand($query, $connection)
    $result = $command.ExecuteScalar()

    if ($result -eq 1) {
        Write-Host "✅ Members table exists" -ForegroundColor Green

        # Check member count
        $countQuery = "SELECT COUNT(*) FROM Members"
        $countCommand = New-Object System.Data.SqlClient.SqlCommand($countQuery, $connection)
        $count = $countCommand.ExecuteScalar()
        Write-Host "✅ Total members in database: $count" -ForegroundColor Green
    } else {
        Write-Host "❌ Members table not found" -ForegroundColor Red
    }
}
catch {
    Write-Host "❌ Connection failed: $_" -ForegroundColor Red
}
finally {
    $connection.Close()
}
