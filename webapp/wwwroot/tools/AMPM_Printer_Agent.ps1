param(
    [switch]$Silent,
    [switch]$Install
)
$ErrorActionPreference = 'SilentlyContinue'

# ==========================================================================
# AMPM IT Tool - Printer Agent
#
# Finds every network printer (laser/inkjet/MFP/label printers with an IP) on
# this PC's LAN (and any extra subnets listed below) and pushes them to the AMPM
# IT Tool website -> Asset Stock.
#   * New printer   -> added with the next tag PRN-0001, PRN-0002 ...
#   * Known printer -> only IP / MAC / hostname / page count / last-seen are
#                      refreshed - assignment, location and anything you typed
#                      by hand is never touched.
# USB printers are NOT found by this agent - the PC Agent on the PC they are
# plugged into reports them.
# How: TCP probe of ports 9100/515/631, then SNMP (model / serial / page count);
# MAC from this PC's ARP table.
#
# This file is downloaded from the website (Asset Stock -> Agents). The access
# key is put into it at download time, so download it again whenever the key
# changes or a new version is published.
#
# Usage:
#   AMPM_Printer_Agent.bat          -> scan now (window stays open)
#   AMPM_Printer_Agent_Setup.bat    -> (run as Administrator) scan every day at 11:00
#   -Silent                         -> no console output (used by the scheduled task)
# ==========================================================================
$serverBase = 'https://ampm-qh-serve-1.onrender.com'
$serverUrl  = "$serverBase/api/endpoints/report-printers"
$agentKey  = '__AMPM_AGENT_KEY__'   # filled in automatically when you download this from the website

# SNMP community used to read device info. 'public' is the factory default; if you
# set a private community on your switches/printers, put the same word here.
$snmpCommunity = 'public'

if ($agentKey -like '__AMPM*') {
    Write-Host ''
    Write-Host ' This copy has no access key.' -ForegroundColor Yellow
    Write-Host ' Please download it again from the website:' -ForegroundColor Yellow
    Write-Host '   Asset Stock -> Agents -> Download' -ForegroundColor Yellow
    if (-not $Silent) { Read-Host 'Press Enter to close' }
    exit 1
}

# Extra subnets to scan besides this PC's own network, written as the first
# three numbers, e.g.  $extraSubnets = @('192.168.2', '10.0.5')
$extraSubnets = @()

$ports      = @(9100, 515, 631)
$tcpWaitMs  = 3000
$maxSubnets = 8
$logFile    = Join-Path $PSScriptRoot 'ampm_printer_log.txt'

function Log($msg) {
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $msg"
    Add-Content -Path $logFile -Value $line
    if (-not $Silent) { Write-Host $msg }
}

try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch {}

# ---------------------------------------------------------------- install --
if ($Install) {
    $taskName = 'AMPM Printer Agent'
    $destDir  = Join-Path $env:ProgramData 'AMPM\PrinterAgent'
    $me = $PSCommandPath
    if (-not $me) { $me = $MyInvocation.MyCommand.Path }
    try {
        # The task runs a copy kept in ProgramData, so it keeps working even if the
        # download folder is deleted. Running Setup again after a new download
        # simply replaces that copy.
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        $dest = Join-Path $destDir (Split-Path $me -Leaf)
        if ($me -ne $dest) { Copy-Item -LiteralPath $me -Destination $dest -Force -ErrorAction Stop }
        $action   = New-ScheduledTaskAction -Execute 'powershell.exe' `
            -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$dest`" -Silent"
        $trigger  = New-ScheduledTaskTrigger -Daily -At '11:00'

        $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
            -StartWhenAvailable -MultipleInstances IgnoreNew
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName 'AMPM Network Printer Scanner' -Confirm:$false -ErrorAction SilentlyContinue
        Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger `
            -Principal $principal -Settings $settings `
            -Description 'Scans the office network for printers and reports them to the AMPM IT Tool (Asset Stock) every day.' `
            -ErrorAction Stop | Out-Null
        Write-Host ''
        Write-Host " DONE - '$taskName' installed (scans every day at 11:00)." -ForegroundColor Green
        Write-Host " Installed copy : $dest"
        Write-Host ' Running it once now...'
        Write-Host ''
    } catch {
        Write-Host " INSTALL FAILED: $_" -ForegroundColor Red
        Write-Host ' Right-click AMPM_Printer_Agent_Setup.bat and choose "Run as administrator".'
        Read-Host 'Press Enter to close'
        exit 1
    }
}

# ------------------------------------------------------------ IP helpers ---
function IpToLong([string]$ip) {
    $o = $ip.Split('.')
    return ([long]$o[0] * 16777216) + ([long]$o[1] * 65536) + ([long]$o[2] * 256) + [long]$o[3]
}
function LongToIp([long]$n) {
    $a = [math]::Floor($n / 16777216) % 256
    $b = [math]::Floor($n / 65536) % 256
    $c = [math]::Floor($n / 256) % 256
    $d = $n % 256
    return "$a.$b.$c.$d"
}

# Own IPv4 address + prefix length of the adapter that carries the default route
$ownIp = $null
$ownPrefix = 24
try {
    $route = Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction Stop | Sort-Object RouteMetric | Select-Object -First 1
    if ($route) {
        $addr = Get-NetIPAddress -InterfaceIndex $route.InterfaceIndex -AddressFamily IPv4 -ErrorAction Stop |
                Where-Object { $_.IPAddress -notlike '169.254.*' } | Select-Object -First 1
        if ($addr) { $ownIp = $addr.IPAddress; $ownPrefix = [int]$addr.PrefixLength }
    }
} catch {}
if (-not $ownIp) {
    # Fallback for old systems without the NetTCPIP module
    $nic = Get-WmiObject Win32_NetworkAdapterConfiguration -Filter 'IPEnabled=True' |
           Where-Object { $_.DefaultIPGateway } | Select-Object -First 1
    if ($nic) {
        $ownIp = ($nic.IPAddress | Where-Object { $_ -match '^\d+\.\d+\.\d+\.\d+$' } | Select-Object -First 1)
        $mask = ($nic.IPSubnet | Where-Object { $_ -match '^\d+\.\d+\.\d+\.\d+$' } | Select-Object -First 1)
        if ($mask) {
            $m = IpToLong $mask
            $bits = 0
            for ($i = 31; $i -ge 0; $i--) { if ([math]::Floor($m / [math]::Pow(2, $i)) % 2 -eq 1) { $bits++ } else { break } }
            $ownPrefix = $bits
        }
    }
}

# Build the list of /24 prefixes ("192.168.1") to scan
$prefixes = New-Object System.Collections.ArrayList
if ($ownIp) {
    $block = [long][math]::Pow(2, 32 - $ownPrefix)
    if ($ownPrefix -ge 22 -and $ownPrefix -le 24) {
        $net = [long]([math]::Floor((IpToLong $ownIp) / $block) * $block)
        for ($i = 0; $i -lt [int]($block / 256); $i++) {
            $p = (LongToIp ($net + 256 * $i)).Split('.')
            [void]$prefixes.Add("$($p[0]).$($p[1]).$($p[2])")
        }
    } else {
        $p = $ownIp.Split('.')
        [void]$prefixes.Add("$($p[0]).$($p[1]).$($p[2])")
    }
}
foreach ($x in $extraSubnets) {
    $x = "$x".Trim().TrimEnd('.')
    if ($x -match '^\d+\.\d+\.\d+$' -and -not $prefixes.Contains($x)) { [void]$prefixes.Add($x) }
}
if ($prefixes.Count -gt $maxSubnets) { $prefixes.RemoveRange($maxSubnets, $prefixes.Count - $maxSubnets) }

if ($prefixes.Count -eq 0) {
    Log 'FAILED - could not work out this PC''s network (no active network adapter?).'
    if (-not $Silent) { Read-Host 'Press Enter to close' }
    exit 1
}

if (-not $Silent) {
    Write-Host ''
    Write-Host ' AMPM - Printer Agent' -ForegroundColor Cyan
    Write-Host " This PC     : $env:COMPUTERNAME ($ownIp)"
    Write-Host " Scanning    : $($prefixes -join '.x, ').x"
    Write-Host ' Please wait...'
    Write-Host ''
}
Log "Scan started from $env:COMPUTERNAME ($ownIp) - subnets: $($prefixes -join ', ')"

# ------------------------------------------------------------- TCP probe ---
# Returns hashtable  ip -> ArrayList of open ports
function Scan-Subnet([string]$prefix) {
    $open = @{}
    # Hosts are probed in chunks of 50 so that no more than ~500 sockets are
    # half-open at the same time.
    for ($start = 1; $start -le 254; $start += 50) {
        $end = [math]::Min($start + 49, 254)
        $jobs = New-Object System.Collections.ArrayList
        for ($h = $start; $h -le $end; $h++) {
            $ip = "$prefix.$h"
            foreach ($port in $ports) {
                $c = New-Object System.Net.Sockets.TcpClient
                try {
                    $ar = $c.BeginConnect($ip, $port, $null, $null)
                    [void]$jobs.Add([pscustomobject]@{ Ip = $ip; Port = $port; Client = $c; Ar = $ar })
                } catch { try { $c.Close() } catch {} }
            }
        }
        $deadline = (Get-Date).AddMilliseconds($tcpWaitMs)
        while ((Get-Date) -lt $deadline) {
            $pending = 0
            foreach ($j in $jobs) { if (-not $j.Ar.IsCompleted) { $pending++ } }
            if ($pending -eq 0) { break }
            Start-Sleep -Milliseconds 100
        }
        foreach ($j in $jobs) {
            $ok = $false
            if ($j.Ar.IsCompleted) {
                try { $j.Client.EndConnect($j.Ar); $ok = $j.Client.Connected } catch { $ok = $false }
            }
            if ($ok) {
                if (-not $open.ContainsKey($j.Ip)) { $open[$j.Ip] = New-Object System.Collections.ArrayList }
                [void]$open[$j.Ip].Add($j.Port)
            }
            try { $j.Client.Close() } catch {}
        }
    }
    return $open
}

# ------------------------------------------------------------------ SNMP ---
function Join-Bytes {
    $l = New-Object 'System.Collections.Generic.List[byte]'
    foreach ($a in $args) { $l.AddRange([byte[]]$a) }
    return ,($l.ToArray())
}
function New-Tlv([int]$tag, [byte[]]$val) {
    $l = New-Object 'System.Collections.Generic.List[byte]'
    $l.Add([byte]$tag)
    $n = $val.Length
    if ($n -lt 128) { $l.Add([byte]$n) }
    elseif ($n -lt 256) { $l.Add([byte]0x81); $l.Add([byte]$n) }
    else { $l.Add([byte]0x82); $l.Add([byte]([math]::Floor($n / 256))); $l.Add([byte]($n % 256)) }
    $l.AddRange($val)
    return ,($l.ToArray())
}
function Encode-Oid([string]$oid) {
    $p = @($oid.Split('.') | ForEach-Object { [int]$_ })
    $l = New-Object 'System.Collections.Generic.List[byte]'
    $l.Add([byte](40 * $p[0] + $p[1]))
    for ($i = 2; $i -lt $p.Count; $i++) {
        $v = $p[$i]
        if ($v -lt 128) { $l.Add([byte]$v) }
        else {
            $tmp = New-Object 'System.Collections.Generic.List[byte]'
            $tmp.Add([byte]($v % 128))
            $v = [math]::Floor($v / 128)
            while ($v -gt 0) { $tmp.Add([byte](($v % 128) + 128)); $v = [math]::Floor($v / 128) }
            $arr = $tmp.ToArray(); [Array]::Reverse($arr)
            $l.AddRange([byte[]]$arr)
        }
    }
    return ,($l.ToArray())
}
function Build-SnmpGet([string]$community, [string]$oid) {
    $empty   = [byte[]]@()
    $varbind = New-Tlv 0x30 (Join-Bytes (New-Tlv 0x06 (Encode-Oid $oid)) (New-Tlv 0x05 $empty))
    $vblist  = New-Tlv 0x30 $varbind
    $pdu     = New-Tlv 0xA0 (Join-Bytes (New-Tlv 0x02 ([byte[]]@(0, 0, 0x12, 0x34))) (New-Tlv 0x02 ([byte[]]@(0))) (New-Tlv 0x02 ([byte[]]@(0))) $vblist)
    $msg     = New-Tlv 0x30 (Join-Bytes (New-Tlv 0x02 ([byte[]]@(0))) (New-Tlv 0x04 ([System.Text.Encoding]::ASCII.GetBytes($community))) $pdu)
    return ,$msg
}
function Read-Tlv([byte[]]$b, [int]$pos) {
    $tag = [int]$b[$pos]; $pos++
    $len = [int]$b[$pos]; $pos++
    if ($len -ge 128) {
        $nb = $len - 128; $len = 0
        for ($i = 0; $i -lt $nb; $i++) { $len = ($len * 256) + [int]$b[$pos]; $pos++ }
    }
    return @{ Tag = $tag; Start = $pos; Len = $len; Next = ($pos + $len) }
}
function Parse-SnmpValue([byte[]]$b) {
    try {
        $m = Read-Tlv $b 0
        $p = $m.Start
        $t = Read-Tlv $b $p; $p = $t.Next              # version
        $t = Read-Tlv $b $p; $p = $t.Next              # community
        $pdu = Read-Tlv $b $p
        if ($pdu.Tag -ne 0xA2) { return $null }        # not a GetResponse
        $p = $pdu.Start
        $t = Read-Tlv $b $p; $p = $t.Next              # request-id
        $es = Read-Tlv $b $p; $p = $es.Next            # error-status
        if ([int]$b[$es.Start] -ne 0) { return $null } # noSuchName etc.
        $t = Read-Tlv $b $p; $p = $t.Next              # error-index
        $vbl = Read-Tlv $b $p
        $vb  = Read-Tlv $b $vbl.Start
        $oidT = Read-Tlv $b $vb.Start
        $val = Read-Tlv $b $oidT.Next
        if ($val.Tag -eq 0x04) {
            $s = [System.Text.Encoding]::UTF8.GetString($b, $val.Start, $val.Len)
            return ($s -replace '[^\x20-\x7E]', '').Trim()
        }
        if ($val.Tag -eq 0x02 -or $val.Tag -eq 0x41 -or $val.Tag -eq 0x42 -or $val.Tag -eq 0x43) {
            [long]$n = 0
            for ($i = 0; $i -lt $val.Len; $i++) { $n = ($n * 256) + [int]$b[$val.Start + $i] }
            return "$n"
        }
        return $null
    } catch { return $null }
}
function Get-Snmp([string]$ip, [string]$oid, [int]$timeoutMs = 700) {
    $udp = New-Object System.Net.Sockets.UdpClient
    try {
        $udp.Client.ReceiveTimeout = $timeoutMs
        $udp.Client.SendTimeout = $timeoutMs
        $udp.Connect($ip, 161)
        $pkt = Build-SnmpGet $snmpCommunity $oid
        [void]$udp.Send($pkt, $pkt.Length)
        $ep = New-Object System.Net.IPEndPoint -ArgumentList ([System.Net.IPAddress]::Any), 0
        $resp = $udp.Receive([ref]$ep)
        return (Parse-SnmpValue $resp)
    } catch { return $null }
    finally { try { $udp.Close() } catch {} }
}

$brands = @(
    @{ Key = 'hewlett'; Name = 'HP' }, @{ Key = 'hp '; Name = 'HP' }, @{ Key = 'laserjet'; Name = 'HP' },
    @{ Key = 'brother'; Name = 'Brother' }, @{ Key = 'canon'; Name = 'Canon' }, @{ Key = 'epson'; Name = 'Epson' },
    @{ Key = 'samsung'; Name = 'Samsung' }, @{ Key = 'xerox'; Name = 'Xerox' }, @{ Key = 'ricoh'; Name = 'Ricoh' },
    @{ Key = 'kyocera'; Name = 'Kyocera' }, @{ Key = 'lexmark'; Name = 'Lexmark' }, @{ Key = 'konica'; Name = 'Konica Minolta' },
    @{ Key = 'minolta'; Name = 'Konica Minolta' }, @{ Key = 'sharp'; Name = 'Sharp' }, @{ Key = 'oki'; Name = 'OKI' },
    @{ Key = 'zebra'; Name = 'Zebra' }, @{ Key = 'tsc '; Name = 'TSC' }, @{ Key = 'pantum'; Name = 'Pantum' },
    @{ Key = 'toshiba'; Name = 'Toshiba' }, @{ Key = 'panasonic'; Name = 'Panasonic' }, @{ Key = 'dell'; Name = 'Dell' },
    @{ Key = 'fuji'; Name = 'Fuji Xerox' }
)
function Find-Brand([string]$text) {
    $t = ($text + ' ').ToLower()
    foreach ($b in $brands) { if ($t.Contains($b.Key)) { return $b.Name } }
    return ''
}

# ----------------------------------------------------- scan + collect ------
$allOpen = @{}
foreach ($pf in $prefixes) {
    if (-not $Silent) { Write-Host " Probing $pf.1 - $pf.254 ..." }
    $r = Scan-Subnet $pf
    foreach ($k in $r.Keys) { $allOpen[$k] = $r[$k] }
}

# ARP table (filled by the probes above): ip -> MAC
$arp = @{}
try {
    Get-NetNeighbor -AddressFamily IPv4 -ErrorAction Stop | ForEach-Object {
        if ($_.LinkLayerAddress -and $_.LinkLayerAddress -ne '00-00-00-00-00-00' -and $_.LinkLayerAddress -ne 'FF-FF-FF-FF-FF-FF') {
            $arp[$_.IPAddress] = ($_.LinkLayerAddress -replace '-', ':').ToUpper()
        }
    }
} catch {}
if ($arp.Count -eq 0) {
    foreach ($line in (arp -a)) {
        if ($line -match '^\s*(\d+\.\d+\.\d+\.\d+)\s+([0-9a-fA-F]{2}[-:][0-9a-fA-F]{2}[-:][0-9a-fA-F]{2}[-:][0-9a-fA-F]{2}[-:][0-9a-fA-F]{2}[-:][0-9a-fA-F]{2})') {
            $arp[$Matches[1]] = ($Matches[2] -replace '-', ':').ToUpper()
        }
    }
}

$found = New-Object System.Collections.ArrayList
$candidates = $allOpen.Keys | Sort-Object { IpToLong $_ }
foreach ($ip in $candidates) {
    if ($ip -eq $ownIp) { continue }
    $open = @($allOpen[$ip])
    $isRaw = ($open -contains 9100) -or ($open -contains 515)

    $sysDescr = Get-Snmp $ip '1.3.6.1.2.1.1.1.0'
    $sysName = ''; $hrDescr = ''; $serial = ''; $pages = ''
    if ($sysDescr) {
        $sysName = Get-Snmp $ip '1.3.6.1.2.1.1.5.0'
        $hrDescr = Get-Snmp $ip '1.3.6.1.2.1.25.3.2.1.3.1'
        $serial  = Get-Snmp $ip '1.3.6.1.2.1.43.5.1.1.17.1'
        $pages   = Get-Snmp $ip '1.3.6.1.2.1.43.10.2.1.4.1.1'
    }

    # 631 (IPP) alone is not enough - PCs and NAS boxes also use it. Require
    # printer-MIB answers in that case.
    $looksPrinter = $isRaw -or ($serial) -or ($hrDescr -and $pages)
    if (-not $looksPrinter) {
        Log ("  host {0} ports={1} snmp={2} -> not a printer (skipped)" -f $ip, ($open -join ','), $(if ($sysDescr) { 'yes' } else { 'no' }))
        continue
    }

        $text = "$hrDescr $sysDescr"
        $brand = Find-Brand $text
        $model = $hrDescr
        if (-not $model -and $sysDescr -match 'PID:([^,;]+)') { $model = $Matches[1].Trim() }
        if (-not $model -and $sysDescr) { $model = $sysDescr; if ($model.Length -gt 60) { $model = $model.Substring(0, 60) } }
        if ($brand -and $model -and $model.ToLower().StartsWith($brand.ToLower() + ' ')) { $model = $model.Substring($brand.Length + 1).Trim() }

        $hostName = $sysName
        if (-not $hostName) {
            try { $hostName = ([System.Net.Dns]::GetHostEntry($ip)).HostName } catch { $hostName = '' }
        }

        $item = [ordered]@{
            ip         = $ip
            mac        = [string]$arp[$ip]
            hostname   = [string]$hostName
            brand      = [string]$brand
            model      = [string]$model
            serial     = [string]$serial
            pageCount  = [string]$pages
            ports      = ($open -join ',')
        }
        [void]$found.Add($item)
        Log ("  host {0} ports={1} snmp={2} -> printer: {3} {4} SN:{5} pages:{6}" -f $ip, ($open -join ','), $(if ($sysDescr) { 'yes' } else { 'no' }), $brand, $model, $serial, $pages)
}

Log "Scan finished - $($found.Count) printer(s) found"

if (-not $Silent) {
    Write-Host ''
    if ($found.Count -eq 0) {
        Write-Host ' No printers found. Make sure this PC is on the same network as the printers.' -ForegroundColor Yellow
    } else {
        Write-Host " Found $($found.Count) network printer(s):" -ForegroundColor Green
        foreach ($f in $found) {
            Write-Host ('   {0,-15} {1,-17} {2} {3}  {4}' -f $f.ip, $f.mac, $f.brand, $f.model, $(if ($f.serial) { "SN:$($f.serial)" } else { '' }))
        }
    }
    Write-Host ''
}

# ------------------------------------------------------------------ send ---
$body = [ordered]@{
    key         = $agentKey
    scannedFrom = $env:COMPUTERNAME
    subnets     = ($prefixes -join ', ')
    printers    = @($found)
}
$json = ConvertTo-Json -InputObject $body -Depth 5
$bytes = [System.Text.Encoding]::UTF8.GetBytes($json)

try {
    $resp = Invoke-RestMethod -Uri $serverUrl -Method Post -Body $bytes -ContentType 'application/json; charset=utf-8' -TimeoutSec 90
    if ($resp.ok -eq $true) {
        Log "OK - sent to website: $($resp.added) new, $($resp.updated) updated"
        if (-not $Silent) { Write-Host " DONE - website updated: $($resp.added) new printer(s), $($resp.updated) refreshed." -ForegroundColor Green
                            Write-Host ' Open Asset Stock in the IT Tool to assign each printer to an employee.' }
    } else {
        Log "Server rejected: $($resp | ConvertTo-Json -Compress)"
        if (-not $Silent) { Write-Host ' Server rejected the request (wrong or old key? download this agent again from the website).' -ForegroundColor Yellow }
    }
} catch {
    Log "FAILED to send - $_"
    if (-not $Silent) { Write-Host " FAILED to send to the website - check internet / key. Error: $_" -ForegroundColor Red }
}

if (-not $Silent) {
    Write-Host ''
    Write-Host "Log file: $logFile"
    Read-Host 'Press Enter to close'
}
