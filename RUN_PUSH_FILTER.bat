@echo off
cd /d "%~dp0"
git add -A > "%~dp0push_filter_log.txt" 2>&1
git commit -m "Assets: add Assigned/Unassigned status filter and clickable stat cards" >> "%~dp0push_filter_log.txt" 2>&1
git push origin main >> "%~dp0push_filter_log.txt" 2>&1
echo FINISHED errorlevel=%errorlevel% >> "%~dp0push_filter_log.txt"
