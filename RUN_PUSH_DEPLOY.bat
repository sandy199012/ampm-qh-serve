@echo off
title AMPM - Push Software Deploy
cd /d "%~dp0"
(
git add -A
git commit -m "Add Software Deploy: push installers (.exe/.msi/.pkg) and agent updates to Windows/Mac computers from the website" -m "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01WYLdMPc2GDbGGib8nnTtwd"
git push origin main
) > push_deploy_log.txt 2>&1
type push_deploy_log.txt
echo.
echo Finished. The website redeploys automatically in a few minutes.
pause
