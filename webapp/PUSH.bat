@echo off
title AMPM Web App - Push to GitHub
color 2F
cls

echo.
echo  ================================================
echo   AMPM Web App - GitHub Push
echo   Render auto deploy karega!
echo  ================================================
echo.

cd /d "D:\F111\AMPMWeb"

:: Check git init
if not exist ".git" (
    echo  First time setup...
    git init
    git branch -M main
    git remote add origin https://github.com/sandy199012/ampm-it-web.git
)

git add .
set MSG=Update %date% %time%
git commit -m "%MSG%"
git push -u origin main

if %errorlevel% neq 0 (
    echo [FAILED] Push nahi hua!
    pause & exit /b 1
)

echo.
echo  ================================================
echo   DONE! Render 3-5 min mein deploy karega.
echo   Web App: https://ampm-it-web.onrender.com
echo  ================================================
pause
EOF
