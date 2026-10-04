#!/bin/bash
# 매일 아침 launchd(com.d9.hn-paper-update)가 실행한다.
# Claude Code를 헤드리스로 띄워 hn-paper-update 스킬로 오늘자 호를 수집·번역한다.
# 사이트는 data/ 파일이 바뀌면 다음 요청부터 새 호를 보여주므로 재시작할 필요가 없다.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

export PATH="$HOME/.local/bin:/usr/local/share/dotnet:/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

echo "[$(date '+%F %T')] 업데이트 시작"

claude -p "/hn-paper-update" \
  --allowedTools \
    "Skill" \
    "Bash(dotnet run --project src/HnPaper.Web -- collect:*)" \
    "Bash(dotnet run --project src/HnPaper.Web -- validate:*)" \
    "Read" "Glob" \
    "Edit(data/ko/**)" \
    "WebFetch"

# 스킬이 검증까지 하지만, 결과를 로그에 한 번 더 남긴다.
dotnet run --project src/HnPaper.Web -- validate

echo "[$(date '+%F %T')] 업데이트 끝"
