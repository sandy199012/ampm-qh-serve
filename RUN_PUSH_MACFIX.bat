@echo off
cd /d "%~dp0"
git add -A > "%~dp0push_macfix_log.txt" 2>&1
git commit -m "Mac Agent: Terminal one-line installer (avoids macOS Gatekeeper warning)" >> "%~dp0push_macfix_log.txt" 2>&1
git push origin main >> "%~dp0push_macfix_log.txt" 2>&1
echo FINISHED errorlevel=%errorlevel% >> "%~dp0push_macfix_log.txt"
