@echo off
title AMPM - Push /get page
cd /d "%~dp0"
(
git add -A
git commit -m "Add public /get page (Android APK, iPhone and desktop install) and installable web app manifest" -m "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01WYLdMPc2GDbGGib8nnTtwd"
git push origin main
) > push_get_log.txt 2>&1
type push_get_log.txt
echo.
echo Finished. The website redeploys automatically in a few minutes.
pause
