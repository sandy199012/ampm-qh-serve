$ErrorActionPreference = 'SilentlyContinue'

# AMPM IT Tool - PC Inventory Agent
# Collects this PC's own hostname/IP/OS/CPU/RAM/free-disk and pushes it to
# the live AMPM IT Tool website's PC Inventory (Monitor / Endpoints tab).
# Run once on each office PC - no manual typing or CSV import needed.

$serverUrl = 'https://ampm-qh-serve-1.onrender.com/api/endpoints/report-pc'
$agentKey  = 'AMPM-AGENT-2026'

$ip = (Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object { $_.IPAddress -notlike '169.254.*' -and $_.IPAddress -ne '127.0.0.1' } |
    Select-Object -First 1 -ExpandProperty IPAddress)

$os  = (Get-CimInstance Win32_OperatingSystem).Caption
$cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name
$ramGb = [math]::Round(((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory) / 1GB, 1)
$diskFree = [math]::Round(((Get-PSDrive C).Free) / 1GB, 1)

$payload = @{
    key      = $agentKey
    hostname = $env:COMPUTERNAME
    ip       = $ip
    os       = $os
    cpu      = $cpu
    ramGb    = "$ramGb"
    diskFree = "$diskFree"
    user     = $env:USERNAME
} | ConvertTo-Json

Write-Host ""
Write-Host " AMPM PC Inventory Agent" -ForegroundColor Cyan
Write-Host " ------------------------"
Write-Host " Hostname : $env:COMPUTERNAME"
Write-Host " IP       : $ip"
Write-Host " OS       : $os"
Write-Host " CPU      : $cpu"
Write-Host " RAM      : $ramGb GB"
Write-Host " Disk Free: $diskFree GB"
Write-Host " User     : $env:USERNAME"
Write-Host ""

try {
    $resp = Invoke-RestMethod -Uri $serverUrl -Method Post -Body $payload -ContentType 'application/json' -TimeoutSec 20
    if ($resp.ok -eq $true) {
        Write-Host " DONE - website ke PC Inventory mein bhej diya gaya." -ForegroundColor Green
    } else {
        Write-Host " Server ne mana kar diya: $($resp | ConvertTo-Json -Compress)" -ForegroundColor Yellow
    }
} catch {
    Write-Host " FAILED - internet check karo ya IT ko bhejo ye error:" -ForegroundColor Red
    Write-Host " $_"
}

Write-Host ""
Write-Host "Ye window band mat karo agar error dikha ho - IT ko screenshot bhejo."
Read-Host "Enter dabao band karne ke liye"
