@echo off
cd /d "%~dp0"
git add -A > "%~dp0push_myassets_log.txt" 2>&1
git commit -m "My Helpdesk: show the employee their assigned assets" >> "%~dp0push_myassets_log.txt" 2>&1
git push origin main >> "%~dp0push_myassets_log.txt" 2>&1
echo FINISHED errorlevel=%errorlevel% >> "%~dp0push_myassets_log.txt"
