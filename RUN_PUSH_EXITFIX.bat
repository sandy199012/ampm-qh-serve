@echo off
cd /d "%~dp0"
git add -A > "%~dp0push_exitfix_log.txt" 2>&1
git commit -m "Exit employee: auto-unassign their assets; button to free assets of already-exited employees" >> "%~dp0push_exitfix_log.txt" 2>&1
git push origin main >> "%~dp0push_exitfix_log.txt" 2>&1
echo FINISHED errorlevel=%errorlevel% >> "%~dp0push_exitfix_log.txt"
