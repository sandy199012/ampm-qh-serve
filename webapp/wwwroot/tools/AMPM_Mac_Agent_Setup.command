#!/bin/bash
# Double-click ONCE: makes this Mac report itself at every restart and every 10 minutes.
SELF="$(cd "$(dirname "$0")" && pwd)/$(basename "$0")"
cd "$(dirname "$0")" || exit 1
SRC="$(pwd)/AMPM_Mac_Agent.sh"
xattr -dr com.apple.quarantine . >/dev/null 2>&1

if [ "$(id -u)" -ne 0 ]; then
    echo
    echo " Administrator password is needed once (to install the automatic job)."
    echo
    exec sudo /bin/bash "$SELF"
fi

LABEL="com.ampm.pcagent"
DEST_DIR="/Library/Application Support/AMPM/PcAgent"
DEST="$DEST_DIR/AMPM_Mac_Agent.sh"
PLIST="/Library/LaunchDaemons/$LABEL.plist"

mkdir -p "$DEST_DIR" || { echo " INSTALL FAILED"; read -r -p "Press Enter to close"; exit 1; }
cp -f "$SRC" "$DEST" && chmod 755 "$DEST" && chown root:wheel "$DEST"

cat > "$PLIST" <<PLISTEOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key><string>$LABEL</string>
    <key>ProgramArguments</key>
    <array>
        <string>/bin/bash</string>
        <string>$DEST</string>
        <string>--silent</string>
    </array>
    <key>RunAtLoad</key><true/>
    <key>StartInterval</key><integer>600</integer>
    <key>StandardOutPath</key><string>/var/log/ampm_pcagent.out</string>
    <key>StandardErrorPath</key><string>/var/log/ampm_pcagent.out</string>
</dict>
</plist>
PLISTEOF
chown root:wheel "$PLIST"; chmod 644 "$PLIST"
[ -f "$PLIST" ] || { echo " INSTALL FAILED - could not write $PLIST"; exit 1; }

launchctl bootout system "$PLIST" >/dev/null 2>&1
if launchctl bootstrap system "$PLIST" 2>/dev/null || launchctl load -w "$PLIST" 2>/dev/null; then
    echo
    echo " DONE - '$LABEL' installed (reports at start-up and every 10 minutes)."
    echo " Installed copy : $DEST"
    echo " Log file       : $DEST_DIR/ampm_agent_log.txt"
    echo " Reporting once now..."
    echo
    /bin/bash "$DEST"
else
    echo " INSTALL FAILED - could not start the job."
fi
echo
read -r -p "Press Enter to close"
