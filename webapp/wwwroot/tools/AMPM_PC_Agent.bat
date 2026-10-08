@echo off
title AMPM - PC Inventory Agent
cd /d "%~dp0"
rem Files downloaded from a browser/ZIP are "blocked" by Windows - unblock them so PowerShell will run them.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -LiteralPath '%~dp0' -File | Unblock-File" >nul 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0AMPM_PC_Agent.ps1"
