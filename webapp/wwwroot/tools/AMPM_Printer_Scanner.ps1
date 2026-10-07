param(
    [switch]$Silent,
    [switch]$Install
)
$ErrorActionPreference = 'SilentlyContinue'

# ==========================================================================
# AMPM IT Tool - Network Printer + Network Device Scanner
#
# Finds every network printer AND every network device (switches, WiFi
# access points / routers, NVR / DVR recorders, IP cameras) on this PC's LAN
# (and any extra subnets listed below) and pushes them to the AMPM IT Tool
# website -> Asset Stock (printers in the main list, the rest in the
# "Network Devices" tab).
#   * New device   -> added with the next tag: PRN-0001 (printer), SW-0001
#                     (switch), WIFI-0001, NVR-0001, CAM-0001 ...
#   * Known device -> only its IP / MAC / hostname / last-seen (and page count
#                     for printers) are refreshed - assignment, location and
#                     anything you typed by hand is never touched.
#
# How it finds devices:
#   1. TCP probe of every address in the subnet: printer ports 9100/515/631,
#      camera/NVR ports 554 (RTSP) / 8000 / 37777, management ports 80/443/22/23.
#   2. SNMP (public community) is asked for model / serial / page count / type.
#      Switches and access points should have SNMP v1/v2c enabled (community
#      "public") to be recognised reliably; cameras/NVRs are recognised from
#      their ports and web-login banner without SNMP.
#   3. MAC address is read from this PC's ARP table.
#
# Usage:
#   AMPM_Printer_Scanner.bat           -> scan now (window stays open)
#   AMPM_Printer_Scanner_Install.bat   -> (run as Administrator) scan every day
#   -Silent                            -> no console output (used by the task)
# ==========================================================================

$serverBase = 'https://ampm-qh-serve-1.onrender.com'
$serverUrl  = "$serverBase/api/endpoints/report-printers"
$netUrl     = "$serverBase/api/endpoints/report-network"
$agentKey  = 'AMPM-AGENT-2026'

# Extra subnets to scan besides this PC's own network, written as the first
# three numbers, e.g.  $extraSubnets = @('192.168.2', '10.0.5')
$extraSubnets = @()

$ports      = @(9100, 515, 631, 554, 8000, 37777, 80, 443, 22, 23)
$tcpWaitMs  = 3000
$maxSubnets = 8
$logFile    = Join-Path $PSScriptRoot 'ampm_printer_scan_log.txt'

function Log($msg) {
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $msg"
    Add-Content -Path $logFile -Value $line
    if (-not $Silent) { Write-Host $msg }
}

try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch {}

# ---------------------------------------------------------------- install --
if ($Install) {
    $taskName = 'AMPM Network Printer Scanner'
    $me = $PSCommandPath
    if (-not $me) { $me = $MyInvocation.MyCommand.Path }
    try {
        $action   = New-ScheduledTaskAction -Execute 'powershell.exe' `
            -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$me`" -Silent"
        $trigger  = New-ScheduledTaskTrigger -Daily -At '11:00'
        $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
            -StartWhenAvailable -MultipleInstances IgnoreNew
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
        Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger `
            -Principal $principal -Settings $settings `
            -Description 'Scans the office network for printers and reports them to the AMPM IT Tool (Asset Stock) every day.' `
            -ErrorAction Stop | Out-Null
        Write-Host ''
        Write-Host " DONE - '$taskName' installed. It will scan every day at 11:00" -ForegroundColor Green
        Write-Host ' (or at the next start-up if this PC was off at that time).'
        Write-Host ' Running the first scan now...'
        Write-Host ''
    } catch {
        Write-Host " INSTALL FAILED: $_" -ForegroundColor Red
        Write-Host ' Right-click AMPM_Printer_Scanner_Install.bat and choose "Run as administrator".'
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
    Write-Host ' AMPM - Network Printer Scanner' -ForegroundColor Cyan
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
        $pkt = Build-SnmpGet 'public' $oid
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

# ---- network-device helpers (switch / WiFi / NVR / camera) ----------------
$netBrands = @(
    @{ Re = 'hikvision|\bds-[0-9a-z]|\bids-'; Name = 'Hikvision' },
    @{ Re = 'dahua|\bdh-|\bipc-hd|\bxvr'; Name = 'Dahua' },
    @{ Re = 'cp plus|cpplus|\bcp-u|\buvr'; Name = 'CP Plus' },
    @{ Re = 'uniview|\bunv\b'; Name = 'Uniview' },
    @{ Re = 'reolink'; Name = 'Reolink' }, @{ Re = '\baxis\b'; Name = 'Axis' },
    @{ Re = 'tiandy'; Name = 'Tiandy' }, @{ Re = 'honeywell'; Name = 'Honeywell' }, @{ Re = 'hanwha|samsung techwin'; Name = 'Hanwha' },
    @{ Re = 'cisco|catalyst'; Name = 'Cisco' },
    @{ Re = 'tp-link|tplink|\btl-|\barcher|\bjetstream|\beap\d'; Name = 'TP-Link' },
    @{ Re = 'd-link|dlink|\bdgs-|\bdes-|\bdap-'; Name = 'D-Link' },
    @{ Re = 'netgear'; Name = 'Netgear' }, @{ Re = 'ubiquiti|ubnt|unifi|\buap'; Name = 'Ubiquiti' },
    @{ Re = 'mikrotik|routeros'; Name = 'MikroTik' }, @{ Re = 'huawei'; Name = 'Huawei' },
    @{ Re = 'juniper'; Name = 'Juniper' }, @{ Re = 'aruba|procurve|hewlett|\bhp\b|\bhpe\b'; Name = 'HPE / Aruba' },
    @{ Re = 'powerconnect|\bdell\b'; Name = 'Dell' }, @{ Re = 'tenda'; Name = 'Tenda' }, @{ Re = 'zyxel'; Name = 'Zyxel' },
    @{ Re = 'cambium'; Name = 'Cambium' }, @{ Re = 'ruijie'; Name = 'Ruijie' }, @{ Re = 'mercusys'; Name = 'Mercusys' },
    @{ Re = 'netis'; Name = 'Netis' }, @{ Re = 'linksys'; Name = 'Linksys' }, @{ Re = '\basus'; Name = 'ASUS' },
    @{ Re = 'extreme networks|\bexos\b'; Name = 'Extreme' }, @{ Re = '\bh3c\b'; Name = 'H3C' }
)
function Find-NetBrand([string]$text) {
    $t = ($text + ' ').ToLower()
    foreach ($b in $netBrands) { if ($t -match $b.Re) { return $b.Name } }
    return ''
}

# One plain HTTP GET (port 80 only - https is skipped on purpose so that
# certificate checks are never switched off) to read Server header, login
# realm (Hikvision puts the model there, Dahua the serial) and page title.
function Get-HttpInfo([string]$ip) {
    $info = @{ Server = ''; Realm = ''; Title = ''; Status = 0 }
    $resp = $null
    try {
        $req = [System.Net.HttpWebRequest]::Create("http://$ip/")
        $req.Method = 'GET'
        $req.Timeout = 2500
        $req.ReadWriteTimeout = 2500
        $req.AllowAutoRedirect = $false
        $req.UserAgent = 'AMPM-Scanner'
        try { $resp = $req.GetResponse() } catch [System.Net.WebException] { $resp = $_.Exception.Response }
    } catch {}
    if ($resp) {
        try {
            $info.Status = [int]$resp.StatusCode
            $info.Server = [string]$resp.Headers['Server']
            $auth = [string]$resp.Headers['WWW-Authenticate']
            if ($auth -match 'realm="([^"]*)"') { $info.Realm = $Matches[1] }
            $stream = $resp.GetResponseStream()
            $buf = New-Object byte[] 4096
            $n = $stream.Read($buf, 0, 4096)
            if ($n -gt 0) {
                $html = [System.Text.Encoding]::UTF8.GetString($buf, 0, $n)
                if ($html -match '(?is)<title[^>]*>(.*?)</title>') { $info.Title = ($Matches[1] -replace '\s+', ' ').Trim() }
            }
            $stream.Close()
        } catch {}
        try { $resp.Close() } catch {}
    }
    return $info
}

# Decide switch / WiFi / NVR / camera ('' = not a network device).
function Get-NetCategory([object]$open, [string]$text, [bool]$hasSnmp, [long]$services) {
    $t = $text.ToLower()
    $camText = 'hikvision|dahua|\bipc\b|ip camera|network camera|netcam|cp plus|cpplus|uniview|reolink|\bds-2c|\bds-2d|\bdh-ipc|onvif|camera|\bpnc|\bpnm'
    $nvrText = '\bnvr|\bdvr|\bxvr|\buvr|recorder|\bds-7|\bds-8|\bds-9|\bids-|\bds-n|\bdh-nvr|\bdh-xvr'
    $strongCam = ($open -contains 554) -or ($open -contains 37777)
    $weakCam   = ($open -contains 8000) -and (($t -match $camText) -or ($t -match $nvrText))
    if ($strongCam -or $weakCam) {
        if ($t -match $nvrText) { return 'NVR' }
        return 'Camera'
    }
    $apText     = 'access point|wireless|wi-?fi|wlan|unifi|\buap\b|\buap-|cambium|mercusys|\beap\d|\bap\d{2,}'
    $routerText = 'router|routeros|gateway|firewall|archer|\btl-wr|\bvigor|fortigate|pfsense|openwrt'
    $switchText = 'switch|catalyst|procurve|jetstream|powerconnect|\bsg\d|\btl-sg|\bdgs-|\bgs\d{3}|\bws-c|cisco ios'
    $brandHit = (Find-NetBrand $t) -ne ''
    if (-not $hasSnmp -and -not $brandHit) { return '' }
    if ($t -match $apText)     { return 'WiFi Device' }
    if ($t -match $routerText) { return 'WiFi Device' }
    if ($t -match $switchText) { return 'Network Switch' }
    if ($hasSnmp) {
        if (($services -band 2) -ne 0) { return 'Network Switch' }
        if (($services -band 4) -ne 0) { return 'WiFi Device' }
    }
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
$netFound = New-Object System.Collections.ArrayList
$candidates = $allOpen.Keys | Sort-Object { IpToLong $_ }
foreach ($ip in $candidates) {
    if ($ip -eq $ownIp) { continue }
    $open = @($allOpen[$ip])
    $isRaw = ($open -contains 9100) -or ($open -contains 515)
    $strongCam = ($open -contains 554) -or ($open -contains 37777)

    # SNMP is skipped for obvious cameras/NVRs (they rarely answer and each
    # silent host costs about a second).
    $sysDescr = $null
    if (-not $strongCam) { $sysDescr = Get-Snmp $ip '1.3.6.1.2.1.1.1.0' }
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

    if ($looksPrinter) {
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
        continue
    }

    # ---- not a printer: switch / WiFi / NVR / camera? ----
    $webPort = ($open -contains 80) -or ($open -contains 443) -or ($open -contains 22) -or ($open -contains 23) -or ($open -contains 8000) -or $strongCam
    if (-not $webPort) { continue }

    $http = @{ Server = ''; Realm = ''; Title = ''; Status = 0 }
    if ($open -contains 80) { $http = Get-HttpInfo $ip }

    $services = [long]0
    $hasSnmp = [bool]$sysDescr
    if ($hasSnmp) {
        $sv = Get-Snmp $ip '1.3.6.1.2.1.1.7.0'
        if ($sv) { $services = [long]$sv }
    }
    $text = "$($http.Server) $($http.Realm) $($http.Title) $sysDescr $sysName"
    $cat = Get-NetCategory $open $text $hasSnmp $services
    if (-not $cat) { continue }

    $ifNum = ''; $entModel = ''; $entSerial = ''
    if ($hasSnmp) {
        $ifNum     = Get-Snmp $ip '1.3.6.1.2.1.2.1.0'
        $entModel  = Get-Snmp $ip '1.3.6.1.2.1.47.1.1.1.1.13.1'
        $entSerial = Get-Snmp $ip '1.3.6.1.2.1.47.1.1.1.1.11.1'
    }

    $brand = Find-NetBrand $text
    if (-not $brand -and ($open -contains 37777)) { $brand = 'Dahua' }   # 37777 is Dahua's own protocol port
    $model = $entModel
    if (-not $model -and $http.Realm -match '^(i?DS-|NVR|XVR|DVR|DH-|IPC|UVR|HW|CP-|UNV)') { $model = $http.Realm }
    if (-not $model -and $http.Title -and $http.Title.Length -le 60 -and $brand) { $model = $http.Title }
    if (-not $model -and $sysDescr) { $model = $sysDescr; if ($model.Length -gt 60) { $model = $model.Substring(0, 60) } }
    if ($brand -and $model -and $model.ToLower().StartsWith($brand.ToLower() + ' ')) { $model = $model.Substring($brand.Length + 1).Trim() }
    $nserial = $entSerial
    if (-not $nserial -and $http.Realm -match '^Login to (\S+)') { $nserial = $Matches[1] }

    $hostName = $sysName
    if (-not $hostName) {
        try { $hostName = ([System.Net.Dns]::GetHostEntry($ip)).HostName } catch { $hostName = '' }
    }
    $descr = ("$sysDescr $($http.Title)").Trim()
    if ($descr.Length -gt 120) { $descr = $descr.Substring(0, 120) }

    [void]$netFound.Add([ordered]@{
        ip        = $ip
        mac       = [string]$arp[$ip]
        hostname  = [string]$hostName
        category  = [string]$cat
        brand     = [string]$brand
        model     = [string]$model
        serial    = [string]$nserial
        descr     = [string]$descr
        portCount = [string]$ifNum
        ports     = ($open -join ',')
    })
}

Log "Scan finished - $($found.Count) printer(s), $($netFound.Count) network device(s) found"

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
    if ($netFound.Count -eq 0) {
        Write-Host ' No switches / WiFi / NVR / cameras recognised.' -ForegroundColor Yellow
    } else {
        Write-Host " Found $($netFound.Count) network device(s) (switch / WiFi / NVR / camera):" -ForegroundColor Green
        foreach ($f in $netFound) {
            Write-Host ('   {0,-15} {1,-17} {2,-15} {3} {4}' -f $f.ip, $f.mac, $f.category, $f.brand, $f.model)
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
        if (-not $Silent) { Write-Host ' Server rejected the request.' -ForegroundColor Yellow }
    }
} catch {
    Log "FAILED to send - $_"
    if (-not $Silent) { Write-Host " FAILED to send to the website - check internet. Error: $_" -ForegroundColor Red }
}

# network devices (switch / WiFi / NVR / camera) -> Asset Stock "Network Devices" tab
$body2 = [ordered]@{
    key         = $agentKey
    scannedFrom = $env:COMPUTERNAME
    subnets     = ($prefixes -join ', ')
    devices     = @($netFound)
}
$bytes2 = [System.Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject $body2 -Depth 5))
try {
    $resp2 = Invoke-RestMethod -Uri $netUrl -Method Post -Body $bytes2 -ContentType 'application/json; charset=utf-8' -TimeoutSec 90
    if ($resp2.ok -eq $true) {
        Log "OK - network devices sent: $($resp2.added) new, $($resp2.updated) updated"
        if (-not $Silent) { Write-Host " DONE - network devices updated: $($resp2.added) new, $($resp2.updated) refreshed (Asset Stock -> Network Devices tab)." -ForegroundColor Green }
    } else {
        Log "Server rejected network devices: $($resp2 | ConvertTo-Json -Compress)"
    }
} catch {
    Log "FAILED to send network devices - $_"
    if (-not $Silent) { Write-Host " FAILED to send network devices. Error: $_" -ForegroundColor Red }
}

if (-not $Silent) {
    Write-Host ''
    Write-Host "Log file: $logFile"
    Read-Host 'Press Enter to close'
}
