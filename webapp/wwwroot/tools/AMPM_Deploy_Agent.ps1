param(
    [switch]$Silent,
    [switch]$Install
)
$ErrorActionPreference = 'SilentlyContinue'
$ProgressPreference = 'SilentlyContinue'

# AMPM IT Tool - Deploy Agent (Windows)
# Lets the AMPM IT Tool website install software / patches and update the AMPM agents on this
# PC (Software Deploy page). Every 5 minutes it asks the website "is anything waiting for me?",
# downloads it, checks the SHA-256, runs it with administrator rights (the scheduled task runs as
# SYSTEM) and reports the result back.
#
# This file is downloaded from the website (Asset Stock -> Agents). The access key is put into it
# at download time, so download it again whenever the key changes or a new version is published.
#
#   AMPM_Deploy_Agent.bat          -> check for work now (window stays open)
#   AMPM_Deploy_Agent_Setup.bat    -> (run once as Administrator) check automatically at every
#                                     restart and every 5 minutes
#   -Silent                        -> no console output (used by the scheduled task)

$agentVersion = '1.0'
$baseUrl  = 'https://ampm-qh-serve-1.onrender.com'
$agentKey = '__AMPM_AGENT_KEY__'   # filled in automatically when you download this from the website
$taskName = 'AMPM Deploy Agent'
$dataDir  = Join-Path $env:ProgramData 'AMPM\DeployAgent'
$logFile  = Join-Path $dataDir 'ampm_deploy_log.txt'
$procTimeoutMs = 3600000   # an installer may run for up to 60 minutes

New-Item -ItemType Directory -Path $dataDir -Force | Out-Null

function Log($msg) {
    try {
        if ((Test-Path -LiteralPath $logFile) -and ((Get-Item -LiteralPath $logFile).Length -gt 1MB)) {
            Move-Item -LiteralPath $logFile -Destination ($logFile + '.old') -Force
        }
        Add-Content -Path $logFile -Value ("$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $msg")
    } catch {}
    if (-not $Silent) { Write-Host $msg }
}

if ($agentKey -like '__AMPM*') {
    Write-Host ''
    Write-Host ' This copy has no access key.' -ForegroundColor Yellow
    Write-Host ' Please download it again from the website:' -ForegroundColor Yellow
    Write-Host '   Asset Stock -> Agents -> Download Deploy Agent' -ForegroundColor Yellow
    if (-not $Silent) { Read-Host 'Press Enter to close' }
    exit 1
}

try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch {}

# ---------------------------------------------------------------- install --
if ($Install) {
    $me = $PSCommandPath
    if (-not $me) { $me = $MyInvocation.MyCommand.Path }
    try {
        $dest = Join-Path $dataDir 'AMPM_Deploy_Agent.ps1'
        if ($me -ne $dest) { Copy-Item -LiteralPath $me -Destination $dest -Force -ErrorAction Stop }

        $action   = New-ScheduledTaskAction -Execute 'powershell.exe' `
            -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$dest`" -Silent"
        $t1 = New-ScheduledTaskTrigger -AtStartup
        $t2 = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) `
            -RepetitionInterval (New-TimeSpan -Minutes 5) -RepetitionDuration (New-TimeSpan -Days 3650)
        $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
            -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Hours 3)
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
        Register-ScheduledTask -TaskName $taskName -Action $action -Trigger @($t1, $t2) `
            -Principal $principal -Settings $settings `
            -Description 'Lets the AMPM IT Tool install software and patches on this PC. Checks the website at start-up and every 5 minutes.' `
            -ErrorAction Stop | Out-Null
        Log "Installed '$taskName' (version $agentVersion)."
        if (-not $Silent) {
            Write-Host ''
            Write-Host " DONE - '$taskName' installed. It checks the website at start-up and every 5 minutes." -ForegroundColor Green
            Write-Host " Installed copy : $dest"
            Write-Host ''
            Read-Host 'Press Enter to close'
        }
        exit 0
    } catch {
        Log "INSTALL FAILED: $_"
        if (-not $Silent) {
            Write-Host " INSTALL FAILED: $_" -ForegroundColor Red
            Write-Host ' Right-click AMPM_Deploy_Agent_Setup.bat and choose "Run as administrator".'
            Read-Host 'Press Enter to close'
        }
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
if (-not $ip) { $ip = '' }
$loggedUser = ''
try { $loggedUser = [string](Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue).UserName } catch {}

function PostJson($path, $obj) {
    $json  = ConvertTo-Json -InputObject $obj -Depth 6 -Compress
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    return Invoke-RestMethod -Uri ($baseUrl + $path) -Method Post -Body $bytes -ContentType 'application/json; charset=utf-8' -TimeoutSec 60
}

function Report($id, $status, $code, $out) {
    $o = [string]$out
    if ($o.Length -gt 3500) { $o = $o.Substring($o.Length - 3500) }
    try {
        PostJson '/api/deploy/result' @{ key = $agentKey; hostname = $hostName; taskId = [string]$id; status = $status; exitCode = [string]$code; output = $o } | Out-Null
    } catch { Log "Could not report the result of task $id : $_" }
}

# Runs a program, waits (with a time limit) and returns the exit code + the last of its output.
function Run-Program($file, $arguments, $timeoutMs) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $file
    $psi.Arguments = $arguments
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    if (-not $p) { return @{ code = -1; out = 'Could not start the program.' } }
    $so = $p.StandardOutput.ReadToEndAsync()
    $se = $p.StandardError.ReadToEndAsync()
    if (-not $p.WaitForExit($timeoutMs)) {
        try { $p.Kill() } catch {}
        return @{ code = -1; out = 'Timed out and was stopped.' }
    }
    $p.WaitForExit()
    $text = ([string]$so.Result + "`n" + [string]$se.Result).Trim()
    return @{ code = [int]$p.ExitCode; out = $text }
}

function Download($url, $dest) {
    Invoke-WebRequest -Uri $url -OutFile $dest -UseBasicParsing -TimeoutSec 3600 -ErrorAction Stop
}

# ------------------------------------------------------------ ask for work --
$poll = $null
try {
    $poll = PostJson '/api/deploy/poll' @{ key = $agentKey; hostname = $hostName; ip = $ip; os = 'windows'; agentVersion = $agentVersion; user = $loggedUser }
} catch {
    Log "Could not reach the website: $_"
    if (-not $Silent) { Read-Host 'Press Enter to close' }
    exit 0
}
if (-not $poll -or $poll.ok -ne $true) {
    Log 'The website rejected the check (wrong or old key? download this agent again).'
    if (-not $Silent) { Read-Host 'Press Enter to close' }
    exit 0
}

$tasks = @($poll.tasks | Where-Object { $_ })
if ($tasks.Count -eq 0) {
    if (-not $Silent) { Write-Host ' Nothing waiting for this PC.' -ForegroundColor Green }
}

foreach ($t in $tasks) {
    $id = $t.id
    Log "Task $id '$($t.name)' ($($t.kind)) started."
    Report $id 'running' 0 'Started'
    $work = Join-Path $dataDir ("work\" + $id)
    $selfUpdate = $false
    try {
        New-Item -ItemType Directory -Path $work -Force | Out-Null
        $keyQ = [uri]::EscapeDataString($agentKey)

        if ($t.kind -eq 'installer') {
            $leaf = Split-Path ([string]$t.fileName) -Leaf
            $file = Join-Path $work $leaf
            Download ($baseUrl + '/api/deploy/file?task=' + $id + '&key=' + $keyQ) $file
            $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash
            if ($hash -ne ([string]$t.sha256)) { throw "SHA-256 mismatch - the file was not run. (got $hash)" }

            $extra = [string]$t.args
            if ($leaf -like '*.msi') {
                $res = Run-Program 'msiexec.exe' ('/i "' + $file + '" /qn /norestart ' + $extra) $procTimeoutMs
            } else {
                $res = Run-Program $file $extra $procTimeoutMs
            }
            $code = [int]$res.code
            if ($code -eq 0 -or $code -eq 3010 -or $code -eq 1641) {
                $note = if ($code -eq 0) { 'Installed successfully.' } else { 'Installed - a restart is needed to finish.' }
                Report $id 'success' $code ($note + "`n" + $res.out)
                Log "Task $id done (exit code $code)."
            } else {
                Report $id 'failed' $code ("Exit code $code`n" + $res.out)
                Log "Task $id FAILED (exit code $code)."
            }
        }
        elseif ($t.kind -eq 'agent') {
            $type = [string]$t.agentType
            $zip = Join-Path $work 'agent.zip'
            Download ($baseUrl + '/api/deploy/agent-zip?type=' + [uri]::EscapeDataString($type) + '&key=' + $keyQ) $zip
            $dir = Join-Path $work 'x'
            Expand-Archive -LiteralPath $zip -DestinationPath $dir -Force -ErrorAction Stop
            $script = Get-ChildItem -Path $dir -Filter '*.ps1' -Recurse | Select-Object -First 1
            if (-not $script) { throw 'The agent package has no script.' }
            $psArgs = '-NoProfile -ExecutionPolicy Bypass -File "' + $script.FullName + '" -Install -Silent'
            if ($type -eq 'deploy') {
                # This agent replaces itself: report first (the install re-registers this very task).
                Report $id 'success' 0 'Deploy Agent update started on this PC.'
                $selfUpdate = $true
                Log "Task $id : updating the Deploy Agent."
                $res = Run-Program 'powershell.exe' $psArgs 600000
                Log "Deploy Agent update finished (exit code $($res.code))."
            } else {
                $res = Run-Program 'powershell.exe' $psArgs 600000
                $code = [int]$res.code
                if ($code -eq 0) { Report $id 'success' 0 ("Agent updated.`n" + $res.out); Log "Task $id done." }
                else { Report $id 'failed' $code ("Exit code $code`n" + $res.out); Log "Task $id FAILED (exit code $code)." }
            }
        }
        else { throw "Unknown task kind '$($t.kind)'." }
    } catch {
        Report $id 'failed' 1 ([string]$_)
        Log "Task $id FAILED: $_"
    } finally {
        try { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue } catch {}
    }
    if ($selfUpdate) { break }
}

if (-not $Silent) {
    Write-Host ''
    Write-Host " AMPM Deploy Agent $agentVersion on $hostName - done." -ForegroundColor Cyan
    Write-Host " Log file: $logFile"
    Read-Host 'Press Enter to close'
}
