#!/bin/bash
# server_api.sh — универсальный API состояния сервера для ServerControl.
# Ничего не знает о конкретном сервере: всё берёт из config.sh рядом.
#
# Использование: bash server_api.sh status|units|logs:<unit>|stats|services|term:<cmd>

set -uo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONFIG="$DIR/config.sh"

# Значения по умолчанию — если config.sh нет.
LAN_IFACE=""
WATCH_SERVICES=""
HEALTH_DIR="/var/log/servercontrol"
GENERATION_MARKER=""
AGENT_MARKER=""

if [[ -f "$CONFIG" ]]; then
    # shellcheck disable=SC1090
    source "$CONFIG"
fi

json_str() { printf '"%s"' "$(printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g; s/\t/ /g; s/\r//g')"; }
json_arr() {
    local first=1 out=""
    for v in "$@"; do
        [[ -z "$v" ]] && continue
        [[ $first -eq 1 ]] && first=0 || out+=","
        out+=$(json_str "$v")
    done
    printf '[%s]' "$out"
}

svc_state() { systemctl is-active "$1" 2>/dev/null || true; }
proc_running() { [[ -n "$1" ]] && pgrep -f "$1" >/dev/null 2>&1 && echo on || echo off; }

cmd_status() {
    local temp_c="" mem_total mem_used mem_avail swap_total swap_used
    local tfile
    tfile=$(ls /sys/class/thermal/thermal_zone*/temp 2>/dev/null | head -1)
    [[ -n "$tfile" ]] && temp_c=$(( $(cat "$tfile" 2>/dev/null || echo 0) / 1000 ))

    read -r mem_total mem_used mem_avail < <(free -m | awk '/^Mem:/{print $2, $3, $7}')
    read -r swap_total swap_used < <(free -m | awk '/^Swap:/{print $2, $3}')

    local disk_pct disk_free hdd_pct="" hdd_free="" hdd_path="/"
    disk_pct=$(df / | awk 'NR==2{gsub("%","",$5); print $5}')
    disk_free=$(df -h / | awk 'NR==2{print $4}')
    if [[ -n "${DATA_DISK:-}" && -d "${DATA_DISK}" ]]; then
        hdd_path="$DATA_DISK"
        hdd_pct=$(df "$DATA_DISK" | awk 'NR==2{gsub("%","",$5); print $5}')
        hdd_free=$(df -h "$DATA_DISK" | awk 'NR==2{print $4}')
    fi

    local load1 load5 load15
    read -r load1 load5 load15 _ < /proc/loadavg
    local uptime_s
    uptime_s=$(cut -d. -f1 /proc/uptime)

    # Видеокарта (AMD/Intel sysfs; на NVIDIA пусто)
    local vram_used=0 vram_total=0
    local vf
    vf=$(ls /sys/class/drm/card*/device/mem_info_vram_used 2>/dev/null | head -1)
    if [[ -n "$vf" ]]; then
        vram_used=$(( $(cat "$vf" 2>/dev/null || echo 0) / 1048576 ))
        vram_total=$(( $(cat "${vf%_used}_total" 2>/dev/null || echo 0) / 1048576 ))
    fi

    local lan_ip tail_ip
    lan_ip=$(hostname -I 2>/dev/null | awk '{print $1}')
    tail_ip=$(tailscale ip -4 2>/dev/null | head -1)

    # Опрашиваемые службы из конфига
    local svc_json="" name
    for name in $WATCH_SERVICES; do
        [[ -n "$svc_json" ]] && svc_json+=","
        svc_json+="\"$name\": $(json_str "$(svc_state "$name")")"
    done
    [[ -n "$svc_json" ]] && svc_json="$svc_json,"
    svc_json+="\"generation\": $(json_str "$(proc_running "$GENERATION_MARKER")")"
    svc_json+=",\"agent\": $(json_str "$(proc_running "$AGENT_MARKER")")"

    cat <<EOF
{
  "ok": true,
  "host": $(json_str "$(hostname)"),
  "temp_c": ${temp_c:-null},
  "load": [$load1, $load5, $load15],
  "uptime_s": $uptime_s,
  "mem": {"total_mb": $mem_total, "used_mb": $mem_used, "avail_mb": $mem_avail},
  "swap": {"total_mb": ${swap_total:-0}, "used_mb": ${swap_used:-0}},
  "disk": {"pct": $disk_pct, "free": $(json_str "$disk_free")},
  "hdd": {"path": $(json_str "$hdd_path"), "pct": ${hdd_pct:-null}, "free": $(json_str "$hdd_free")},
  "gpu": {"vram_used_mb": $vram_used, "vram_total_mb": $vram_total},
  "network": {"lan_ip": $(json_str "$lan_ip"), "tailscale_ip": $(json_str "$tail_ip")},
  "services": {$svc_json},
  "ts": $(date +%s)
}
EOF
}

cmd_units() {
    systemctl list-units --type=service --all --no-legend --no-pager 2>/dev/null \
      | python3 -c '
import sys, json
units = []
for line in sys.stdin:
    p = line.split()
    if len(p) < 4:
        continue
    units.append({"name": p[0], "state": p[2], "sub": p[3]})
print(json.dumps({"ok": True, "units": units}, ensure_ascii=False))
'
}

cmd_services() {
    # Список настроенных служб с состоянием (из config.sh)
    echo -n '{"ok": true, "services": ['
    local first=1 name label
    for name in $WATCH_SERVICES; do
        [[ $first -eq 1 ]] && first=0 || echo -n ","
        printf '{"name": %s, "label": %s, "state": %s}' \
            "$(json_str "$name")" "$(json_str "$name")" "$(json_str "$(svc_state "$name")")"
    done
    echo ']}'
}

cmd_stats() {
    local today file
    today=$(date +%Y-%m-%d)
    file="$HEALTH_DIR/health_$today.log"
    echo '{"ok": true, "samples": ['
    if [[ -f "$file" ]]; then
        local first=1
        while IFS= read -r line; do
            if [[ "$line" =~ ^CPU:[[:space:]]+([0-9]+).*load:\ ([0-9.]+)\ ([0-9.]+)\ ([0-9.]+) ]]; then
                [[ $first -eq 1 ]] && first=0 || echo ","
                printf '  {"temp": %s, "load": [%s, %s, %s]}' \
                    "${BASH_REMATCH[1]}" "${BASH_REMATCH[2]}" "${BASH_REMATCH[3]}" "${BASH_REMATCH[4]}"
            fi
        done < "$file"
    fi
    echo ''
    echo ']}'
}

cmd_logs() {
    local unit=${1:-} n=${2:-80}
    case "$unit" in
        health)  tail -n "$n" "$HEALTH_DIR"/health_*.log 2>/dev/null ;;
        backup)  tail -n "$n" "${BACKUP_LOG:-/dev/null}" 2>/dev/null ;;
        "")      journalctl -n "$n" --no-pager -q 2>/dev/null ;;
        *)       journalctl -u "$unit" -n "$n" --no-pager -q 2>/dev/null ;;
    esac
}

cmd_term() {
    local cmd=${1:-}
    [[ -z "$cmd" ]] && { echo '{"ok": false, "error": "empty"}'; return; }
    printf '{"ok": true, "output": '
    bash -c "$cmd" 2>&1 | head -200 | python3 -c 'import sys,json; print(json.dumps(sys.stdin.read()))'
    printf ', "exit": %d}\n' "$?"
}

case "${1:-status}" in
    status)   cmd_status ;;
    units)    cmd_units ;;
    services) cmd_services ;;
    stats)    cmd_stats ;;
    logs)     shift; cmd_logs "${1:-}" "${2:-80}" ;;
    term)     shift; cmd_term "$*" ;;
    *)        echo '{"ok": false, "error": "unknown command"}' ;;
esac
