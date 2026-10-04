#!/bin/bash
# launchd 에이전트 두 개를 설치(또는 재설치)한다.
#   com.d9.hn-paper-web     로그인하면 사이트를 띄우고 계속 유지한다 (http://127.0.0.1:5080)
#   com.d9.hn-paper-update  매일 오전 7시에 오늘자 호를 만든다
# 로그: ~/.local/hn-paper/logs/
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
AGENTS="$HOME/Library/LaunchAgents"
DOMAIN="gui/$(id -u)"

mkdir -p "$AGENTS" "$HOME/.local/hn-paper/logs"

for name in web update; do
  label="com.d9.hn-paper-$name"
  target="$AGENTS/$label.plist"
  sed -e "s#__ROOT__#$ROOT#g" -e "s#__HOME__#$HOME#g" "$ROOT/deploy/launchd/$label.plist" > "$target"
  plutil -lint "$target" > /dev/null
  launchctl bootout "$DOMAIN/$label" 2>/dev/null || true
  launchctl bootstrap "$DOMAIN" "$target"
  echo "설치: $label"
done
