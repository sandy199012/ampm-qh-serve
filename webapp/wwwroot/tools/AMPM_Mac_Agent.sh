#!/bin/bash
# AMPM IT Tool - Mac Inventory Agent
# Collects this Mac's hostname/IP/MAC/OS/hardware/installed apps and any USB
# printers set up on it, and pushes them to the live AMPM IT Tool website
# (PC Inventory + Asset Stock) - same data the Windows PC Agent sends.
#
# This file is downloaded from the website (Asset Stock -> Agents). The access
# key is put into it at download time, so download it again whenever the key
# changes or a new version is published.
#
#   AMPM_Mac_Agent.command        -> report this Mac now (window stays open)
#   AMPM_Mac_Agent_Setup.command  -> (run once) report automatically at every
#                                    restart and every 10 minutes
#   --silent                      -> no console output (used by the scheduled job)

SERVER_URL='https://ampm-qh-serve-1.onrender.com/api/endpoints/report-pc'
AGENT_KEY='__AMPM_AGENT_KEY__'   # filled in automatically when you download this from the website

SILENT=0
[ "$1" = "--silent" ] && SILENT=1
export LC_ALL=C
PATH="$PATH:/usr/sbin:/usr/bin:/bin:/sbin"; export PATH

SELF_DIR="$(cd "$(dirname "$0")" && pwd)"
LOG_FILE="$SELF_DIR/ampm_agent_log.txt"

say() { [ "$SILENT" = "1" ] || printf '%s\n' "$*"; }
log() { printf '%s  %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$*" >> "$LOG_FILE" 2>/dev/null; say "$*"; }

# JSON-escape a string (backslash, quotes, control characters)
jesc() {
    printf '%s' "$1" | tr '\t\r' '  ' | tr -d '\000-\010\013\014\016-\037' \
      | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' | awk 'BEGIN{ORS=""} {print (NR>1?"\\n":"") $0}'
}

case "$AGENT_KEY" in
    __AMPM*)
        echo
        echo " This copy has no access key."
        echo " Please download it again from the website:"
        echo "   Asset Stock -> Agents -> Download Mac Agent"
        exit 1 ;;
esac

# ---- network: IP + MAC of the adapter that carries the default route ----------
IFACE="$(route -n get default 2>/dev/null | awk '/interface:/{print $2; exit}')"
IP=""; MAC=""
if [ -n "$IFACE" ]; then
    IP="$(ipconfig getifaddr "$IFACE" 2>/dev/null)"
    MAC="$(ifconfig "$IFACE" 2>/dev/null | awk '/ether /{print $2; exit}')"
fi
if [ -z "$IP" ]; then
    for i in en0 en1 en2 en3 en4 en5; do
        v="$(ipconfig getifaddr "$i" 2>/dev/null)"
        if [ -n "$v" ]; then IP="$v"; IFACE="$i"; MAC="$(ifconfig "$i" 2>/dev/null | awk '/ether /{print $2; exit}')"; break; fi
    done
fi
[ -z "$IP" ] && IP="Unknown"
# Same look as the Windows agent: AA-BB-CC-DD-EE-FF
MAC="$(printf '%s' "$MAC" | tr 'a-f:' 'A-F-')"

# ---- computer name -------------------------------------------------------------
HOST="$(scutil --get LocalHostName 2>/dev/null)"
[ -z "$HOST" ] && HOST="$(hostname -s 2>/dev/null)"
[ -z "$HOST" ] && HOST="$(hostname 2>/dev/null)"

# ---- OS / hardware ---------------------------------------------------------------
OS_NAME="$(sw_vers -productName 2>/dev/null)"; [ -z "$OS_NAME" ] && OS_NAME="macOS"
OS_VER="$(sw_vers -productVersion 2>/dev/null)"
OS_BUILD_NO="$(sw_vers -buildVersion 2>/dev/null)"
OS="$OS_NAME $OS_VER"
OS_BUILD="$OS_VER (Build $OS_BUILD_NO)"
ARCH="$(uname -m 2>/dev/null)"
[ "$ARCH" = "arm64" ] && ARCH="arm64 (Apple Silicon)"

HW="$(system_profiler SPHardwareDataType 2>/dev/null)"
MODEL_NAME="$(printf '%s\n' "$HW" | awk -F': ' '/Model Name:/{print $2; exit}')"
MODEL_ID="$(printf '%s\n' "$HW" | awk -F': ' '/Model Identifier:/{print $2; exit}')"
MODEL="$MODEL_NAME"
[ -n "$MODEL_ID" ] && MODEL="$MODEL_NAME ($MODEL_ID)"
[ -z "$MODEL" ] && MODEL="$(sysctl -n hw.model 2>/dev/null)"
MANUFACTURER="Apple"

SERIAL="$(ioreg -l 2>/dev/null | awk -F'"' '/IOPlatformSerialNumber/{print $4; exit}')"
[ -z "$SERIAL" ] && SERIAL="$(printf '%s\n' "$HW" | awk -F': ' '/Serial Number/{print $2; exit}')"
[ -z "$SERIAL" ] && SERIAL="Not available"

CPU="$(sysctl -n machdep.cpu.brand_string 2>/dev/null)"
if [ -z "$CPU" ]; then
    CPU="$(printf '%s\n' "$HW" | awk -F': ' '/Chip:|Processor Name:/{print $2; exit}')"
fi
RAM_GB="$(sysctl -n hw.memsize 2>/dev/null | awk '{printf "%.1f", $1/1073741824}')"

# Disk: on modern macOS the user data lives on the Data volume of the same APFS container
DFV="/System/Volumes/Data"; [ -d "$DFV" ] || DFV="/"
read -r DISK_TOTAL DISK_FREE <<EOF2
$(df -k "$DFV" 2>/dev/null | awk 'NR==2{printf "%.1f %.1f", $2/1048576, $4/1048576}')
EOF2

# The scheduled job runs as root, so use whoever is actually signed in at the screen
LOGGED_USER="$(stat -f%Su /dev/console 2>/dev/null)"
case "$LOGGED_USER" in root|_mbsetupuser|loginwindow) LOGGED_USER="" ;; esac
[ -z "$LOGGED_USER" ] && [ "$(id -u)" != "0" ] && LOGGED_USER="$(id -un)"

# ---- installed applications (name + version) -------------------------------------
SW_JSON=""; SW_COUNT=0
add_app() {
    local app="$1" name ver plist
    plist="$app/Contents/Info"
    [ -f "$plist.plist" ] || return
    name="$(defaults read "$plist" CFBundleDisplayName 2>/dev/null)"
    [ -z "$name" ] && name="$(defaults read "$plist" CFBundleName 2>/dev/null)"
    [ -z "$name" ] && name="$(basename "$app" .app)"
    ver="$(defaults read "$plist" CFBundleShortVersionString 2>/dev/null)"
    [ -z "$ver" ] && ver="$(defaults read "$plist" CFBundleVersion 2>/dev/null)"
    [ -n "$SW_JSON" ] && SW_JSON="$SW_JSON,"
    SW_JSON="$SW_JSON{\"name\":\"$(jesc "$name")\",\"version\":\"$(jesc "$ver")\"}"
    SW_COUNT=$((SW_COUNT+1))
}
while IFS= read -r app; do
    [ -n "$app" ] && add_app "$app"
done <<EOF3
$( { find /Applications -maxdepth 2 -name '*.app' -prune 2>/dev/null; } | sort -u )
EOF3

# ---- USB printers set up on this Mac (CUPS) ------------------------------------------
PR_JSON=""; PR_COUNT=0; PR_SEEN=""
while IFS= read -r line; do
    # device for NAME: usb://Brand/Model?serial=XYZ
    case "$line" in "device for "*) ;; *) continue ;; esac
    pname="${line#device for }"; pname="${pname%%: *}"
    uri="${line#*: }"
    PR_SEEN="$PR_SEEN$pname [${uri%%\?*}]; "
    case "$uri" in usb:*) ;; *) continue ;; esac
    case "$pname" in *PDF*|*Fax*|*fax*) continue ;; esac
    pserial=""
    case "$uri" in *serial=*) pserial="${uri##*serial=}"; pserial="${pserial%%&*}" ;; esac
    off="false"
    lpstat -p "$pname" 2>/dev/null | grep -qi "disabled" && off="true"
    [ -n "$PR_JSON" ] && PR_JSON="$PR_JSON,"
    PR_JSON="$PR_JSON{\"name\":\"$(jesc "$(printf '%s' "$pname" | tr '_' ' ')")\",\"driver\":\"\",\"port\":\"$(jesc "${uri%%\?*}")\",\"offline\":$off,\"serial\":\"$(jesc "$pserial")\"}"
    PR_COUNT=$((PR_COUNT+1))
done <<EOF4
$(lpstat -v 2>/dev/null)
EOF4
log "Printers seen by macOS: ${PR_SEEN:-none} | USB: $PR_COUNT"

# ---- build + send -----------------------------------------------------------------------
PAYLOAD="{\"key\":\"$(jesc "$AGENT_KEY")\",\"hostname\":\"$(jesc "$HOST")\",\"ip\":\"$(jesc "$IP")\",\"mac\":\"$(jesc "$MAC")\",\"os\":\"$(jesc "$OS")\",\"osBuild\":\"$(jesc "$OS_BUILD")\",\"arch\":\"$(jesc "$ARCH")\",\"manufacturer\":\"$MANUFACTURER\",\"model\":\"$(jesc "$MODEL")\",\"serial\":\"$(jesc "$SERIAL")\",\"cpu\":\"$(jesc "$CPU")\",\"ramGb\":\"$RAM_GB\",\"diskFree\":\"$DISK_FREE\",\"diskTotal\":\"$DISK_TOTAL\",\"user\":\"$(jesc "$LOGGED_USER")\",\"software\":[$SW_JSON],\"usbPrinters\":[$PR_JSON]}"

if [ "$SILENT" = "0" ]; then
    echo
    echo " AMPM Mac Inventory Agent"
    echo " ------------------------"
    echo " Hostname     : $HOST"
    echo " IP           : $IP"
    echo " MAC          : $MAC"
    echo " OS           : $OS"
    echo " OS Build     : $OS_BUILD"
    echo " Architecture : $ARCH"
    echo " Manufacturer : $MANUFACTURER"
    echo " Model        : $MODEL"
    echo " Serial No.   : $SERIAL"
    echo " CPU          : $CPU"
    echo " RAM          : $RAM_GB GB"
    echo " Disk Free    : $DISK_FREE GB"
    echo " Disk Total   : $DISK_TOTAL GB"
    echo " User         : $LOGGED_USER"
    echo " Software     : $SW_COUNT apps found"
    echo " USB Printers : $PR_COUNT found"
    echo
fi

if [ -z "$HOST" ]; then log "FAILED - could not read the computer name"; exit 1; fi

TMP="$(mktemp -t ampm_mac_payload 2>/dev/null || echo /tmp/ampm_mac_payload.$$)"
printf '%s' "$PAYLOAD" > "$TMP"
RESP="$(curl -sS -m 30 -X POST -H 'Content-Type: application/json' --data-binary @"$TMP" "$SERVER_URL" 2>&1)"
RC=$?
rm -f "$TMP"
if [ $RC -eq 0 ] && printf '%s' "$RESP" | grep -q '"ok":[ ]*true'; then
    log "OK - sent (serial=$SERIAL, $SW_COUNT app entries, $PR_COUNT USB printer(s))"
    say " DONE - sent to the website's PC Inventory."
    exit 0
elif [ $RC -eq 0 ]; then
    log "Server rejected: $RESP"
    say " Server rejected the request (wrong or old key? download this agent again from the website)."
    exit 2
else
    log "FAILED - $RESP"
    say " FAILED - check your internet connection. Error: $RESP"
    exit 3
fi
