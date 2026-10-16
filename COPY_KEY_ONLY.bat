@echo off
set "KS=%USERPROFILE%\.android\debug.keystore"
if not exist "%KS%" (
  echo Signing key nahi mili: %KS%
  pause
  exit /b 1
)
powershell -NoProfile -Command "[Convert]::ToBase64String([IO.File]::ReadAllBytes('%KS%')) | Set-Clipboard"
echo Signing key clipboard me copy ho gayi. Ab GitHub secret box me Ctrl+V karo.
timeout /t 4 >nul
