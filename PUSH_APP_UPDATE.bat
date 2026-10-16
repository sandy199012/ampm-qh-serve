@echo off
title AMPM - Helpdesk app update bhejo
cd /d "%~dp0"

rem App ka source yahan hai (isi folder me tum code badalte ho / Claude badalta hai):
set "APPSRC=D:\F111\AMPMTool_Final\AMPMHelpdeskApp"

where git >nul 2>nul
if errorlevel 1 (
  echo [ERROR] git installed nahi hai. https://git-scm.com se install karo.
  if not defined AMPM_NOPAUSE pause
  exit /b 1
)
if not exist "%APPSRC%\pubspec.yaml" (
  echo [ERROR] App folder nahi mila: %APPSRC%
  echo Is file ko Notepad me kholo aur 3rd line ka APPSRC sahi karo.
  if not defined AMPM_NOPAUSE pause
  exit /b 1
)

set "MSG=%~1"
if "%MSG%"=="" (
  echo.
  echo  Update ka chhota note likho - ye users ko "Naya update available" me dikhega.
  echo  ^(Zaroori update ke liye note ke shuru me [mandatory] likho.^)
  echo.
  set /p MSG=Note: 
)
if "%MSG%"=="" set "MSG=Improvements and bug fixes"

echo.
echo [1/3] App ka code mobile_app folder me copy ho raha hai...
robocopy "%APPSRC%" "%~dp0mobile_app" /MIR /NFL /NDL /NJH /NJS /NP /XD build .dart_tool .gradle .kotlin .idea "{lib" GITHUB_WORKFLOW /XF *.apk build_log.txt local.properties .flutter-plugins .flutter-plugins-dependencies BUILD_APK.bat SETUP_AUTO_UPDATE.bat SETUP_GITHUB.bat PUSH_UPDATE.bat COPY_SIGNING_KEY.bat SETUP_UPDATES.txt SETUP_AND_BUILD.bat >nul
if errorlevel 8 (
  echo [ERROR] Copy fail hua.
  if not defined AMPM_NOPAUSE pause
  exit /b 1
)

echo [2/3] GitHub workflow files theek kar raha hoon...
if not exist ".github\workflows" mkdir ".github\workflows"
copy /Y "APP_UPDATE_FILES\build-app.yml" ".github\workflows\build-app.yml" >nul
copy /Y "APP_UPDATE_FILES\deploy.yml" ".github\workflows\deploy.yml" >nul

echo [3/3] GitHub par bhej raha hoon...
git add -A
git commit -m "%MSG%"
git push origin main
if errorlevel 1 (
  echo.
  echo [ERROR] git push fail hua - upar ka message dekho.
  if not defined AMPM_NOPAUSE pause
  exit /b 1
)

echo.
echo  ============================================================
echo   Bhej diya! 8-12 minute me naya APK ban ke release ho jayega.
echo   Phir sabke phone me app kholte hi "Naya update available" aayega.
echo   Progress: https://github.com/sandy199012/ampm-qh-serve/actions
echo   APK link (naye phone ke liye): https://ampm-qh-serve-1.onrender.com/app
echo  ============================================================
echo.
if not defined AMPM_NOPAUSE pause
