@echo off
title AMPM - PC Agent Setup

net session >nul 2>&1
if not "%errorlevel%"=="0" (
    echo.
    echo  [ERROR] This file must be run as ADMINISTRATOR.
    echo  Right-click this file and choose "Run as administrator".
    echo.
    pause
    exit /b 1
)

cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0AMPM_PC_Agent_Setup.ps1"
echo.
echo ================================================
echo  Whatever is printed above is the result.
echo  If you see an error/red text, send a screenshot
echo  or "ampm_setup_log.txt" (in this same folder) to Claude.
echo ================================================
pause
