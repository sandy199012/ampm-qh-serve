@echo off
title AMPM - Helpdesk app auto-update SETUP (sirf ek baar)
cd /d "%~dp0"
echo.
echo  Ye sirf EK BAAR chalana hai. Iske baad har update ke liye
echo  sirf PUSH_APP_UPDATE.bat chalana hoga.
echo.

set "KS=%USERPROFILE%\.android\debug.keystore"
if not exist "%KS%" (
  echo [ERROR] Signing key nahi mili: %KS%
  echo Ye wahi PC chahiye jahan se tum app ka APK build karte ho ^(BUILD_APK.bat^).
  echo Agar app kisi aur PC par bana tha to wahan se ye file yahan copy karo.
  pause
  exit /b 1
)

powershell -NoProfile -Command "[Convert]::ToBase64String([IO.File]::ReadAllBytes('%KS%')) | Set-Clipboard"
echo  Signing key clipboard me copy ho gayi.
echo.
echo  Ab browser me GitHub ka secret page khulega. Wahan:
echo     Name   :  DEBUG_KEYSTORE_B64
echo     Secret :  Ctrl+V  ^(jo abhi copy hua^)
echo     Phir "Add secret" dabao.
echo  ^(Ye key kisi ko mat bhejna - isi se app signed hai.^)
echo.
start "" "https://github.com/sandy199012/ampm-qh-serve/settings/secrets/actions/new"
echo  Secret add karne ke baad yahan koi bhi key dabao...
pause >nul

call "%~dp0PUSH_APP_UPDATE.bat" "Helpdesk app 1.1 - in-app updates, My Device, new look"
