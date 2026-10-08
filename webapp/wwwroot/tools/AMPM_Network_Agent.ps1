param(
    [switch]$Silent,
    [switch]$Install
)
$ErrorActionPreference = 'SilentlyContinue'

# ==========================================================================
# AMPM IT Tool - Network Device Agent
#
# Finds every switch, WiFi access point / router, NVR / DVR recorder and IP
# camera on this PC's LAN (and any extra subnets listed below) and pushes them
# to the AMPM IT Tool website -> Asset Stock -> Network Devices tab.
#   * New device   -> added with the next tag: SW-0001 (switch), WIFI-0001,
#                     NVR-0001, CAM-0001; devices that answer but cannot be told
#                     apart come in as NET-0001 "Other Network Device" - open Edit
#                     on the website and pick the real type.
#   * Known device -> only IP / MAC / hostname / last-seen are refreshed -
#                     assignment, location and anything you typed is never touched.
# Network printers are NOT handled here - use the Printer Agent.
# How: TCP probe (554/8000/37777 cameras+NVR, 80/443/22/23 management), plain
# HTTP page + login banner, ONVIF probe for cameras/NVR (model + camera/recorder),
# SNMP for switches/WiFi (enable SNMP v1/v2c, community "public", on them for the
# best result); MAC from this PC's ARP table.
#
# This file is downloaded from the website (Asset Stock -> Agents). The access
# key is put into it at download time, so download it again whenever the key
# changes or a new version is published.
#
# Usage:
#   AMPM_Network_Agent.bat          -> scan now (window stays open)
#   AMPM_Network_Agent_Setup.bat    -> (run as Administrator) scan every day at 11:30
#   -Silent                         -> no console output (used by the scheduled task)
# ==========================================================================
$serverBase = 'https://ampm-qh-serve-1.onrender.com'
$netUrl     = "$serverBase/api/endpoints/report-network"
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

$ports      = @(554, 8000, 37777, 80, 443, 22, 23, 9100, 515, 631)
$tcpWaitMs  = 3000
$maxSubnets = 8
$logFile    = Join-Path $PSScriptRoot 'ampm_network_log.txt'

function Log($msg) {
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $msg"
    Add-Content -Path $logFile -Value $line
    if (-not $Silent) { Write-Host $msg }
}

try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch {}

# ---------------------------------------------------------------- install --
if ($Install) {
    $taskName = 'AMPM Network Device Agent'
    $destDir  = Join-Path $env:ProgramData 'AMPM\NetworkAgent'
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
        $trigger  = New-ScheduledTaskTrigger -Daily -At '11:30'

        $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
            -StartWhenAvailable -MultipleInstances IgnoreNew
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName 'AMPM Network Printer Scanner' -Confirm:$false -ErrorAction SilentlyContinue
        Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger `
            -Principal $principal -Settings $settings `
            -Description 'Scans the office network for switches, WiFi, NVR and cameras and reports them to the AMPM IT Tool (Asset Stock - Network Devices) every day.' `
            -ErrorAction Stop | Out-Null
        Write-Host ''
        Write-Host " DONE - '$taskName' installed (scans every day at 11:30)." -ForegroundColor Green
        Write-Host " Installed copy : $dest"
        Write-Host ' Running it once now...'
        Write-Host ''
    } catch {
        Write-Host " INSTALL FAILED: $_" -ForegroundColor Red
        Write-Host ' Right-click AMPM_Network_Agent_Setup.bat and choose "Run as administrator".'
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
    Write-Host ' AMPM - Network Device Agent' -ForegroundColor Cyan
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
function Get-HttpInfo([string]$ip, [string]$path = '/') {
    $info = @{ Server = ''; Realm = ''; Title = ''; Status = 0 }
    $resp = $null
    try {
        $req = [System.Net.HttpWebRequest]::Create("http://$ip$path")
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

# ONVIF discovery probe, sent straight to one camera/recorder (UDP 3702). Almost
# every IP camera and NVR answers it WITHOUT a password and tells its hardware
# model, its name and whether it is a camera or a recorder.
function Get-Onvif([string]$ip) {
    $res = @{ Hardware = ''; Name = ''; Types = '' }
    $udp = $null
    try {
        $udp = New-Object System.Net.Sockets.UdpClient
        $udp.Client.ReceiveTimeout = 1200
        $xml = '<?xml version="1.0" encoding="UTF-8"?><e:Envelope xmlns:e="http://www.w3.org/2003/05/soap-envelope" xmlns:w="http://schemas.xmlsoap.org/ws/2004/08/addressing" xmlns:d="http://schemas.xmlsoap.org/ws/2005/04/discovery"><e:Header><w:MessageID>uuid:' + [guid]::NewGuid().ToString() + '</w:MessageID><w:To>urn:schemas-xmlsoap-org:ws:2005:04:discovery</w:To><w:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</w:Action></e:Header><e:Body><d:Probe/></e:Body></e:Envelope>'
        $b = [System.Text.Encoding]::UTF8.GetBytes($xml)
        [void]$udp.Send($b, $b.Length, $ip, 3702)
        $ep = New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Any, 0)
        $r = $udp.Receive([ref]$ep)
        $txt = [System.Text.Encoding]::UTF8.GetString($r)
        if ($txt -match 'onvif://www\.onvif\.org/hardware/([^\s<]+)') { $res.Hardware = [uri]::UnescapeDataString($Matches[1]) }
        if ($txt -match 'onvif://www\.onvif\.org/name/([^\s<]+)') { $res.Name = [uri]::UnescapeDataString($Matches[1]) }
        $tl = @()
        foreach ($m in [regex]::Matches($txt, 'onvif://www\.onvif\.org/type/([^\s<]+)')) { $tl += $m.Groups[1].Value }
        $res.Types = ($tl -join ' ')
    } catch {} finally { if ($udp) { try { $udp.Close() } catch {} } }
    return $res
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

$netFound = New-Object System.Collections.ArrayList
$candidates = $allOpen.Keys | Sort-Object { IpToLong $_ }
foreach ($ip in $candidates) {
    if ($ip -eq $ownIp) { continue }
    $open = @($allOpen[$ip])
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

    # Printers belong to the Printer Agent.
    $isPrinter = ($open -contains 9100) -or ($open -contains 515) -or ($open -contains 631) -or ($serial) -or ($hrDescr -and $pages)
    if ($isPrinter) {
        Log ("  host {0} ports={1} -> printer / print server (skipped - Printer Agent handles it)" -f $ip, ($open -join ','))
        continue
    }

    # ---- not a printer: switch / WiFi / NVR / camera? ----
    $webPort = ($open -contains 80) -or ($open -contains 443) -or ($open -contains 22) -or ($open -contains 23) -or ($open -contains 8000) -or $strongCam
    if (-not $webPort) { continue }

    $http = @{ Server = ''; Realm = ''; Title = ''; Status = 0 }
    if ($open -contains 80) { $http = Get-HttpInfo $ip }

    # Newer Hikvision / Dahua recorders show a plain web page on "/" (no model in
    # it). Their API paths answer "401 login required" WITHOUT any password, and
    # that answer carries the model (Hikvision) or serial number (Dahua) in the
    # login realm - which is what tells an NVR from a camera.
    if (($open -contains 80) -and ($http.Status -ne 0) -and ($strongCam -or ($open -contains 8000)) -and -not $http.Realm) {
        $h2 = Get-HttpInfo $ip '/ISAPI/System/deviceInfo'
        if ($h2.Realm) { $http.Realm = $h2.Realm }
        else {
            $h3 = Get-HttpInfo $ip '/cgi-bin/magicBox.cgi?action=getSystemInfo'
            if ($h3.Realm) { $http.Realm = $h3.Realm }
        }
    }

    $onv = @{ Hardware = ''; Name = ''; Types = '' }
    if ($strongCam -or ($open -contains 8000)) { $onv = Get-Onvif $ip }

    $services = [long]0
    $hasSnmp = [bool]$sysDescr
    if ($hasSnmp) {
        $sv = Get-Snmp $ip '1.3.6.1.2.1.1.7.0'
        if ($sv) { $services = [long]$sv }
    }
    $text = "$($http.Server) $($http.Realm) $($http.Title) $sysDescr $sysName $($onv.Hardware) $($onv.Name) $($onv.Types)"
    $cat = Get-NetCategory $open $text $hasSnmp $services
    $logCat = $cat
    if (-not $cat) {
        # Answers like a network device but cannot be told apart (typically a switch
        # or router with SNMP switched off). Plain web/file servers are left out; the
        # rest is sent as "Other Network Device" so it shows up in the website and
        # can be given its real type there with Edit.
        $isServer = ($http.Server -match 'IIS|Microsoft|nginx|Apache|Kestrel|Werkzeug|gunicorn|Express|Tomcat|Jetty') -or ($sysDescr -match 'Windows|Linux|Darwin|FreeBSD')
        if ($isServer) { $logCat = 'skipped (looks like a server/PC)' } else { $cat = 'Other Network Device'; $logCat = $cat }
    }
    Log ("  host {0} ports={1} snmp={2} server='{3}' realm='{4}' title='{5}' onvif='{6} {7} {8}' -> {9}" -f $ip, ($open -join ','), $(if ($hasSnmp) { 'yes' } else { 'no' }), $http.Server, $http.Realm, $http.Title, $onv.Hardware, $onv.Name, $onv.Types, $logCat)
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
    if (-not $model -and $onv.Hardware) { $model = $onv.Hardware }
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

Log "Scan finished - $($netFound.Count) network device(s) found"
$typeCount = @{}
foreach ($nf in $netFound) { $typeCount[[string]$nf.category] = 1 + [int]$typeCount[[string]$nf.category] }
Log ("  network device types: " + ((@($typeCount.Keys | Sort-Object | ForEach-Object { "$($typeCount[$_]) $_" })) -join ', '))

if (-not $Silent) {
    Write-Host ''
    if ($netFound.Count -eq 0) {
        Write-Host ' No switches / WiFi / NVR / cameras recognised.' -ForegroundColor Yellow
    } else {
        Write-Host " Found $($netFound.Count) network device(s):" -ForegroundColor Green
        foreach ($f in $netFound) {
            Write-Host ('   {0,-15} {1,-17} {2,-20} {3} {4}' -f $f.ip, $f.mac, $f.category, $f.brand, $f.model)
        }
    }
    Write-Host ''
}

# ------------------------------------------------------------------ send ---
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
        if (-not $Silent) { Write-Host ' Server rejected the request (wrong or old key? download this agent again from the website).' -ForegroundColor Yellow }
    }
} catch {
    Log "FAILED to send network devices - $_"
    if (-not $Silent) { Write-Host " FAILED to send network devices - check internet / key. Error: $_" -ForegroundColor Red }
}

if (-not $Silent) {
    Write-Host ''
    Write-Host "Log file: $logFile"
    Read-Host 'Press Enter to close'
}
