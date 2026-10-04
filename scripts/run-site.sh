#!/bin/bash
# launchd(com.d9.hn-paper-web)가 실행한다. 사이트를 Release로 게시한 뒤 모든 인터페이스의 5080 포트로 띄운다.
# 같은 네트워크의 다른 기기에서 http://<이 맥의 IP>:5080 으로 접속할 수 있다. 로컬에서만 열려면 HN_PAPER_URLS를 지정한다.
# 개발 중에는 `dotnet run --project src/HnPaper.Web`을 쓰면 된다.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/.build/site"

export PATH="/usr/local/share/dotnet:/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

dotnet publish "$ROOT/src/HnPaper.Web" -c Release -o "$OUT"

export ASPNETCORE_URLS="${HN_PAPER_URLS:-http://0.0.0.0:5080}"
export Paper__DataDirectory="$ROOT/data"

cd "$OUT"
exec dotnet HnPaper.Web.dll
