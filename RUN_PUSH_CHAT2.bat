@echo off
cd /d "%~dp0"
set AMPM_NOPAUSE=1
call "%~dp0PUSH_APP_UPDATE.bat" "Chat: employee se employee chat (WhatsApp jaisa), file bhejo/download karo, chat history save, files 2 din baad auto-delete" > "%~dp0push_chat2_log.txt" 2>&1
echo FINISHED errorlevel=%errorlevel% >> "%~dp0push_chat2_log.txt"
