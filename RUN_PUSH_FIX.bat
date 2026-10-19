@echo off
cd /d "%~dp0"
git add -A > "%~dp0push_fix_log.txt" 2>&1
git commit -m "Fix Assets 500: restore status filter in controller, make view null-safe" >> "%~dp0push_fix_log.txt" 2>&1
git push origin main >> "%~dp0push_fix_log.txt" 2>&1
echo FINISHED errorlevel=%errorlevel% >> "%~dp0push_fix_log.txt"
