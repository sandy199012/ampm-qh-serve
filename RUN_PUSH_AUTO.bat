@echo off
cd /d "%~dp0"
set AMPM_NOPAUSE=1
call "%~dp0PUSH_APP_UPDATE.bat" "Helpdesk app 1.1 - in-app updates, My Device, new look" > "%~dp0push_log.txt" 2>&1
echo FINISHED errorlevel=%errorlevel% >> "%~dp0push_log.txt"
