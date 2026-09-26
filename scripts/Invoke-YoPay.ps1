<#
.SYNOPSIS
    Calls the YoPay merchant API with a signed request.

.DESCRIPTION
    Reproduces the canonical string the server builds:

        METHOD \n PATH_AND_QUERY \n UNIX_TIMESTAMP \n NONCE \n SHA256_HEX(BODY)

    and signs it with HMAC-SHA256. This script is the reference every SDK has to match,
    so if an SDK disagrees with it, the SDK is wrong.

.EXAMPLE
    $env:YOPAY_KEY_ID = 'abc123'
    $env:YOPAY_SECRET = 'base64-secret-from-the-seeder'

    .\scripts\Invoke-YoPay.ps1 -Path /v1/payment/create -Body @{
        orderRef = 'ORD-1001'
        amount   = 500
        method   = 1
    }
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $Path,
    [hashtable] $Body,
    [string] $Method = 'POST',
    [string] $BaseUrl = 'http://localhost:5080',
    [string] $KeyId = $env:YOPAY_KEY_ID,
    [string] $Secret = $env:YOPAY_SECRET
)

if (-not $KeyId -or -not $Secret) {
    throw 'Set YOPAY_KEY_ID and YOPAY_SECRET first. The seeder prints both.'
}

$json = if ($Body) { $Body | ConvertTo-Json -Compress -Depth 10 } else { '' }

$sha = [System.Security.Cryptography.SHA256]::Create()
$bodyHash = [BitConverter]::ToString(
    $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($json))).Replace('-', '')

$timestamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$nonce = [Guid]::NewGuid().ToString('N')

$canonical = ($Method.ToUpperInvariant(), $Path, $timestamp, $nonce, $bodyHash) -join "`n"

$hmac = [System.Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($Secret))
$signature = [BitConverter]::ToString(
    $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($canonical))).Replace('-', '')

$headers = @{
    'X-YoPay-Key'       = $KeyId
    'X-YoPay-Timestamp' = $timestamp
    'X-YoPay-Nonce'     = $nonce
    'X-YoPay-Signature' = $signature
}

try {
    Invoke-RestMethod -Uri "$BaseUrl$Path" -Method $Method -Headers $headers `
        -ContentType 'application/json' -Body $json
}
catch {
    # The server answers failures as problem+json; showing the body is the whole point.
    $response = $_.Exception.Response
    if ($response) {
        $reader = [IO.StreamReader]::new($response.GetResponseStream())
        Write-Host "HTTP $([int]$response.StatusCode)" -ForegroundColor Red
        Write-Host $reader.ReadToEnd()
    }
    else {
        throw
    }
}
