@echo off
cd /d "D:\F111\deploy_package"
git add -A > push_dbtransfer_log.txt 2>&1
git commit -m "Add superadmin Database Transfer page: copy the whole database to another Supabase project" -m "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01WYLdMPc2GDbGGib8nnTtwd" >> push_dbtransfer_log.txt 2>&1
git push origin main >> push_dbtransfer_log.txt 2>&1
type push_dbtransfer_log.txt
timeout /t 8 >nul
