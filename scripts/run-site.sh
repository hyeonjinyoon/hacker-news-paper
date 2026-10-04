#!/bin/bash
# launchd(com.d9.hn-paper-web)가 실행한다. 사이트를 Release로 게시한 뒤 127.0.0.1:5080에서 띄운다.
# 개발 중에는 `dotnet run --project src/HnPaper.Web`을 쓰면 된다.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/.build/site"

export PATH="/usr/local/share/dotnet:/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

dotnet publish "$ROOT/src/HnPaper.Web" -c Release -o "$OUT"

export ASPNETCORE_URLS="${HN_PAPER_URLS:-http://127.0.0.1:5080}"
export Paper__DataDirectory="$ROOT/data"

cd "$OUT"
exec dotnet HnPaper.Web.dll
