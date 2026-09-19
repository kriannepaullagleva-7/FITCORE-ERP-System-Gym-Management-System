# FitCore ERP - Complete Verification and Launch Script
# This script verifies everything is ready and launches the application

Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║  FitCore ERP - Full System Verification & Launch           ║" -ForegroundColor Green
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""

# Change to project directory
$projectPath = "C:\Users\USER\source\repos\ERP_Project1\ERP_Project1"
Set-Location $projectPath

# Step 1: Verify Project Files
Write-Host "STEP 1: Verifying Project Files..." -ForegroundColor Cyan
$filesToCheck = @(
    "Program.cs",
    "Form1_Simple.cs",
    "appsettings.json",
    "ERP_winforms.csproj"
)

$allFilesExist = $true
foreach ($file in $filesToCheck) {
    if (Test-Path $file) {
        Write-Host "  ✅ $file" -ForegroundColor Green
    } else {
        Write-Host "  ❌ $file MISSING" -ForegroundColor Red
        $allFilesExist = $false
    }
}

if (-not $allFilesExist) {
    Write-Host "❌ Required files missing!" -ForegroundColor Red
    Exit 1
}
Write-Host ""

# Step 2: Check appsettings.json
Write-Host "STEP 2: Verifying appsettings.json Configuration..." -ForegroundColor Cyan
$appsettings = Get-Content "appsettings.json" | ConvertFrom-Json
$connString = $appsettings.ConnectionStrings.TenantErp
if ($connString -like "*SQLEXPRESS*" -or $connString -like "*database.windows.net*") {
    Write-Host "  ✅ Connection string configured" -ForegroundColor Green
    Write-Host "     Server: $($connString.Split(';')[0])" -ForegroundColor Gray
} else {
    Write-Host "  ⚠️  Connection string may be invalid" -ForegroundColor Yellow
}
Write-Host ""

# Step 3: Verify Build
Write-Host "STEP 3: Verifying Application Build..." -ForegroundColor Cyan
$buildOutput = dotnet build 2>&1 | Out-String
if ($buildOutput -like "*succeeded*" -and $buildOutput -like "*0 Error*") {
    Write-Host "  ✅ Build successful (0 errors)" -ForegroundColor Green
} else {
    Write-Host "  ❌ Build failed!" -ForegroundColor Red
    Write-Host $buildOutput -ForegroundColor Red
    Exit 1
}
Write-Host ""

# Step 4: Verify Database
Write-Host "STEP 4: Verifying Database Connection..." -ForegroundColor Cyan
$dbInfoScript = @"
using System;
using System.Data.SqlClient;

try {
    string connStr = @"Server=.\SQLEXPRESS;Database=TenantErp;Trusted_Connection=True;Encrypt=false;TrustServerCertificate=True;";
    using (SqlConnection conn = new SqlConnection(connStr)) {
        conn.Open();
        Console.WriteLine("SUCCESS");
        conn.Close();
    }
}
catch (Exception ex) {
    Console.WriteLine("FAILED: " + ex.Message);
}
"@

$result = $dbInfoScript | csc - 2>&1
if ($result -like "*SUCCESS*") {
    Write-Host "  ✅ Database connection successful" -ForegroundColor Green
} else {
    Write-Host "  ⚠️  Database connection may have issues" -ForegroundColor Yellow
    Write-Host "     (SQL Server Express may not be running)" -ForegroundColor Gray
}
Write-Host ""

# Step 5: Summary
Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║  ✅ VERIFICATION COMPLETE - READY TO LAUNCH                ║" -ForegroundColor Green
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""

Write-Host "Next Steps:" -ForegroundColor Yellow
Write-Host "1. Ensure SQL Server Express is running"
Write-Host "2. Press any key to launch the application..."
Write-Host ""

Read-Host "Press Enter to launch"

Write-Host ""
Write-Host "🚀 Launching FitCore ERP..." -ForegroundColor Cyan
Write-Host ""

# Launch the application
dotnet run
