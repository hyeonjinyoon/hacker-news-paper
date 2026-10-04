# Hacker News paper

[Hacker News](https://news.ycombinator.com/) 1면 상위 30개 글을 매일 아침 한국어로 옮겨, 신문 1면처럼 보여 주는 비공식 사이트입니다.

- **1면**: HN 순위대로 고정된 자리에 기사를 배치합니다(1위 톱, 2–3위 보조 기사, 4–5위 사진 기사 …). 기사를 분류하지 않습니다. YC 회사 채용 글, 매달 올라오는 구인 스레드("Who is hiring?" 등), 포인트가 30점에 못 미치는 글(HN이 반응을 보려고 1면에 잠깐 올린 새 글 등 아직 반응이 적은 글)은 싣지 않고, 그 자리는 다음 순위 글로 채웁니다.
- **기사 페이지**: 기사를 누르면 원문으로 가기 전에 중간 페이지가 열립니다. 원문 요약(굵은 한 줄 요약 · 왜 중요한가 · 핵심 내용 · HN 반응)과 HN 댓글 번역(최대 30개, HN 댓글란과 같은 순서)이 나오고, 제목을 누르면 원문으로, 그 아래 원제를 누르면 Hacker News 글로 갑니다.
- **번역**: Claude Code 스킬이 맡습니다. 제목은 원제의 뉘앙스를 살려 옮기고(담담한 제목은 합니다체, 구어 느낌이 강한 제목은 해요체), 원문 기사는 전문을 번역하지 않고 자기 말로 요약합니다. HN에 직접 올라온 본문 글과 댓글은 번역합니다. 댓글도 원문 말투에 맞춰 합니다체나 해요체로 옮깁니다. 이미 번역한 기사(같은 날 다시 수집했거나 며칠째 1면에 남은 글)는 제목·본문을 그대로 쓰고, 새로 달린 댓글만 번역합니다.

이 사이트는 Y Combinator나 Hacker News와 관계가 없습니다. 각 글의 저작권은 원 출처에 있습니다.

## 구성

```
HN API ──collect──▶ data/raw/      1면 수집본, 기사별 댓글
          │        data/img/      원문 og 이미지를 줄인 WebP
          │
Claude Code 스킬 /hn-paper-update
          ├─ reuse: 이미 번역한 기사의 제목·본문, 남아 있는 댓글의 번역을 이 호로 가져옴
          ├─ 메인: 1면 제목 번역(새 기사만) ──▶ data/ko/{날짜}.json
          ├─ hn-paper-article (기사마다) ─────▶ data/ko/{날짜}/{id}.json          본문 요약
          │    원문은 WebFetch로 읽고, 실패하면 에이전트 전용 헤드리스 브라우저(Playwright MCP)로 다시 읽는다
          └─ hn-paper-comments (기사마다,      ▶ data/ko/{날짜}/{id}.comments.json  댓글 번역
             Sonnet 5.5 · 추론 high)
          │
ASP.NET Core 사이트 (src/HnPaper.Web) ◀── data/ 를 읽어 지면을 그린다(파일이 바뀌면 바로 반영)
```

| 경로 | 내용 |
|---|---|
| `src/HnPaper.Web/` | Razor Pages 사이트와 수집·검증 CLI (.NET 10) |
| `.claude/skills/hn-paper-update/` | 오늘자 호를 만드는 스킬 (순서, 제목 번역 규칙) |
| `.claude/agents/` | 기사별 서브에이전트 (본문 요약 `hn-paper-article`, 댓글 번역 `hn-paper-comments`) |
| `scripts/` | 사이트 구동·launchd 설치 스크립트, 그라데이션 패널 이미지 생성 스크립트 |
| `deploy/launchd/` | 사이트를 상시 구동하는 launchd 에이전트 템플릿 |
| `demo/` | 처음 만든 정적 데모 페이지 |
| `data/` | 날짜별 수집본·번역본·이미지 (gitignore, 저장소에 올라가지 않음) |

## 준비물

- [.NET 10 SDK](https://dotnet.microsoft.com/)
- [Claude Code](https://claude.com/claude-code): 번역할 때만 필요합니다
- [uv](https://docs.astral.sh/uv/): 그라데이션 패널 이미지를 새로 만들 때만 필요합니다

## 사이트 실행

개발할 때:

```sh
dotnet run --project src/HnPaper.Web
# http://localhost:5080
```

macOS에서 로그인할 때마다 자동으로 띄우려면 launchd 에이전트를 설치합니다. Release로 게시한 뒤 모든 인터페이스의 5080 포트로 띄우고, 꺼지면 다시 켭니다.

```sh
scripts/install-launchd.sh      # 설치(또는 재설치) — 에이전트 이름 com.d9.hn-paper-web
scripts/uninstall-launchd.sh    # 제거
launchctl kickstart -k gui/$(id -u)/com.d9.hn-paper-web   # 코드를 바꾼 뒤 다시 빌드해서 띄우기
```

- 로그: `~/.local/hn-paper/logs/`
- 로컬에서만 열려면 `HN_PAPER_URLS=http://127.0.0.1:5080`을 지정합니다.
- 데이터 폴더 위치를 바꾸려면 `Paper__DataDirectory`를 지정합니다. 없으면 저장소의 `data/`를 씁니다.

## 매일 업데이트

저장소 폴더에서 Claude Code로 스킬을 실행합니다. 매일 아침 자동으로 돌리려면 Claude 데스크톱 앱 루틴에 같은 프롬프트를 등록합니다.

```
/hn-paper-update                  새로 수집하고 1면 제목·기사 페이지를 모두 만든다
/hn-paper-update 2026-10-04       그 호의 빠진 번역만 채운다(수집하지 않음, 있는 번역은 그대로 둠)
/hn-paper-update 2026-10-04 49942706   그 기사의 페이지만 다시 만든다
```

루틴이나 헤드리스(`claude -p`)로 돌릴 때는 다음 도구를 허용해야 끝까지 진행됩니다. 서브에이전트는 메인의 권한을 물려받습니다.

```
Skill  Agent  Read  Glob  WebFetch  Edit(data/ko/**)
mcp__hn-browser__browser_navigate  mcp__hn-browser__browser_wait_for  mcp__hn-browser__browser_evaluate  mcp__hn-browser__browser_close
Bash(dotnet run --project src/HnPaper.Web -- collect:*)
Bash(dotnet run --project src/HnPaper.Web -- collect-items:*)
Bash(dotnet run --project src/HnPaper.Web -- collect-thumbs:*)
Bash(dotnet run --project src/HnPaper.Web -- reuse:*)
Bash(dotnet run --project src/HnPaper.Web -- merge-comments:*)
Bash(dotnet run --project src/HnPaper.Web -- validate:*)
```

기사 30개를 기사마다 새 서브에이전트로 처리하므로 한 번 실행에 15~20분쯤 걸립니다. 번역·요약 규칙은 스킬과 에이전트 정의 파일에 있으니, 바꾸고 싶으면 그 파일을 고칩니다.

## CLI

스킬이 부르는 명령이지만 직접 써도 됩니다.

```sh
dotnet run --project src/HnPaper.Web -- collect [--comments 30] [--out 경로]
dotnet run --project src/HnPaper.Web -- collect-items 2026-10-04 [--comments 30]
dotnet run --project src/HnPaper.Web -- collect-thumbs 2026-10-04
dotnet run --project src/HnPaper.Web -- collect-fill 2026-10-04 [--comments 30]
dotnet run --project src/HnPaper.Web -- reuse 2026-10-04
dotnet run --project src/HnPaper.Web -- merge-comments 2026-10-04 49949235
dotnet run --project src/HnPaper.Web -- validate [2026-10-04] [id ...] [--part body|comments]
```

| 명령 | 하는 일 |
|---|---|
| `collect` | HN 1면 상위 30개(채용 글·구인 스레드·30포인트 미만 글 제외), 기사별 댓글(HN 순서, 답글 포함, 기본 30개), 줄인 대표 이미지를 오늘 날짜(KST)로 저장 |
| `collect-items` | 이미 수집한 호의 기사별 댓글만 다시 수집 |
| `collect-thumbs` | 이미 수집한 호의 대표 이미지만 다시 줄여 저장 |
| `collect-fill` | 이미 수집한 호에서 싣지 않는 글(위 `collect`의 제외 기준)을 빼고 순위를 다시 매긴 뒤, 모자란 자리를 지금 HN 1면에서 그 호에 없는 글(수집 시각 전에 올라온 것)로 채움. 새로 넣은 기사만 댓글·대표 이미지를 모으므로, 그 기사의 제목 번역과 중간 페이지는 따로 만든다 |
| `reuse` | 이미 번역한 기사(같은 id)의 제목·본문을 이 호에서, 없으면 최근 7개 호에서 가져오고, 지금 수집본에 있는 댓글의 번역만 모아 수집본 순서로 다시 씀. 새로 번역할 제목 목록을 알려 줌 |
| `merge-comments` | `hn-paper-comments`가 새로 번역한 댓글(`data/ko/{날짜}/{id}.comments.new.json`)을 댓글 번역본에 합치고 그 파일을 지움 |
| `validate` | 번역본 검사. 모든 기사가 빠짐없는지, 제목이 반말이 아닌지, 요약이 합니다체인지, 요약 틀(한 줄 요약·소제목·글머리표 개수·분량·HN 반응)을 지켰는지, 굵게를 한 줄 요약에만 썼는지, 수집한 댓글이 모두 번역됐는지 확인 |

## 데이터

```
data/
├── raw/{날짜}.json                     1면 수집본(순위, 원제, URL, 포인트, 댓글 수, og 이미지·설명)
├── raw/{날짜}/{id}.json                기사별 HN 본문 글과 댓글
├── img/{날짜}/{id}-1600-v2.webp        줄인 대표 이미지(큰 것) — /thumbs/ 로 제공
├── img/{날짜}/{id}-800-v2.webp         줄인 대표 이미지(썸네일)
├── ko/{날짜}.json                      1면 제목 번역
├── ko/{날짜}/{id}.json                 기사 본문(마크다운)
└── ko/{날짜}/{id}.comments.json        댓글 번역
```

- 1면의 요약과 톱기사 리드는 따로 쓰지 않고, 기사 본문의 첫 소제목 앞 문단을 가져다 씁니다.
- 대표 이미지는 16:9 칸을 덮는 크기로 줄이되 원본보다 키우지 않습니다. 줄이지 못한 이미지(SVG, AVIF 등)는 원본 주소를 그대로 씁니다. 이미지 규칙을 바꾸면 `ThumbnailMaker.Version`을 올려 캐시된 옛 이미지를 피합니다.
- og 이미지가 없는 기사는 `wwwroot/img/gradients/`의 그라데이션 패널 중 하나를 기사 id로 골라 씁니다. 패널은 `uv run scripts/generate-gradients.py [--count N] [--seed N]`로 다시 만들 수 있습니다.

## 사이트 주소

| 주소 | 페이지 |
|---|---|
| `/` | 번역이 끝난 가장 최근 호의 1면 |
| `/{날짜}` | 그 날짜의 1면 |
| `/{날짜}/{id}` | 기사 페이지(요약·댓글) |
| `/editions` | 지난 호 목록 |

번역이 아직 없는 호나 기사는 원문 제목·설명·댓글로 표시합니다.

## 참고

- 마크다운은 [Markdig](https://github.com/xoofx/markdig)로 렌더링합니다. 번역본과 댓글은 외부에서 온 글이라 원시 HTML을 막고 http(s)·mailto가 아닌 링크는 지웁니다.
- 이미지는 [SkiaSharp](https://github.com/mono/SkiaSharp)로 줄이고, 글꼴은 [Pretendard](https://github.com/orioncactus/pretendard)를 씁니다.
- HTML·CSS는 Brotli/Gzip으로 압축하고, 정적 파일은 오래 캐시합니다(버전이 붙은 CSS 1년, 줄인 이미지 30일).
