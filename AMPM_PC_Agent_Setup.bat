@echo off
title AMPM - PC Agent Setup

net session >nul 2>&1
if not "%errorlevel%"=="0" (
    echo.
    echo  [ERROR] Ye file ADMINISTRATOR se chalani padegi.
    echo  Is file par right-click karo aur "Run as administrator" choose karo.
    echo.
    pause
    exit /b 1
)

cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0AMPM_PC_Agent_Setup.ps1"
