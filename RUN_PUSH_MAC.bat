@echo off
cd /d "%~dp0"
git add -A > "%~dp0push_mac_log.txt" 2>&1
git commit -m "Agents: add Mac Agent (macOS PC inventory) download" >> "%~dp0push_mac_log.txt" 2>&1
git push origin main >> "%~dp0push_mac_log.txt" 2>&1
echo FINISHED errorlevel=%errorlevel% >> "%~dp0push_mac_log.txt"
