@echo off
title AMPM - PC Inventory Agent
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0AMPM_PC_Agent.ps1"
echo.
echo ================================================
echo  Whatever is printed above is the result.
echo  If you see an error/red text, send a screenshot
echo  or "ampm_agent_log.txt" (in this same folder) to Claude.
echo ================================================
pause
