# FitCore ERP - the development workflow, in one place.
#
#   .\fitcore.ps1 stop      stop any running FitCore process
#   .\fitcore.ps1 build     stop, then clean, restore and build the solution
#   .\fitcore.ps1 rebuild   as build, but also removes bin/obj first
#   .\fitcore.ps1 db        show migration state for the master and all three tenants
#   .\fitcore.ps1 update    apply pending migrations to the master and all three tenants
#   .\fitcore.ps1 api       start ERP_api (https://localhost:7214)
#   .\fitcore.ps1 ui        start ERP_winforms
#   .\fitcore.ps1 run       stop, build, start the API, wait for it, start the desktop client
#   .\fitcore.ps1 test      run the xUnit suite
#
# Why this exists: a running ERP_winforms.exe or ERP_api holds an exclusive lock on its own
# output assembly, and MSBuild then fails with MSB3021/MSB3027 "being used by another
# process". Every build path here stops those processes first, so the lock never happens.
# Nothing else on the machine is touched - only processes whose executable lives under this
# repository.

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('stop', 'build', 'rebuild', 'db', 'update', 'api', 'ui', 'run', 'test')]
    [string]$Command = 'run'
)

$ErrorActionPreference = 'Stop'

$Root           = $PSScriptRoot
$Solution       = Join-Path $Root 'ERP_Project1.slnx'
$ApiProject     = Join-Path $Root 'ERP_api\ERP_api.csproj'
$WinFormsProject= Join-Path $Root 'ERP_Project1\ERP_winforms.csproj'
$InfraProject   = Join-Path $Root 'ERP_infrastructure'
$TestProject    = Join-Path $Root 'ERP_Tests\ERP_Tests.csproj'
$ApiUrl         = 'https://localhost:7214'

# The projects whose bin/obj a rebuild may clear. ERP_UI is deliberately absent: it is
# retired, is not in the solution, and must not be touched by the build.
$ActiveProjects = 'ERP_Project1', 'ERP_api', 'ERP_infrastructure', 'ERP_domain', 'ERP_Tests'

function Write-Step([string]$text) {
    Write-Host ''
    Write-Host "==> $text" -ForegroundColor Cyan
}

function Write-Ok([string]$text)   { Write-Host "    $text" -ForegroundColor Green }
function Write-Note([string]$text) { Write-Host "    $text" -ForegroundColor DarkGray }

# ---------------------------------------------------------------------------- stop

<#
    Stops only FitCore processes: ERP_winforms.exe, and any dotnet.exe whose command line
    names a project inside this repository. An unrelated dotnet tool - or somebody else's
    application that happens to be called something similar - is left alone.
#>
function Stop-FitCore {
    $stopped = 0

    foreach ($p in @(Get-Process -Name 'ERP_winforms' -ErrorAction SilentlyContinue)) {
        try {
            $path = $p.Path
            if (-not $path -or $path.StartsWith($Root, [StringComparison]::OrdinalIgnoreCase)) {
                Stop-Process -Id $p.Id -Force
                Write-Note "stopped ERP_winforms (pid $($p.Id))"
                $stopped++
            }
        } catch { }
    }

    # ERP_api runs either as its own apphost or under dotnet.exe, depending on how it was
    # started, so both are checked by command line rather than by name alone.
    foreach ($p in @(Get-Process -Name 'ERP_api' -ErrorAction SilentlyContinue)) {
        try { Stop-Process -Id $p.Id -Force; Write-Note "stopped ERP_api (pid $($p.Id))"; $stopped++ } catch { }
    }

    $escaped = $Root.Replace('\', '\\')
    $hosts = Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
             Where-Object { $_.CommandLine -and $_.CommandLine -like "*$Root*" -and $_.CommandLine -notlike '*fitcore.ps1*' }

    foreach ($h in $hosts) {
        try {
            Stop-Process -Id $h.ProcessId -Force
            Write-Note "stopped dotnet host (pid $($h.ProcessId))"
            $stopped++
        } catch { }
    }

    if ($stopped -eq 0) { Write-Note 'no FitCore process was running' }
    else { Start-Sleep -Milliseconds 800 }   # let Windows release the file handles

    return $stopped
}

function Assert-NoLocks {
    $locked = @()

    foreach ($name in $ActiveProjects) {
        $bin = Join-Path $Root "$name\bin"
        if (-not (Test-Path $bin)) { continue }

        foreach ($dll in Get-ChildItem $bin -Recurse -Include *.exe, *.dll -ErrorAction SilentlyContinue) {
            try {
                $s = [System.IO.File]::Open($dll.FullName, 'Open', 'ReadWrite', 'None')
                $s.Close()
            } catch {
                $locked += $dll.FullName
            }
        }
    }

    if ($locked.Count -gt 0) {
        Write-Host "    still locked:" -ForegroundColor Yellow
        $locked | Select-Object -First 5 | ForEach-Object { Write-Host "      $_" -ForegroundColor Yellow }
        throw "Build output is still locked by a running process. Close it and try again."
    }

    Write-Ok 'no stale FitCore process, no locked output'
}

# ---------------------------------------------------------------------------- build

function Invoke-Build([switch]$Hard) {
    Write-Step 'Stopping FitCore processes'
    Stop-FitCore | Out-Null
    Assert-NoLocks

    if ($Hard) {
        Write-Step 'Removing bin and obj'
        foreach ($name in $ActiveProjects) {
            foreach ($dir in 'bin', 'obj') {
                $path = Join-Path $Root "$name\$dir"
                if (Test-Path $path) { Remove-Item $path -Recurse -Force; Write-Note "removed $name\$dir" }
            }
        }
    }

    Write-Step 'dotnet clean'
    dotnet clean $Solution --nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'clean failed' }

    Write-Step 'dotnet restore'
    dotnet restore $Solution --nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'restore failed' }

    Write-Step 'dotnet build'
    dotnet build $Solution --nologo --no-restore -v m | Select-String -Pattern 'error|warning|Build succeeded|-> '
    if ($LASTEXITCODE -ne 0) { throw 'build failed' }

    Write-Ok 'build succeeded'
}

# ---------------------------------------------------------------------------- database

<#
    One tenant schema, three tenant databases plus the master. TENANT_ERP_CONNECTION_NAME selects
    which tenant the design-time factory resolves; left unset it means Tenant A. The factory
    prints the target before doing anything, which is the line to read before applying anything.

    A tenant missing from this list is a tenant that silently never gets migrated, so adding a
    database means adding it here.
#>
$Targets = @(
    @{ Name = 'master         '; Context = 'MasterErpDbContext'; Connection = $null },
    @{ Name = 'tenant A micro '; Context = 'TenantErpDbContext'; Connection = $null },
    @{ Name = 'tenant B small '; Context = 'TenantErpDbContext'; Connection = 'TenantErpB' },
    @{ Name = 'tenant C medium'; Context = 'TenantErpDbContext'; Connection = 'TenantErpC' }
)

function Invoke-Ef([string]$verb, [hashtable]$target) {
    if ($target.Connection) { $env:TENANT_ERP_CONNECTION_NAME = $target.Connection }
    else { Remove-Item Env:\TENANT_ERP_CONNECTION_NAME -ErrorAction SilentlyContinue }

    try {
        if ($verb -eq 'list') {
            dotnet ef migrations list --context $target.Context `
                --project $InfraProject --startup-project $ApiProject --no-build
        }
        else {
            dotnet ef database update --context $target.Context `
                --project $InfraProject --startup-project $ApiProject --no-build
        }
    }
    finally {
        Remove-Item Env:\TENANT_ERP_CONNECTION_NAME -ErrorAction SilentlyContinue
    }
}

function Show-Database {
    foreach ($t in $Targets) {
        Write-Step "Migrations - $($t.Name.Trim())"
        $out = Invoke-Ef 'list' $t
        $out | Where-Object { $_ -match 'design-time|Pending' } | ForEach-Object { Write-Note $_ }
        $applied = @($out | Where-Object { $_ -match '^\d{14}_' })
        $pending = @($applied | Where-Object { $_ -match 'Pending' })
        Write-Ok "$($applied.Count) migration(s), $($pending.Count) pending"
    }
}

function Update-Database {
    foreach ($t in $Targets) {
        Write-Step "Updating - $($t.Name.Trim())"
        Invoke-Ef 'update' $t | Where-Object { $_ -match 'design-time|Applying|already up to date|Done' } |
            ForEach-Object { Write-Note $_ }
        if ($LASTEXITCODE -ne 0) { throw "database update failed for $($t.Name.Trim())" }
    }
    Write-Ok 'the master and all three tenant databases are up to date'
}

# ---------------------------------------------------------------------------- run

function Start-Api {
    Write-Step "Starting ERP_api ($ApiUrl)"
    Start-Process dotnet -ArgumentList @(
        'run', '--project', $ApiProject, '--launch-profile', 'https', '--no-build'
    ) -WorkingDirectory $Root -WindowStyle Minimized
}

<#
    Waits for Kestrel to accept connections on the HTTPS port.

    Deliberately a TCP connect rather than an HTTP request: Windows PowerShell 5.1's
    Invoke-WebRequest is unreliable against a loopback HTTPS endpoint with a self-signed
    development certificate - it intermittently reports "the underlying connection was
    closed" for a server that is perfectly healthy. Whether the port is accepting is the
    only thing this needs to know.
#>
function Wait-Api([int]$timeoutSeconds = 90) {
    $port = ([Uri]$ApiUrl).Port
    Write-Note "waiting for $ApiUrl ..."

    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $client = New-Object System.Net.Sockets.TcpClient
        try {
            $client.Connect('localhost', $port)
            if ($client.Connected) { $client.Close(); Write-Ok "API is listening on $ApiUrl"; return $true }
        }
        catch { }
        finally { $client.Dispose() }

        Start-Sleep -Seconds 2
    }

    throw "The API did not start listening on port $port within $timeoutSeconds seconds."
}

function Start-Ui {
    Write-Step 'Starting ERP_winforms'
    $exe = Join-Path $Root 'ERP_Project1\bin\Debug\net10.0-windows\ERP_winforms.exe'
    if (-not (Test-Path $exe)) { throw "Not built yet: $exe" }
    Start-Process $exe -WorkingDirectory (Split-Path $exe)
    Write-Ok 'desktop client launched'
}

# ---------------------------------------------------------------------------- dispatch

switch ($Command) {
    'stop'    { Write-Step 'Stopping FitCore processes'; Stop-FitCore | Out-Null; Assert-NoLocks }
    'build'   { Invoke-Build }
    'rebuild' { Invoke-Build -Hard }
    'db'      { Show-Database }
    'update'  { Update-Database }
    'api'     { Start-Api; Wait-Api | Out-Null }
    'ui'      { Start-Ui }
    'test'    { Write-Step 'dotnet test'; dotnet test $TestProject --nologo -v q }
    'run'     { Invoke-Build; Start-Api; Wait-Api | Out-Null; Start-Ui }
}

Write-Host ''
