param(
    [switch]$Silent,
    [switch]$Install
)
$ErrorActionPreference = 'SilentlyContinue'

# AMPM IT Tool - Print Monitor Agent
# Reads this PC's Windows print history (every document printed: who, when, which
# printer, how many pages) and sends it to the AMPM IT Tool website
# (Endpoints -> Print Log, with an Excel download).
#
# This file is downloaded from the website (Asset Stock -> Agents). The access
# key is put into it at download time, so download it again whenever the key
# changes or a new version is published.
#
#   AMPM_Print_Agent.bat          -> send this PC's print history now (window stays open)
#   AMPM_Print_Agent_Setup.bat    -> (run once as Administrator) send automatically
#                                    at every restart and every 5 minutes
#   -Silent                       -> no console output (used by the scheduled task)
#
# Windows keeps the print history only if its "PrintService / Operational" log is
# switched on. This agent switches it on (needs Administrator, Setup does that) -
# so only prints made AFTER the first install are recorded.

$agentVersion = '1.0'
$serverUrl = 'https://ampm-qh-serve-1.onrender.com/api/endpoints/report-prints'
$agentKey  = '__AMPM_AGENT_KEY__'   # filled in automatically when you download this from the website
$logName   = 'Microsoft-Windows-PrintService/Operational'
$logFile   = Join-Path $PSScriptRoot 'ampm_print_log.txt'
$stateFile = Join-Path $PSScriptRoot 'ampm_print_state.json'
$batchSize = 200

function Log($msg) {
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $msg"
    Add-Content -Path $logFile -Value $line
    if (-not $Silent) { Write-Host $msg }
}

if ($agentKey -like '__AMPM*') {
    Write-Host ''
    Write-Host ' This copy has no access key.' -ForegroundColor Yellow
    Write-Host ' Please download it again from the website:' -ForegroundColor Yellow
    Write-Host '   Asset Stock -> Agents -> Download Print Monitor Agent' -ForegroundColor Yellow
    if (-not $Silent) { Read-Host 'Press Enter to close' }
    exit 1
}

try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch {}

# ---------------------------------------------------------------- install --
if ($Install) {
    $taskName = 'AMPM Print Agent'
    $destDir  = Join-Path $env:ProgramData 'AMPM\PrintAgent'
    $me = $PSCommandPath
    if (-not $me) { $me = $MyInvocation.MyCommand.Path }
    try {
        # The task runs a copy kept in ProgramData, so it keeps working even if the
        # download folder is deleted. Running Setup again after a new download
        # simply replaces that copy (the "already sent" memory is kept).
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        $dest = Join-Path $destDir (Split-Path $me -Leaf)
        if ($me -ne $dest) { Copy-Item -LiteralPath $me -Destination $dest -Force -ErrorAction Stop }

        # Switch the Windows print history on and give it room (20 MB).
        & wevtutil.exe sl $logName /e:true 2>&1 | Out-Null
        & wevtutil.exe sl $logName /ms:20971520 2>&1 | Out-Null

        $action   = New-ScheduledTaskAction -Execute 'powershell.exe' `
            -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$dest`" -Silent"
        $t1 = New-ScheduledTaskTrigger -AtStartup
        $t2 = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) `
            -RepetitionInterval (New-TimeSpan -Minutes 5) -RepetitionDuration (New-TimeSpan -Days 3650)
        $trigger = @($t1, $t2)
        $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
            -StartWhenAvailable -MultipleInstances IgnoreNew
        foreach ($old in @('AMPM Print Agent', 'AMPM_Print_Agent', 'AMPM Print Monitor Agent')) {
            Unregister-ScheduledTask -TaskName $old -Confirm:$false -ErrorAction SilentlyContinue
        }
        Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger `
            -Principal $principal -Settings $settings `
            -Description 'Sends this PC print history (user, printer, pages, time) to the AMPM IT Tool at start-up and every 5 minutes.' `
            -ErrorAction Stop | Out-Null
        Write-Host ''
        Write-Host " DONE - '$taskName' installed (sends print history at start-up and every 5 minutes)." -ForegroundColor Green
        Write-Host " Installed copy : $dest"
        Write-Host ' Windows print history is switched ON - prints made from now on are recorded.'
        Write-Host ' Sending once now...'
        Write-Host ''
        # From here on this run keeps its state/log in the installed folder too.
        $logFile   = Join-Path $destDir 'ampm_print_log.txt'
        $stateFile = Join-Path $destDir 'ampm_print_state.json'
    } catch {
        Write-Host " INSTALL FAILED: $_" -ForegroundColor Red
        Write-Host ' Right-click AMPM_Print_Agent_Setup.bat and choose "Run as administrator".'
        Read-Host 'Press Enter to close'
        exit 1
    }
}

# ------------------------------------------------------ this PC's details --
$hostName = $env:COMPUTERNAME
$ip = $null
try {
    $route = Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue |
        Sort-Object -Property RouteMetric | Select-Object -First 1
    if ($route) {
        $ip = Get-NetIPAddress -InterfaceIndex $route.InterfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
            Where-Object { $_.IPAddress -notlike '169.254.*' } | Select-Object -First 1 -ExpandProperty IPAddress
    }
} catch {}
if (-not $ip) {
    $ip = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -notlike '169.254.*' -and $_.IPAddress -ne '127.0.0.1' } |
        Select-Object -First 1 -ExpandProperty IPAddress
}
if (-not $ip) { $ip = '' }

# ------------------------------------------------- make sure the log is on --
$logInfo = $null
try { $logInfo = Get-WinEvent -ListLog $logName -ErrorAction Stop } catch {}
if ($logInfo -and -not $logInfo.IsEnabled) {
    & wevtutil.exe sl $logName /e:true 2>&1 | Out-Null
    & wevtutil.exe sl $logName /ms:20971520 2>&1 | Out-Null
    try { $logInfo = Get-WinEvent -ListLog $logName -ErrorAction Stop } catch {}
    if ($logInfo -and $logInfo.IsEnabled) { Log 'Windows print history was OFF - switched it ON (prints from now on are recorded).' }
    else { Log 'Windows print history is OFF and could not be switched on - run AMPM_Print_Agent_Setup.bat as Administrator.' }
}

# ---------------------------------------------------- what was sent already --
$lastId = [long]0
if (Test-Path -LiteralPath $stateFile) {
    try {
        $st = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
        if ($st -and $st.lastRecordId) { $lastId = [long]$st.lastRecordId }
    } catch {}
}

# ------------------------------------------------- read the print history --
# Event 307 = "a document was printed". Fields, in order:
#   JobId, DocumentName, Owner (user), ClientMachine, Printer, Port, SizeBytes, Pages
$events = @()
try {
    $events = @(Get-WinEvent -FilterHashtable @{ LogName = $logName; Id = 307 } -Oldest -ErrorAction Stop)
} catch {
    # "No events were found" is normal (nothing printed yet) - anything else is logged.
    if ($_.Exception.Message -notlike '*No events*') { Log "Could not read the print log: $($_.Exception.Message)" }
}

# If Windows cleared the log, record ids start again from 1.
if ($events.Count -gt 0 -and $lastId -gt 0) {
    $maxId = ($events | Measure-Object -Property RecordId -Maximum).Maximum
    if ($maxId -lt $lastId) { $lastId = 0 }
}

$newEvents = @($events | Where-Object { $_.RecordId -gt $lastId })

function ToJob($e) {
    $p = $e.Properties
    $v = { param($i) if ($p.Count -gt $i -and $null -ne $p[$i].Value) { [string]$p[$i].Value } else { '' } }
    @{
        recordId = [string]$e.RecordId
        time     = $e.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss')
        document = (& $v 1)
        user     = (& $v 2)
        client   = (& $v 3)
        printer  = (& $v 4)
        port     = (& $v 5)
        bytes    = (& $v 6)
        pages    = (& $v 7)
    }
}

$sent = 0; $failed = $false; $dup = 0
$jobsAll = @($newEvents | ForEach-Object { ToJob $_ })

function Send-Batch($batch) {
    $payload = @{
        key          = $agentKey
        hostname     = $hostName
        ip           = $ip
        agentVersion = $agentVersion
        jobs         = @($batch)
    }
    $json  = ConvertTo-Json -InputObject $payload -Depth 6 -Compress
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    return Invoke-RestMethod -Uri $serverUrl -Method Post -Body $bytes -ContentType 'application/json; charset=utf-8' -TimeoutSec 40
}

if ($jobsAll.Count -eq 0) {
    # Nothing new - still tell the website this PC's agent is alive.
    try {
        $resp = Send-Batch @()
        if ($resp.ok -eq $true) { Log 'OK - no new prints (agent alive)' }
        else { Log "Server rejected: $($resp | ConvertTo-Json -Compress)"; $failed = $true }
    } catch { Log "FAILED - $_"; $failed = $true }
} else {
    for ($i = 0; $i -lt $jobsAll.Count; $i += $batchSize) {
        $end = [Math]::Min($i + $batchSize, $jobsAll.Count) - 1
        $batch = @($jobsAll[$i..$end])
        try {
            $resp = Send-Batch $batch
            if ($resp.ok -eq $true) {
                $sent += $batch.Count
                if ($resp.duplicates) { $dup += [int]$resp.duplicates }
                # remember how far we got, so a failed batch is retried next time
                $lastBatchId = [long](($batch | ForEach-Object { [long]$_.recordId } | Measure-Object -Maximum).Maximum)
                try { (@{ lastRecordId = $lastBatchId; savedAt = (Get-Date).ToString('s') } | ConvertTo-Json) | Set-Content -LiteralPath $stateFile -Encoding UTF8 } catch {}
                Log "OK - sent $($batch.Count) print job(s) (record ids up to $lastBatchId)"
            } else {
                Log "Server rejected: $($resp | ConvertTo-Json -Compress)"
                $failed = $true; break
            }
        } catch {
            Log "FAILED - $_"
            $failed = $true; break
        }
    }
}

if (-not $Silent) {
    Write-Host ''
    Write-Host ' AMPM Print Monitor Agent' -ForegroundColor Cyan
    Write-Host ' -------------------------'
    Write-Host " PC            : $hostName ($ip)"
    Write-Host " Print history : $(if ($logInfo -and $logInfo.IsEnabled) { 'ON' } else { 'OFF (run Setup as Administrator)' })"
    Write-Host " Prints found  : $($events.Count) in Windows log, $($newEvents.Count) not sent before"
    Write-Host " Sent now      : $sent"
    if ($failed) { Write-Host ' Some data could not be sent (check internet / download this agent again). It will retry.' -ForegroundColor Yellow }
    elseif ($events.Count -eq 0) { Write-Host ' No prints recorded yet - they appear after the first print from this PC.' -ForegroundColor Yellow }
    else { Write-Host " DONE - see the website: Monitor -> Print Log" -ForegroundColor Green }
    Write-Host ''
    Write-Host "Log file: $logFile"
    Read-Host 'Press Enter to close'
}
