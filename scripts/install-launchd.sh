#!/bin/bash
# 사이트를 상시 구동하는 launchd 에이전트(com.d9.hn-paper-web)를 설치(또는 재설치)한다.
# 로그인하면 사이트를 띄우고 계속 유지한다 (포트 5080, 모든 인터페이스).
# 매일 아침 업데이트는 Claude 데스크톱 앱 루틴에서 /hn-paper-update를 실행한다.
# 로그: ~/.local/hn-paper/logs/
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
AGENTS="$HOME/Library/LaunchAgents"
DOMAIN="gui/$(id -u)"

mkdir -p "$AGENTS" "$HOME/.local/hn-paper/logs"

for name in web; do
  label="com.d9.hn-paper-$name"
  target="$AGENTS/$label.plist"
  sed -e "s#__ROOT__#$ROOT#g" -e "s#__HOME__#$HOME#g" "$ROOT/deploy/launchd/$label.plist" > "$target"
  plutil -lint "$target" > /dev/null
  launchctl bootout "$DOMAIN/$label" 2>/dev/null || true
  launchctl bootstrap "$DOMAIN" "$target"
  echo "설치: $label"
done
