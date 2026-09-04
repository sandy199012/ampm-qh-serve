@echo off
title AMPM - Push to GitHub
color 2F
cls

echo.
echo  ================================================
echo   AMPM IT System - GitHub Push
echo   Both services auto deploy on Render!
echo  ================================================
echo.

cd /d "D:\F111\deploy_package"

git add .
set MSG=Update %date% %time%
git commit -m "%MSG%"
git push origin main

if %errorlevel% neq 0 (
    echo.
    echo [FAILED] Push nahi hua!
    echo Internet check karo ya credentials verify karo.
    pause
    exit /b 1
)

echo.
echo  ================================================
echo   PUSHED! Render 3-5 min mein deploy karega.
echo.
echo   QH Server: https://ampm-qh-serve.onrender.com
echo   Web App:   https://ampm-it-web.onrender.com
echo  ================================================
echo.
pause
