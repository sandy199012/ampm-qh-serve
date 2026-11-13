@echo off
title AMPM - Push English text update
cd /d "%~dp0"
set AMPM_NOPAUSE=1
call PUSH_APP_UPDATE.bat "All screens, buttons and messages are now in English" > push_english_log.txt 2>&1
type push_english_log.txt
echo.
echo Finished. The website redeploys automatically; the new app build takes about 10 minutes.
pause
