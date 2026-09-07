$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot 'AMPM_PC_Agent.ps1'
$taskName   = 'AMPM PC Inventory Agent'

if (-not (Test-Path $scriptPath)) {
    Write-Host "ERROR: AMPM_PC_Agent.ps1 nahi mila - dono files (Setup + Agent) same folder mein hone chahiye." -ForegroundColor Red
    exit 1
}

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

Write-Host ""
Write-Host " DONE - '$taskName' scheduled task install ho gaya." -ForegroundColor Green
Write-Host " Ab is PC ka data automatically bhejta rahega:"
Write-Host "   - Har system restart pe"
Write-Host "   - Har 10 minute mein"
Write-Host ""
Write-Host " Turant test karne ke liye ab AMPM_PC_Agent.bat bhi chala sakte ho."
Write-Host ""
Read-Host "Enter dabao band karne ke liye"
