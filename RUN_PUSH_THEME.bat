@echo off
cd /d "%~dp0"
set AMPM_NOPAUSE=1
call "%~dp0PUSH_APP_UPDATE.bat" "Helpdesk app: new Aurora theme (violet + rose), rounded inputs, softer shadows" > "%~dp0push_theme_log.txt" 2>&1
echo FINISHED errorlevel=%errorlevel% >> "%~dp0push_theme_log.txt"
