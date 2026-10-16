@echo off
cd /d "%~dp0"
git add -A > "%~dp0push_web_log.txt" 2>&1
git commit -m "Update feed: fall back to releases.atom when GitHub API is rate-limited" >> "%~dp0push_web_log.txt" 2>&1
git push origin main >> "%~dp0push_web_log.txt" 2>&1
echo FINISHED errorlevel=%errorlevel% >> "%~dp0push_web_log.txt"
