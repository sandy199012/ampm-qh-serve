param([switch]$Silent)
$ErrorActionPreference = 'SilentlyContinue'

# AMPM IT Tool - PC Inventory Agent
# Collects this PC's hostname/IP/OS/CPU/RAM/free-disk/installed-software and
# pushes it to the live AMPM IT Tool website's PC Inventory (Endpoints tab).
# Run manually (double-click AMPM_PC_Agent.bat) for a one-time report, or run
# AMPM_PC_Agent_Setup.bat once (as Administrator) to have this run itself
# automatically at every restart and every 10 minutes after that.

$serverUrl = 'https://ampm-qh-serve-1.onrender.com/api/endpoints/report-pc'
$agentKey  = 'AMPM-AGENT-2026'
$logFile   = Join-Path $PSScriptRoot 'ampm_agent_log.txt'

function Log($msg) {
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $msg"
    Add-Content -Path $logFile -Value $line
    if (-not $Silent) { Write-Host $msg }
}

$ip = (Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object { $_.IPAddress -notlike '169.254.*' -and $_.IPAddress -ne '127.0.0.1' } |
    Select-Object -First 1 -ExpandProperty IPAddress)

$os  = (Get-CimInstance Win32_OperatingSystem).Caption
$cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name
$ramGb = [math]::Round(((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory) / 1GB, 1)
$diskFree = [math]::Round(((Get-PSDrive C).Free) / 1GB, 1)

# Installed software - read straight from the Windows Uninstall registry keys
# (fast and complete; unlike Win32_Product this never triggers MSI repairs).
$software = @()
$regPaths = @(
    'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*'
)
foreach ($p in $regPaths) {
    Get-ItemProperty $p -ErrorAction SilentlyContinue | ForEach-Object {
        if ($_.DisplayName -and $_.SystemComponent -ne 1) {
            $software += [PSCustomObject]@{ name = $_.DisplayName; version = "$($_.DisplayVersion)" }
        }
    }
}
$software = $software | Sort-Object name -Unique

$payload = @{
    key      = $agentKey
    hostname = $env:COMPUTERNAME
    ip       = $ip
    os       = $os
    cpu      = $cpu
    ramGb    = "$ramGb"
    diskFree = "$diskFree"
    user     = $env:USERNAME
    software = $software
} | ConvertTo-Json -Depth 4

if (-not $Silent) {
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
    Write-Host " Software : $($software.Count) programs found"
    Write-Host ""
}

try {
    $resp = Invoke-RestMethod -Uri $serverUrl -Method Post -Body $payload -ContentType 'application/json' -TimeoutSec 25
    if ($resp.ok -eq $true) {
        Log "OK - sent ($($software.Count) software entries)"
        if (-not $Silent) { Write-Host " DONE - website ke PC Inventory mein bhej diya gaya." -ForegroundColor Green }
    } else {
        Log "Server rejected: $($resp | ConvertTo-Json -Compress)"
        if (-not $Silent) { Write-Host " Server ne mana kar diya - upar wala message dekho." -ForegroundColor Yellow }
    }
} catch {
    Log "FAILED - $_"
    if (-not $Silent) { Write-Host " FAILED - internet check karo. Error: $_" -ForegroundColor Red }
}

if (-not $Silent) {
    Write-Host ""
    Write-Host "Log file: $logFile"
    Read-Host "Enter dabao band karne ke liye"
}
