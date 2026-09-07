@echo off
title AMPM - PC Inventory Agent
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0AMPM_PC_Agent.ps1"
echo.
echo ================================================
echo  Upar jo bhi likha hai, wahi result hai.
echo  Agar error/red text dikha ho to screenshot ya
echo  "ampm_agent_log.txt" (isi folder mein) Claude ko bhejo.
echo ================================================
pause
