@echo off
title AMPM - Push dashboard fix
cd /d "%~dp0"
(
git add -A
git commit -m "Fix: Dashboard link no longer shows the login page for signed-in users" -m "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01WYLdMPc2GDbGGib8nnTtwd"
git push origin main
) > push_dash_log.txt 2>&1
type push_dash_log.txt
echo.
echo Finished. The website redeploys automatically in a few minutes.
pause
