@echo off
title AMPM - GitHub Setup
color 1F
cls

echo.
echo  ================================================
echo   AMPM Fashions - GitHub Repository Setup
echo   Ek baar hi run karna hai!
echo  ================================================
echo.

:: Check git
where git >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] Git not found!
    pause & exit /b 1
)

:: Set folder - QH Server folder mein se run karo
cd /d "%~dp0"

echo  [1/5] Git repo initialize kar raha hai...
git init
git branch -M main

echo.
echo  [2/5] GitHub se connect kar raha hai...
git remote remove origin 2>nul
git remote add origin https://github.com/sandy199012/ampm-qh-server.git

echo.
echo  [3/5] Saari files add kar raha hai...
git add .
git commit -m "Initial: AMPM QH Monitor Server v3.0"

echo.
echo  [4/5] GitHub pe push kar raha hai...
echo  (Browser mein GitHub login maangega - allow karo)
echo.
git push -u origin main

if %errorlevel% neq 0 (
    echo.
    echo  [FAILED] Push nahi hua!
    echo.
    echo  Fix karo:
    echo  1. github.com pe jaao
    echo  2. New repository banao: ampm-qh-server
    echo  3. "Public" rakho
    echo  4. README mat add karo
    echo  5. Create Repository click karo
    echo  6. Phir ye bat dobara run karo
    echo.
    pause
    exit /b 1
)

echo.
echo  ================================================
echo   SETUP COMPLETE!
echo  ================================================
echo.
echo   Repository: https://github.com/sandy199012/ampm-qh-server
echo.
echo   Ab PUSH.bat se updates bhejo!
echo  ================================================
echo.
pause
