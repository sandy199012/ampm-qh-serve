#!/bin/bash
# AMPM IT Tool - Mac Deploy Agent
# Lets the AMPM IT Tool website install software (.pkg) and update the Mac agents on this Mac.
# Every 5 minutes it asks the website "is anything waiting for me?", downloads it, checks the
# SHA-256, runs it as root (launchd daemon) and reports the result back.
#
# This file is served by the website with the access key filled in (Asset Stock -> Agents).
#   --silent   -> no console output (used by the launchd job)

BASE='__AMPM_BASE_URL__'
KEY='__AMPM_AGENT_KEY__'
AGENT_VERSION='1.0'

SILENT=0
[ "$1" = "--silent" ] && SILENT=1
export LC_ALL=C
PATH="$PATH:/usr/sbin:/usr/bin:/bin:/sbin"; export PATH

DATA_DIR="/Library/Application Support/AMPM/DeployAgent"
LOG_FILE="$DATA_DIR/ampm_deploy_log.txt"
mkdir -p "$DATA_DIR"

log() {
    echo "$(date '+%Y-%m-%d %H:%M:%S')  $*" >> "$LOG_FILE"
    [ "$SILENT" -eq 0 ] && echo "$*"
}
[ -f "$LOG_FILE" ] && [ "$(stat -f%z "$LOG_FILE" 2>/dev/null || echo 0)" -gt 1048576 ] && mv -f "$LOG_FILE" "$LOG_FILE.old"

case "$KEY" in __AMPM*) echo " This copy has no access key - install it again from the website."; exit 1 ;; esac

HOST="$(scutil --get ComputerName 2>/dev/null)"; [ -z "$HOST" ] && HOST="$(hostname -s)"
IP="$(ipconfig getifaddr en0 2>/dev/null || ipconfig getifaddr en1 2>/dev/null)"
USER_NAME="$(stat -f%Su /dev/console 2>/dev/null)"

report() {   # id status code output
    curl -sS -m 60 -X POST --data-urlencode "key=$KEY" --data-urlencode "hostname=$HOST" --data-urlencode "taskId=$1" \
        --data-urlencode "status=$2" --data-urlencode "exitCode=$3" --data-urlencode "output=$4" \
        "$BASE/api/deploy/result" >/dev/null 2>&1
}

RESP="$(curl -sS -m 60 -X POST --data-urlencode "key=$KEY" --data-urlencode "hostname=$HOST" --data-urlencode "ip=$IP" \
    --data-urlencode "os=mac" --data-urlencode "agentVersion=$AGENT_VERSION" --data-urlencode "user=$USER_NAME" \
    --data-urlencode "fmt=text" "$BASE/api/deploy/poll" 2>&1)"
FIRST="$(printf '%s\n' "$RESP" | head -n 1)"
if [ "$FIRST" != "OK" ]; then
    log "Could not get work from the website: $(printf '%s' "$RESP" | head -c 200)"
    exit 0
fi

printf '%s\n' "$RESP" | tail -n +2 | while IFS='|' read -r ID KIND FNAME SHA ATYPE ARGS; do
    [ -z "$ID" ] && continue
    log "Task $ID ($KIND) started."
    report "$ID" running 0 "Started"
    WORK="$DATA_DIR/work/$ID"; rm -rf "$WORK"; mkdir -p "$WORK"
    OUT=""; CODE=1

    if [ "$KIND" = "installer" ]; then
        F="$WORK/$(basename "$FNAME")"
        if curl -fsS -m 3600 -G --data-urlencode "key=$KEY" --data-urlencode "task=$ID" "$BASE/api/deploy/file" -o "$F" < /dev/null; then
            GOT="$(shasum -a 256 "$F" | awk '{print $1}')"
            if [ "$GOT" != "$SHA" ]; then
                OUT="SHA-256 mismatch - the file was not run. (got $GOT)"; CODE=1
            else
                installer -pkg "$F" -target / > "$WORK/out.txt" 2>&1 < /dev/null; CODE=$?
                OUT="$(tail -c 3000 "$WORK/out.txt")"
            fi
        else
            OUT="Download failed."; CODE=1
        fi
        if [ "$CODE" -eq 0 ]; then report "$ID" success 0 "Installed successfully. $OUT"; log "Task $ID done."
        else report "$ID" failed "$CODE" "$OUT"; log "Task $ID FAILED ($CODE)."; fi

    elif [ "$KIND" = "agent" ]; then
        if [ "$ATYPE" = "macdeploy" ]; then
            # This agent replaces itself: report first, then reinstall.
            report "$ID" success 0 "Mac Deploy Agent update started on this Mac."
            log "Task $ID : updating the Mac Deploy Agent."
            nohup /bin/bash -c "curl -fsSL -G --data-urlencode 'key=$KEY' '$BASE/api/deploy/mac-install' | bash" >/dev/null 2>&1 &
            rm -rf "$WORK"
            exit 0
        elif [ "$ATYPE" = "mac" ]; then
            if curl -fsSL -G --data-urlencode "key=$KEY" "$BASE/api/endpoints/mac-install" -o "$WORK/install.sh" < /dev/null; then
                /bin/bash "$WORK/install.sh" > "$WORK/out.txt" 2>&1 < /dev/null; CODE=$?
                OUT="$(tail -c 3000 "$WORK/out.txt")"
            else
                OUT="Download failed."; CODE=1
            fi
        else
            OUT="Unknown agent '$ATYPE'."; CODE=1
        fi
        if [ "$CODE" -eq 0 ]; then report "$ID" success 0 "Agent updated. $OUT"; log "Task $ID done."
        else report "$ID" failed "$CODE" "$OUT"; log "Task $ID FAILED ($CODE)."; fi
    else
        report "$ID" failed 1 "Unknown task kind '$KIND'."
    fi
    rm -rf "$WORK"
done
exit 0
