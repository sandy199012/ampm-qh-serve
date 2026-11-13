@echo off
title AMPM - Send Helpdesk app update
cd /d "%~dp0"

rem The app source folder (code changes are made here):
set "APPSRC=D:\F111\AMPMTool_Final\AMPMHelpdeskApp"

where git >nul 2>nul
if errorlevel 1 (
  echo [ERROR] Git is not installed. Install it from https://git-scm.com
  if not defined AMPM_NOPAUSE pause
  exit /b 1
)
if not exist "%APPSRC%\pubspec.yaml" (
  echo [ERROR] App folder not found: %APPSRC%
  echo Open this file in Notepad and correct the APPSRC path near the top.
  if not defined AMPM_NOPAUSE pause
  exit /b 1
)

set "MSG=%~1"
if "%MSG%"=="" (
  echo.
  echo  Write a short update note - it is shown to users in "New update available".
  echo  ^(For a required update, start the note with [mandatory].^)
  echo.
  set /p MSG=Note: 
)
if "%MSG%"=="" set "MSG=Improvements and bug fixes"

echo.
echo [1/3] Copying the app code to the mobile_app folder...
robocopy "%APPSRC%" "%~dp0mobile_app" /MIR /NFL /NDL /NJH /NJS /NP /XD build .dart_tool .gradle .kotlin .idea "{lib" GITHUB_WORKFLOW /XF *.apk build_log.txt local.properties .flutter-plugins .flutter-plugins-dependencies BUILD_APK.bat SETUP_AUTO_UPDATE.bat SETUP_GITHUB.bat PUSH_UPDATE.bat COPY_SIGNING_KEY.bat SETUP_UPDATES.txt SETUP_AND_BUILD.bat >nul
if errorlevel 8 (
  echo [ERROR] Copy failed.
  if not defined AMPM_NOPAUSE pause
  exit /b 1
)

echo [2/3] Preparing the GitHub workflow files...
if not exist ".github\workflows" mkdir ".github\workflows"
copy /Y "APP_UPDATE_FILES\build-app.yml" ".github\workflows\build-app.yml" >nul
copy /Y "APP_UPDATE_FILES\deploy.yml" ".github\workflows\deploy.yml" >nul

echo [3/3] Pushing to GitHub...
git add -A
git commit -m "%MSG%"
git push origin main
if errorlevel 1 (
  echo.
  echo [ERROR] git push failed - see the message above.
  if not defined AMPM_NOPAUSE pause
  exit /b 1
)

echo.
echo  ============================================================
echo   Pushed! The new APK will be built and released in about 8-12 minutes.
echo   After that, every phone will show "New update available" when the app is opened.
echo   Progress: https://github.com/sandy199012/ampm-qh-serve/actions
echo   APK link (for new phones): https://ampm-qh-serve-1.onrender.com/app
echo  ============================================================
echo.
if not defined AMPM_NOPAUSE pause
