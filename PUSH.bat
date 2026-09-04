@echo off
title AMPM - Push to GitHub + Render
color 2F
cls

echo.
echo  ================================================
echo   AMPM - Code Push to GitHub
echo   Render automatically deploy karega!
echo  ================================================
echo.

cd /d "%~dp0"

:: Check karo ki kuch change hua hai
git diff --quiet && git diff --staged --quiet
if %errorlevel% equ 0 (
    echo  Koi change nahi hua - kuch bhi push nahi hoga.
    echo.
    pause
    exit /b 0
)

:: Commit message - date/time automatic
set MSG=Update: %date% %time%

echo  [1/3] Changes add kar raha hai...
git add .

echo  [2/3] Commit kar raha hai: %MSG%
git commit -m "%MSG%"

echo  [3/3] GitHub pe push kar raha hai...
git push origin main

if %errorlevel% neq 0 (
    echo.
    echo  [FAILED] Push nahi hua!
    echo  Internet check karo ya SETUP_GITHUB.bat dobara run karo.
    pause
    exit /b 1
)

echo.
echo  ================================================
echo   PUSH SUCCESSFUL!
echo  ================================================
echo.
echo   GitHub updated!
echo   Render automatically deploy karega - 2-3 min wait karo.
echo.
echo   Server URL:
echo   https://ampm-qh-server.onrender.com
echo.
echo  ================================================
echo.
pause
