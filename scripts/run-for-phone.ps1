<#
.SYNOPSIS
    Starts YoPay so a phone on the same wifi can reach it.

.DESCRIPTION
    run-all.ps1 binds every host to localhost, which is right for a laptop and useless for
    a handset: localhost on the phone is the phone. This starts the same services on
    0.0.0.0 instead and prints the address to type into the app.

    Written for Windows PowerShell 5.1 as well as 7, so no ternary and no null coalescing.
#>
[CmdletBinding()]
param(
    [string] $Root = $PSScriptRoot + '\..'
)

$Root = (Resolve-Path $Root).Path
Set-Location $Root

$envScript = Join-Path $Root 'local.ps1'
if (-not (Test-Path $envScript)) {
    Write-Host 'local.ps1 not found. Copy local.ps1.example and put a key in it.' -ForegroundColor Red
    exit 1
}

. $envScript

# The address the phone has to dial. Wireless first, because that is the interface the
# phone is on; a laptop plugged into ethernet while the phone is on wifi will not work at
# all, and it is better to say so than to print an address that cannot answer.
$wifi = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object {
        $_.IPAddress -notlike '127.*' -and
        $_.IPAddress -notlike '169.254.*' -and
        $_.PrefixOrigin -ne 'WellKnown'
    } |
    Sort-Object -Property @{ Expression = { $_.InterfaceAlias -like '*Wi-Fi*' } } -Descending

if (-not $wifi) {
    Write-Host 'No usable network address found. Is wifi on?' -ForegroundColor Red
    exit 1
}

$address = $wifi[0].IPAddress
$adapter = $wifi[0].InterfaceAlias

# The firewall refuses inbound connections to these ports until told otherwise, and the
# refusal looks exactly like a wrong IP address from the phone's side.
$rule = Get-NetFirewallRule -DisplayName 'YoPay local trial' -ErrorAction SilentlyContinue

if (-not $rule) {
    Write-Host ''
    Write-Host 'Windows Firewall is still blocking the phone. Run this once, in an' -ForegroundColor Yellow
    Write-Host 'Administrator PowerShell:' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '  New-NetFirewallRule -DisplayName "YoPay local trial" -Direction Inbound ' -ForegroundColor White -NoNewline
    Write-Host '-Action Allow -Protocol TCP -LocalPort 5080,5081,5082' -ForegroundColor White
    Write-Host ''
    Write-Host 'Delete it when the trial is over:' -ForegroundColor DarkGray
    Write-Host '  Remove-NetFirewallRule -DisplayName "YoPay local trial"' -ForegroundColor DarkGray
    Write-Host ''
}

$running = Get-Process dotnet -ErrorAction SilentlyContinue
if ($running) {
    Write-Host 'stopping running services' -ForegroundColor DarkGray
    $running | Stop-Process -Force
    Start-Sleep -Seconds 2
}

$services = @(
    @{ Name = 'Api';      Project = 'src\YoPay.Api';        Port = 5080 },
    @{ Name = 'Ingest';   Project = 'src\YoPay.Ingest.Api'; Port = 5081 },
    @{ Name = 'Checkout'; Project = 'src\YoPay.Checkout';   Port = 5082 },
    @{ Name = 'Worker';   Project = 'src\YoPay.Worker';     Port = 5083 }
)

foreach ($service in $services) {
    Write-Host ("starting {0,-9} 0.0.0.0:{1}" -f $service.Name, $service.Port) -ForegroundColor DarkGray

    $command = ". '$envScript'; Set-Location '$Root'; " +
               "dotnet run --project $($service.Project) --urls http://0.0.0.0:$($service.Port)"

    Start-Process powershell -ArgumentList '-NoExit', '-Command', $command | Out-Null
}

Write-Host ''
Write-Host "Adapter:   $adapter" -ForegroundColor DarkGray
Write-Host "Dashboard: http://localhost:5080/dashboard" -ForegroundColor Green
Write-Host ''
Write-Host "In the app, put this in Server address:" -ForegroundColor Green
Write-Host "    http://${address}:5081" -ForegroundColor White
Write-Host ''
Write-Host "Check it from the phone's browser first - it should answer with some JSON:" -ForegroundColor DarkGray
Write-Host "    http://${address}:5081/health" -ForegroundColor DarkGray
