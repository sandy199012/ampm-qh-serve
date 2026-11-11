@echo off
cd /d "%~dp0"
git add -A > "%~dp0push_print2_log.txt" 2>&1
(
echo Print Log: show employee the PC is assigned to ^(column, filter, Excel^)
echo.
echo Co-Authored-By: Claude Sonnet 5.5 ^<noreply@anthropic.com^>
echo Claude-Session: https://claude.ai/code/session_01WYLdMPc2GDbGGib8nnTtwd
) > "%TEMP%\commitmsg_print2.tmp"
git commit -F "%TEMP%\commitmsg_print2.tmp" >> "%~dp0push_print2_log.txt" 2>&1
git push origin main >> "%~dp0push_print2_log.txt" 2>&1
echo FINISHED errorlevel=%errorlevel% >> "%~dp0push_print2_log.txt"
del "%TEMP%\commitmsg_print2.tmp" >nul 2>&1
