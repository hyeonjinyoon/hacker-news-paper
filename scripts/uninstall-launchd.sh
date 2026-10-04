#!/bin/bash
# install-launchd.sh로 설치한 에이전트를 내리고 plist를 지운다. 로그와 data/는 그대로 둔다.
set -uo pipefail

DOMAIN="gui/$(id -u)"

for name in web update; do
  label="com.d9.hn-paper-$name"
  launchctl bootout "$DOMAIN/$label" 2>/dev/null
  rm -f "$HOME/Library/LaunchAgents/$label.plist"
  echo "제거: $label"
done
