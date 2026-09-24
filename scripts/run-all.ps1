<#
.SYNOPSIS
    Starts every service in its own window with the same environment.

.DESCRIPTION
    One command instead of four terminals, and - more to the point - one place where the
    key ring is set. Half an evening went into "Security:ActiveKeyId is not configured"
    because each window had a different one, or none.

    Written for Windows PowerShell 5.1 as well as 7, so it avoids the ternary operator,
    null coalescing and the .NET Core only crypto overloads.
#>
[CmdletBinding()]
param(
    [string] $Root = $PSScriptRoot + '\..'
)

$Root = (Resolve-Path $Root).Path
Set-Location $Root

$envScript = Join-Path $Root 'local.ps1'
if (-not (Test-Path $envScript)) {
    Write-Host 'local.ps1 not found. Create it first - see docs/running-locally.md.' -ForegroundColor Red
    exit 1
}

. $envScript

# A previous run still holds the build output, and dotnet answers that with a file lock
# rather than anything that explains itself. Stopping them first is the difference between
# one command and fifteen minutes of confusion.
$running = Get-Process dotnet -ErrorAction SilentlyContinue
if ($running) {
    Write-Host 'stopping running services' -ForegroundColor DarkGray
    $running | Stop-Process -Force
    Start-Sleep -Seconds 2
}

$services = @(
    @{ Name = 'Api';       Project = 'src\YoPay.Api';       Url = 'http://localhost:5080/dashboard' },
    @{ Name = 'Ingest';    Project = 'src\YoPay.Ingest.Api'; Url = 'http://localhost:5081/health' },
    @{ Name = 'Checkout';  Project = 'src\YoPay.Checkout';  Url = 'http://localhost:5082/health' },
    @{ Name = 'Worker';    Project = 'src\YoPay.Worker';    Url = 'http://localhost:5083/health' }
)

foreach ($service in $services) {
    Write-Host ("starting {0,-9} {1}" -f $service.Name, $service.Url) -ForegroundColor DarkGray

    $command = ". '$envScript'; Set-Location '$Root'; dotnet run --project $($service.Project)"
    Start-Process powershell -ArgumentList '-NoExit', '-Command', $command | Out-Null
}

Write-Host ''
Write-Host 'Dashboard: http://localhost:5080/dashboard' -ForegroundColor Green
