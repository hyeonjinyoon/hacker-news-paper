#!/bin/bash
# Claude Code Stop 훅(.claude/settings.local.json)이 작업이 끝날 때마다 실행한다.
# 마지막 재시작 뒤 사이트 코드(src/HnPaper.Web, bin·obj 제외)가 바뀌었으면 빌드해 보고,
# 빌드가 되면 launchd 서비스(com.d9.hn-paper-web)를 다시 띄운다. 서비스는 run-site.sh로 Release 게시 후 5080 포트로 뜬다.
# 빌드가 안 되면 떠 있는 사이트를 내리지 않는다.
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
STAMP="$ROOT/.build/.site-restarted"
export PATH="/usr/local/share/dotnet:/opt/homebrew/bin:/usr/local/bin:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

if [ -f "$STAMP" ] && [ -z "$(find "$ROOT/src/HnPaper.Web" -type f -newer "$STAMP" -not -path '*/bin/*' -not -path '*/obj/*' -print -quit)" ]; then
  exit 0
fi

if ! dotnet build "$ROOT/src/HnPaper.Web" -v q >/dev/null 2>&1; then
  echo '{"systemMessage":"사이트 코드 빌드가 실패해 5080 사이트는 다시 띄우지 않았습니다."}'
  exit 0
fi

mkdir -p "$ROOT/.build"
touch "$STAMP"
launchctl kickstart -k "gui/$(id -u)/com.d9.hn-paper-web"
echo '{"systemMessage":"사이트 코드가 바뀌어 5080 사이트를 다시 띄웠습니다."}'
