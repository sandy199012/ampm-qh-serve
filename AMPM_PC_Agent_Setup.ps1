$logFile = Join-Path $PSScriptRoot 'ampm_setup_log.txt'
function Log($msg) {
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $msg"
    Add-Content -Path $logFile -Value $line
    Write-Host $msg
}

try {
    $scriptPath = Join-Path $PSScriptRoot 'AMPM_PC_Agent.ps1'
    $taskName   = 'AMPM PC Inventory Agent'

    if (-not (Test-Path $scriptPath)) {
        throw "AMPM_PC_Agent.ps1 not found at '$scriptPath' - both files (Setup + Agent) must be in the same folder."
    }

    Log "Registering scheduled task '$taskName'..."

    $action = New-ScheduledTaskAction -Execute 'powershell.exe' `
        -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$scriptPath`" -Silent"

    $triggerStartup = New-ScheduledTaskTrigger -AtStartup
    $triggerRepeat  = New-ScheduledTaskTrigger -Once -At (Get-Date) `
        -RepetitionInterval (New-TimeSpan -Minutes 10) -RepetitionDuration ([TimeSpan]::MaxValue)

    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -StartWhenAvailable -MultipleInstances IgnoreNew

    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue

    Register-ScheduledTask -TaskName $taskName -Action $action `
        -Trigger @($triggerStartup, $triggerRepeat) -Principal $principal -Settings $settings `
        -Description 'Reports this PC (hostname/IP/OS/CPU/RAM/disk/installed software) to the AMPM IT Tool website - at every restart and every 10 minutes.' | Out-Null

    Log "DONE - '$taskName' scheduled task installed."
    Write-Host ""
    Write-Host " This PC's data will now be sent automatically:" -ForegroundColor Green
    Write-Host "   - On every system restart"
    Write-Host "   - Every 10 minutes"
    Write-Host ""
    Write-Host " You can also run AMPM_PC_Agent.bat any time to test it immediately."

    # Also run it once right now, so Sandy sees a result immediately instead
    # of waiting up to 10 minutes for the first automatic run.
    Write-Host ""
    Write-Host " Running a first test now..."
    & powershell -NoProfile -ExecutionPolicy Bypass -File "$scriptPath" -Silent
    Log "First manual test run triggered."
}
catch {
    Log "SETUP FAILED: $($_.Exception.Message)"
    Write-Host ""
    Write-Host " SETUP FAILED - send this full error to Claude:" -ForegroundColor Red
    Write-Host " $_" -ForegroundColor Red
    Write-Host ""
    Write-Host " Common cause: this file was not run as Administrator (right-click - Run as administrator)."
}

Write-Host ""
Write-Host "Log file: $logFile"
Read-Host "Press Enter to close"
