#!/bin/bash
set -euo pipefail
resources="$(cd "$(dirname "$0")/../Resources" && pwd)"
fail() {
    /usr/bin/osascript - "$1" <<'OSA'
on run argv
    display alert "桌面互联" message (item 1 of argv) as warning
end run
OSA
    exit 1
}
if [[ "$(uname -m)" == arm64 || "$(sysctl -in hw.optional.arm64 2>/dev/null || true)" == 1 ]]; then
    arch=arm64
else
    arch=intel
fi
exe="$resources/EdgeInput.app/Contents/MacOS/edge-input"
log=$(mktemp -t edgepair)
engine_pid=''
agent=''
panel=''
cleanup(){
    for pid in "$panel" "$agent" "$engine_pid"; do
        if [[ -n "$pid" ]]; then kill "$pid" 2>/dev/null || true; fi
    done
    rm -f "$log"
}
trap cleanup EXIT
trap 'exit 0' TERM INT
if "$exe" cli list >/dev/null 2>&1; then
    fail "另一个桌面互联或 Lan Mouse 引擎正在运行。请先退出旧应用，然后再打开此版本。"
else
    "$exe" daemon >>"$log" 2>&1 &
    engine_pid=$!
fi
"$resources/pair-$arch" mac "$exe" >/dev/null 2>>"$log" &
agent=$!
for i in {1..80}; do
    grep -q '^READY ' "$log" && break
    kill -0 "$agent" 2>/dev/null || fail "$(tail -n 1 "$log")"
    sleep .25
done
/usr/bin/osascript -e 'display dialog "桌面互联正在等待或处理键鼠连接。请在 Windows 主程序中配对、拖动设备和应用布局。首次连接请按系统提示为输入引擎授权辅助功能。" with title "桌面互联" buttons {"退出桌面互联"} default button 1' >/dev/null 2>&1 &
panel=$!
while kill -0 "$panel" 2>/dev/null && kill -0 "$agent" 2>/dev/null; do sleep 1; done
if ! kill -0 "$agent" 2>/dev/null; then wait "$agent" || fail "$(tail -n 1 "$log")"; fi
