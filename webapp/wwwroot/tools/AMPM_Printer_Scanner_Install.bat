@echo off
title AMPM - Printer Scanner - Install daily scan

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
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -LiteralPath '%~dp0' -File | Unblock-File" >nul 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0AMPM_Printer_Scanner.ps1" -Install
