@echo off
cd /d "%~dp0"
git add -A > "%~dp0push_sort_log.txt" 2>&1
git commit -m "Assets: sort by asset tag (AMPM-001, AMPM-002...)" >> "%~dp0push_sort_log.txt" 2>&1
git push origin main >> "%~dp0push_sort_log.txt" 2>&1
echo FINISHED errorlevel=%errorlevel% >> "%~dp0push_sort_log.txt"
