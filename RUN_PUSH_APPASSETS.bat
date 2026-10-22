@echo off
cd /d "%~dp0"
set AMPM_NOPAUSE=1
call "%~dp0PUSH_APP_UPDATE.bat" "Helpdesk app: Mere Assets section - employee ko apne assigned assets dikhte hain" > "%~dp0push_appassets_log.txt" 2>&1
echo FINISHED errorlevel=%errorlevel% >> "%~dp0push_appassets_log.txt"
