param([switch]$Silent)
$ErrorActionPreference = 'SilentlyContinue'

# AMPM IT Tool - PC Inventory Agent
# Collects this PC's hostname/IP/MAC/OS/hardware/installed-software and
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

# Pick the IP of whichever adapter actually carries internet traffic
# (the default-route adapter), instead of just the first adapter found -
# that avoids picking up VPN/VMware/VirtualBox/Hyper-V/Docker virtual
# adapters that many PCs have alongside the real LAN/WiFi connection.
$ip = $null
$ifIndexForMac = $null
try {
    $route = Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue |
        Sort-Object -Property RouteMetric |
        Select-Object -First 1
    if ($route) {
        $ifIndexForMac = $route.InterfaceIndex
        $ip = Get-NetIPAddress -InterfaceIndex $route.InterfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
            Where-Object { $_.IPAddress -notlike '169.254.*' } |
            Select-Object -First 1 -ExpandProperty IPAddress
    }
} catch {}

if (-not $ip) {
    # Fallback: first IP on any adapter that is actually "Up", skipping
    # link-local/loopback addresses.
    $upAdapter = Get-NetAdapter -ErrorAction SilentlyContinue |
        Where-Object { $_.Status -eq 'Up' } |
        Sort-Object -Property ifIndex |
        Select-Object -First 1
    if ($upAdapter) {
        $ifIndexForMac = $upAdapter.ifIndex
        $ip = Get-NetIPAddress -InterfaceIndex $upAdapter.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
            Where-Object { $_.IPAddress -notlike '169.254.*' -and $_.IPAddress -ne '127.0.0.1' } |
            Select-Object -First 1 -ExpandProperty IPAddress
    }
}

if (-not $ip) { $ip = 'Unknown' }

# MAC of the same adapter the IP came from (falls back to the first "Up" adapter).
$mac = $null
try {
    if ($ifIndexForMac) {
        $mac = Get-NetAdapter -InterfaceIndex $ifIndexForMac -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty MacAddress
    }
    if (-not $mac) {
        $mac = Get-NetAdapter -ErrorAction SilentlyContinue | Where-Object { $_.Status -eq 'Up' } | Select-Object -First 1 -ExpandProperty MacAddress
    }
} catch {}

$winOS   = Get-CimInstance Win32_OperatingSystem
$os      = $winOS.Caption
$osBuild = "$($winOS.Version) (Build $($winOS.BuildNumber))"
$arch    = $winOS.OSArchitecture

$cs           = Get-CimInstance Win32_ComputerSystem
$manufacturer = $cs.Manufacturer
$model        = $cs.Model

# Serial number - Win32_BIOS is usually right, but on some laptops (mainly
# cheaper/whitebox/refurbished ones) the BIOS serial is left blank or set to a
# generic OEM placeholder ("To Be Filled By O.E.M.", "System Serial Number",
# "Default string", "None", etc). Win32_ComputerSystemProduct.IdentifyingNumber
# often has the real one in that case, so fall back to it.
$serial = (Get-CimInstance Win32_BIOS | Select-Object -First 1).SerialNumber
$placeholders = @('To Be Filled By O.E.M.', 'System Serial Number', 'Default string', 'None', 'Not Specified', 'Not Applicable', '0', '')
if ([string]::IsNullOrWhiteSpace($serial) -or ($placeholders -contains $serial.Trim())) {
    $altSerial = (Get-CimInstance Win32_ComputerSystemProduct | Select-Object -First 1).IdentifyingNumber
    if (-not [string]::IsNullOrWhiteSpace($altSerial) -and -not ($placeholders -contains $altSerial.Trim())) {
        $serial = $altSerial
    }
}
if ([string]::IsNullOrWhiteSpace($serial)) { $serial = 'Not available' }

$cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name
$ramGb = [math]::Round(($cs.TotalPhysicalMemory) / 1GB, 1)
$diskFree  = [math]::Round(((Get-PSDrive C).Free) / 1GB, 1)
$diskTotal = [math]::Round((((Get-PSDrive C).Free + (Get-PSDrive C).Used)) / 1GB, 1)

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

# USB printers plugged into this PC - network printers are found by the separate
# AMPM_Printer_Scanner, but a USB printer has no IP so only the PC it is attached
# to can report it. Virtual printers (PDF/XPS/OneNote/Fax) are skipped. Printers
# that Windows shows as offline (switched off / unplugged) are still reported,
# with offline=true, so the website can show their status.
$usbPrinters = @()
$printersSeen = @()
try {
    $virtual = 'PDF|XPS|OneNote|Fax|Snagit|Send To|Microsoft Print'
    foreach ($pr in @(Get-CimInstance Win32_Printer -ErrorAction SilentlyContinue)) {
        $off = [bool]$pr.WorkOffline
        $printersSeen += ("{0} [{1}]{2}" -f $pr.Name, $pr.PortName, $(if ($off) { ' offline' } else { '' }))
        if ($pr.PortName -match '^(USB|DOT4|BTH|EPUSB|BRUSB)' -and $pr.Name -notmatch $virtual) {
            $usbPrinters += [PSCustomObject]@{ name = "$($pr.Name)"; driver = "$($pr.DriverName)"; port = "$($pr.PortName)"; offline = $off; serial = '' }
        }
    }
} catch {}

# Best-effort USB serial number: Windows only exposes it when the printer
# reports one in its USB device id (USB\VID_xxxx&PID_xxxx\SERIAL). Used only
# when there is exactly ONE USB printer and exactly ONE such candidate, so it
# can never be attached to the wrong printer.
try {
    if ($usbPrinters.Count -eq 1) {
        $cands = @()
        foreach ($d in @(Get-CimInstance Win32_PnPEntity -ErrorAction SilentlyContinue)) {
            if ($d.DeviceID -match '^USB\\VID_[0-9A-Fa-f]{4}&PID_[0-9A-Fa-f]{4}\\([^&\\]{4,})$') {
                $sn = $Matches[1]
                if ($d.Name -match 'Printing|Printer|Composite' -or $d.Service -eq 'usbprint') { $cands += $sn }
            }
        }
        $cands = @($cands | Sort-Object -Unique)
        if ($cands.Count -eq 1) { $usbPrinters[0].serial = $cands[0] }
    }
} catch {}
Log "Printers seen by Windows: $(if ($printersSeen.Count) { $printersSeen -join '; ' } else { 'none' }) | USB: $($usbPrinters.Count)"

$payload = @{
    key          = $agentKey
    hostname     = $env:COMPUTERNAME
    ip           = $ip
    mac          = $mac
    os           = $os
    osBuild      = $osBuild
    arch         = $arch
    manufacturer = $manufacturer
    model        = $model
    serial       = $serial
    cpu          = $cpu
    ramGb        = "$ramGb"
    diskFree     = "$diskFree"
    diskTotal    = "$diskTotal"
    user         = $env:USERNAME
    software     = $software
    usbPrinters  = @($usbPrinters)
} | ConvertTo-Json -Depth 4

if (-not $Silent) {
    Write-Host ""
    Write-Host " AMPM PC Inventory Agent" -ForegroundColor Cyan
    Write-Host " ------------------------"
    Write-Host " Hostname     : $env:COMPUTERNAME"
    Write-Host " IP           : $ip"
    Write-Host " MAC          : $mac"
    Write-Host " OS           : $os"
    Write-Host " OS Build     : $osBuild"
    Write-Host " Architecture : $arch"
    Write-Host " Manufacturer : $manufacturer"
    Write-Host " Model        : $model"
    Write-Host " Serial No.   : $serial"
    Write-Host " CPU          : $cpu"
    Write-Host " RAM          : $ramGb GB"
    Write-Host " Disk Free    : $diskFree GB"
    Write-Host " Disk Total   : $diskTotal GB"
    Write-Host " User         : $env:USERNAME"
    Write-Host " Software     : $($software.Count) programs found"
    Write-Host " USB Printers : $($usbPrinters.Count) found$(if ($usbPrinters.Count) { ' (' + (($usbPrinters | ForEach-Object { $_.name }) -join ', ') + ')' })"
    Write-Host " All printers : $(if ($printersSeen.Count) { $printersSeen -join '; ' } else { 'none' })"
    Write-Host ""
}

try {
    $resp = Invoke-RestMethod -Uri $serverUrl -Method Post -Body $payload -ContentType 'application/json' -TimeoutSec 25
    if ($resp.ok -eq $true) {
        Log "OK - sent (serial=$serial, $($software.Count) software entries, $($usbPrinters.Count) USB printer(s))"
        if (-not $Silent) { Write-Host " DONE - sent to the website's PC Inventory." -ForegroundColor Green }
    } else {
        Log "Server rejected: $($resp | ConvertTo-Json -Compress)"
        if (-not $Silent) { Write-Host " Server rejected the request - see the message above." -ForegroundColor Yellow }
    }
} catch {
    Log "FAILED - $_"
    if (-not $Silent) { Write-Host " FAILED - check your internet connection. Error: $_" -ForegroundColor Red }
}

if (-not $Silent) {
    Write-Host ""
    Write-Host "Log file: $logFile"
    Read-Host "Press Enter to close"
}
