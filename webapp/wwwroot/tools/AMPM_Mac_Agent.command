#!/bin/bash
# Double-click: report this Mac to the AMPM IT Tool now.
cd "$(dirname "$0")" || exit 1
xattr -dr com.apple.quarantine . >/dev/null 2>&1
/bin/bash ./AMPM_Mac_Agent.sh
echo
echo "Log file: $(pwd)/ampm_agent_log.txt"
read -r -p "Press Enter to close"
