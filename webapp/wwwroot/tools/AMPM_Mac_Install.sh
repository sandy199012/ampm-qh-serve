#!/bin/bash
# AMPM IT Tool - Mac Agent installer (served by the website, run from Terminal).
# Installs the Mac Agent so this Mac reports itself to the AMPM IT Tool at every
# restart and every 10 minutes. Nothing is downloaded as a file through the
# browser, so macOS Gatekeeper has nothing to block.
KEY='__AMPM_AGENT_KEY__'
BASE='__AMPM_BASE_URL__'

case "$KEY" in __AMPM*) echo " This installer has no access key - copy the command again from the website (Asset Stock -> Agents)."; exit 1 ;; esac
if [ "$(id -u)" -ne 0 ]; then
    echo " Please run it with sudo - copy the full command again from the website."
    exit 1
fi

LABEL="com.ampm.pcagent"
DEST_DIR="/Library/Application Support/AMPM/PcAgent"
DEST="$DEST_DIR/AMPM_Mac_Agent.sh"
PLIST="/Library/LaunchDaemons/$LABEL.plist"

mkdir -p "$DEST_DIR" || { echo " INSTALL FAILED - cannot create $DEST_DIR"; exit 1; }
echo " Downloading the agent..."
if ! curl -fsSL -G --data-urlencode "key=$KEY" --data-urlencode "file=agent" "$BASE/api/endpoints/mac-install" -o "$DEST.tmp"; then
    echo " INSTALL FAILED - could not download the agent (check the internet connection / key)."
    rm -f "$DEST.tmp"
    exit 1
fi
mv -f "$DEST.tmp" "$DEST" && chmod 755 "$DEST" && chown root:wheel "$DEST"
xattr -c "$DEST" >/dev/null 2>&1

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
    echo " INSTALL FAILED - could not start the background job."
    exit 1
fi
