<#
.SYNOPSIS
    Starts YoPay so a phone can reach it, over shared wifi or over the phone's hotspot.

.DESCRIPTION
    run-all.ps1 binds every host to localhost, which is right for a laptop and useless for
    a handset: localhost on the phone is the phone. This starts the same services on
    0.0.0.0 instead and prints the address to type into the app.

    It also checks the two things that make a correct setup look broken from the phone,
    because both of them fail in the same way - the browser spins and then says it cannot
    reach the site, with nothing in any log to say why:

      1. Windows files a new network as Public, and inbound connections are refused on
         Public no matter what the firewall rule says, if the rule was written for Private.
         A phone hotspot is always a new network, so this is the common case, not the edge.

      2. The firewall rule may simply not be there yet.

    Written for Windows PowerShell 5.1 as well as 7, so no ternary and no null coalescing.

.PARAMETER SkipHealthCheck
    Do not wait for the services to answer. Faster, and worth it only if you already know
    they come up.
#>
[CmdletBinding()]
param(
    [string] $Root = $PSScriptRoot + '\..',
    [switch] $SkipHealthCheck
)

$Root = (Resolve-Path $Root).Path
Set-Location $Root

$envScript = Join-Path $Root 'local.ps1'
if (-not (Test-Path $envScript)) {
    Write-Host 'local.ps1 not found. Copy local.ps1.example and put a key in it.' -ForegroundColor Red
    exit 1
}

. $envScript

$ports = @(5080, 5081, 5082, 5083)

# ---------------------------------------------------------------------------
# The address the phone has to dial.
#
# Wireless first, because that is the interface the phone is on - both when the two share
# a router and when the laptop is a client of the phone's hotspot. A laptop on ethernet
# while the phone is on wifi cannot work at all, and saying so beats printing an address
# that will never answer.
# ---------------------------------------------------------------------------
$candidates = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object {
        $_.IPAddress -notlike '127.*' -and
        $_.IPAddress -notlike '169.254.*' -and
        $_.PrefixOrigin -ne 'WellKnown'
    } |
    Sort-Object -Property @{ Expression = { $_.InterfaceAlias -like '*Wi-Fi*' } } -Descending

if (-not $candidates) {
    Write-Host 'No usable network address found.' -ForegroundColor Red
    Write-Host 'Wifi off, or not connected to the hotspot yet?' -ForegroundColor Red
    exit 1
}

$address = $candidates[0].IPAddress
$adapter = $candidates[0].InterfaceAlias
$ifIndex = $candidates[0].InterfaceIndex

# 169.254.* was filtered out above, but a half-connected adapter can still hand back
# something unroutable. Better to name it than to let the phone discover it.
if ($address -like '169.*') {
    Write-Host "Got $address, which means the adapter never finished connecting." -ForegroundColor Red
    exit 1
}

# ---------------------------------------------------------------------------
# Public vs Private. This is the one that wastes an evening.
# ---------------------------------------------------------------------------
$profileName = $null
$isPublic = $false

$connection = Get-NetConnectionProfile -InterfaceIndex $ifIndex -ErrorAction SilentlyContinue
if ($connection) {
    $profileName = $connection.Name
    $isPublic = ($connection.NetworkCategory -eq 'Public')
}

if ($isPublic) {
    Write-Host ''
    Write-Host "Windows has filed '$profileName' as a Public network." -ForegroundColor Yellow
    Write-Host 'On Public it refuses inbound connections, and from the phone that looks' -ForegroundColor Yellow
    Write-Host 'exactly like a wrong IP address. Run this once, as Administrator:' -ForegroundColor Yellow
    Write-Host ''
    Write-Host "  Set-NetConnectionProfile -Name '$profileName' -NetworkCategory Private" -ForegroundColor White
    Write-Host ''
}

# ---------------------------------------------------------------------------
# The firewall rule.
#
# -Profile Any on purpose. A rule scoped to Private stops working the moment Windows
# decides a network is Public, which it does for every hotspot you have not corrected
# yet - so the rule that is supposed to save you is missing precisely when you need it.
# ---------------------------------------------------------------------------
$ruleName = 'YoPay local trial'
$rule = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue

if (-not $rule) {
    Write-Host ''
    Write-Host 'Windows Firewall is still blocking the phone. Run this once, as' -ForegroundColor Yellow
    Write-Host 'Administrator:' -ForegroundColor Yellow
    Write-Host ''
    Write-Host "  New-NetFirewallRule -DisplayName '$ruleName' -Direction Inbound ``" -ForegroundColor White
    Write-Host ("    -Action Allow -Protocol TCP -LocalPort {0} -Profile Any" -f ($ports -join ',')) -ForegroundColor White
    Write-Host ''
    Write-Host 'Delete it when the trial is over:' -ForegroundColor DarkGray
    Write-Host "  Remove-NetFirewallRule -DisplayName '$ruleName'" -ForegroundColor DarkGray
    Write-Host ''
}
elseif ($rule.Profile -notmatch 'Any' -and $isPublic) {
    Write-Host ''
    Write-Host "The '$ruleName' rule exists but is scoped to $($rule.Profile), and this" -ForegroundColor Yellow
    Write-Host 'network is Public, so it does not apply. Widen it, as Administrator:' -ForegroundColor Yellow
    Write-Host ''
    Write-Host "  Set-NetFirewallRule -DisplayName '$ruleName' -Profile Any" -ForegroundColor White
    Write-Host ''
}

# ---------------------------------------------------------------------------
# Start the services.
# ---------------------------------------------------------------------------
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

# ---------------------------------------------------------------------------
# Answer the question from this machine before the phone is involved.
#
# Dialling the LAN address rather than localhost is the whole point: localhost would
# answer even with the firewall shut, and then the phone would fail and the firewall
# would be the last place anyone looked.
# ---------------------------------------------------------------------------
if (-not $SkipHealthCheck) {
    Write-Host ''
    Write-Host "waiting for http://${address}:5081/health" -ForegroundColor DarkGray

    $ok = $false
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Seconds 3
        try {
            $response = Invoke-WebRequest -Uri "http://${address}:5081/health" `
                -UseBasicParsing -TimeoutSec 4 -ErrorAction Stop
            if ($response.StatusCode -eq 200) { $ok = $true; break }
        }
        catch {
            # Still coming up. dotnet run restores and builds before it listens, so the
            # first half-minute of refusals says nothing.
        }
    }

    Write-Host ''
    if ($ok) {
        Write-Host "reachable on ${address}:5081" -ForegroundColor Green
        Write-Host 'The network side is proven. Anything that fails from here is the app' -ForegroundColor DarkGray
        Write-Host 'or the phone, not the firewall.' -ForegroundColor DarkGray
    }
    else {
        Write-Host "no answer on ${address}:5081 after two minutes" -ForegroundColor Red
        Write-Host ''
        Write-Host 'In order of likelihood:' -ForegroundColor Red
        Write-Host '  - the firewall rule above was never created, or is Private-only' -ForegroundColor Red
        Write-Host '  - the network is still Public' -ForegroundColor Red
        Write-Host '  - a service window opened and died; read the red text in it' -ForegroundColor Red
        Write-Host ''
        Write-Host 'Do not go to the phone yet. It cannot succeed where this failed.' -ForegroundColor Red
    }
}

Write-Host ''
Write-Host "Adapter:   $adapter" -ForegroundColor DarkGray
if ($profileName) {
    Write-Host "Network:   $profileName" -ForegroundColor DarkGray
}
Write-Host "Dashboard: http://localhost:5080/dashboard" -ForegroundColor Green
Write-Host ''
Write-Host 'In the app, put this in Server address:' -ForegroundColor Green
Write-Host "    http://${address}:5081" -ForegroundColor White
Write-Host ''
Write-Host "Check it from the phone's browser first - it should answer with some JSON:" -ForegroundColor DarkGray
Write-Host "    http://${address}:5081/health" -ForegroundColor DarkGray
Write-Host ''
Write-Host 'The address changes every time you reconnect. Re-run this and retype it.' -ForegroundColor DarkGray
