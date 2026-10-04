import html
import os

E = html.escape
HN = "https://news.ycombinator.com/item?id="

# rank: (id, url, site, points, comments, age, by, korean title, english title, summary, kicker, image)
RAW = [
 (1, "49949235", "https://simonwillison.net/2026/Oct/3/default-hard-budget-caps/", "simonwillison.net", 220, 117, "2시간 전", "elffjs",
  "사이먼 윌리슨 “거의 모든 서비스에 ‘기본 하드 예산 상한’ 필요해진다”",
  "We're going to need default hard budget caps on pretty much everything",
  "코딩 에이전트 시대, ‘한도 넘으면 차단’이 기본값 돼야 한다는 주장.", "AI·클라우드", ""),
 (2, "49949438", HN + "49949438", "news.ycombinator.com", 159, 26, "2시간 전", "paveworld",
  "‘너드의 승리’ IT 칼럼니스트 밥 크린절리 별세 소식",
  "Bob Cringely Has Died",
  "고인 가족의 지인에게서 소식을 들었다는 HN 이용자에 따르면 그는 토요일 새벽 잠자던 중 세상을 떠났다. 본명은 마크 스티븐스. 애플 초기 직원 출신으로 PBS 다큐멘터리 ‘너드의 승리’로 널리 알려졌다.",
  "부고", ""),
 (3, "49946355", "https://cosmowenman.substack.com/p/rodin-museum-3d-scan-verdict", "cosmowenman.substack.com", 98, 49, "5시간 전", "CosmoWenman",
  "“때로는 법이 법이 아니다”…로댕미술관 3D 스캔 판결의 ‘배신’",
  "Treachery in the Rodin Museum 3D scan verdict",
  "파리 로댕미술관 소장품의 3D 스캔 데이터 공개를 놓고 법정 다툼을 벌여 온 코스모 웬먼이 최근 판결을 정면으로 비판했다.",
  "문화·법", "https://substackcdn.com/image/fetch/$s_!ZCqr!,w_1200,h_675,c_fill,f_jpg,q_auto:good,fl_progressive:steep,g_auto/https%3A%2F%2Fsubstack-post-media.s3.amazonaws.com%2Fpublic%2Fimages%2F40df7005-5501-477f-ad3a-42327b210fe8_2507x1317.png"),
 (4, "49946393", "https://notoriousbfg.com/hole-punch/", "notoriousbfg.com", 246, 61, "8시간 전", "trwhite",
  "블랙홀 뚫어 우주선 날린다…중력 퍼즐 웹게임 ‘홀 펀치’",
  "Hole Punch: Sling your spaceship around gravitational fields",
  "우주 공간에 블랙홀을 뚫고 그 중력을 이용해 우주선을 집까지 돌려보내는 브라우저 게임.", "게임", ""),
 (5, "49946895", "https://www.phoronix.com/news/XDC-2026-Valve-Timur-AMDGPU", "phoronix.com", 159, 21, "7시간 전", "speckx",
  "밸브 개발자, 10년 된 AMD 그래픽카드 리눅스 지원 끌어올렸다",
  "The work by Valve's Timur Kristóf on improving old AMD GPUs on Linux",
  "밸브 리눅스 그래픽 드라이버팀의 티무르 크리스토프가 지난 1년간 AMDGPU 커널 드라이버를 개선해, GCN 1.0·1.1 세대 구형 카드에서도 리눅스 게임이 한층 원활해졌다.",
  "리눅스", "https://www.phoronix.net/image.php?id=2026&image=timur"),
 (6, "49947631", "https://ben.stolovitz.com/posts/reasons-not-emt-ranked/", "stolovitz.com", 110, 54, "6시간 전", "citelao",
  "내가 응급구조사가 되지 않은 이유, 순위 매겨봤다",
  "Reasons I didn't become an EMT, ranked",
  "응급구조사(EMT) 자격 취득을 미뤄 온 21가지 이유를 ‘어처구니없는’ 순으로 정리했다.", "에세이",
  "https://ben.stolovitz.com/_astro/og.CPXYubIv_2eq6wj.png"),
 (7, "49923873", "https://www.whec.com/top-news/webster-man-celebrating-the-100th-birthday-of-the-kidney-his-mom-donated-to-him-as-a-teenager/", "whec.com", 152, 40, "10시간 전", "gscott",
  "10대 때 엄마에게 받은 신장, ‘100번째 생일’ 맞았다",
  "Celebrating the 100th birthday of the kidney donated to him as a teenager",
  "미국 뉴욕주 웹스터의 레이 베투스키 씨는 16세이던 1978년 3월 어머니의 신장을 이식받았다. 그 신장이 올해 ‘100살’이 됐다.",
  "화제", "https://www.whec.com/wp-content/uploads/2026/09/kidney-guy-.png"),
 (8, "49948332", "https://www.theguardian.com/technology/2026/oct/03/openai-safety-leader-quits-warning-ai-companys-culture-is-broken", "theguardian.com", 201, 147, "4시간 전", "jethronethro",
  "오픈AI 안전 책임자 사임…“회사 문화 망가졌다” 경고",
  "OpenAI safety leader quits, warning AI company's culture is 'broken'",
  "데이비드 로빈슨이 회사를 떠나며, 빠르게 발전하는 AI 기술에 업계가 더 신중해야 한다고 촉구해 온 내부자 대열에 합류했다.",
  "AI", "https://i.guim.co.uk/img/media/77b3a6702fa4e94c36e292a72e438fa40eb19184/625_0_6261_5010/master/6261.jpg?width=1200&height=630&quality=85&auto=format&fit=crop&precrop=40:21,offset-x50,offset-y0&overlay-align=bottom%2Cleft&overlay-width=100p&overlay-base64=L2ltZy9zdGF0aWMvb3ZlcmxheXMvdGctZGVmYXVsdC5wbmc&enable=upscale&s=e64134a41050264f5ceac249a333fc99"),
 (9, "49910462", "https://asteriskmag.com/issues/15/so-you-think-you-could-be-an-electrician", "asteriskmag.com", 28, 15, "3시간 전", "zdw",
  "“전기기사나 해볼까?”…현장직 권하는 사람은 왜 늘 사무직인가",
  "So You Think You Could Be an Electrician?",
  "아이들에게 ‘기술직을 하라’고 권하는 이들이 정작 화이트칼라라는 점을 꼬집는다.", "칼럼",
  "https://asteriskmag.com/media/pages/issues/15/so-you-think-you-could-be-an-electrician/75a9ca6492-1790620578/blue-collar-jesse-smith-1600x900-crop-sharpen50.png"),
 (10, "49942706", "https://aleph-alpha.com/en/blog/kolibri-has-landed-a-sovereign-open-weight-model/", "aleph-alpha.com", 532, 303, "17시간 전", "bastitx",
  "獨 알레프 알파, ‘소버린’ 오픈웨이트 모델 ‘콜리브리’ 공개",
  "Kolibri: A Sovereign Open-Weight Model",
  "총 780억 개 중 30억 개 파라미터만 활성화하는 영어·독일어 전문가혼합(MoE) 모델. 최대 100만 토큰 문맥을 지원하고 아파치 2.0 라이선스로 가중치를 공개했다.",
  "AI", "https://aleph-alpha.com/_astro/00-cover.Du35XCGh_zJqw.jpeg"),
 (11, "49940482", "https://www.youtube.com/watch?v=UOuxo6SA8Uc", "youtube.com", 14, 6, "3시간 전", "bobajeff",
  "수학 교육의 저주 – 그랜트 샌더슨 [영상]",
  "Math's pedagogical curse – Grant Sanderson [video]",
  "수학 유튜브 채널 ‘3Blue1Brown’ 운영자 그랜트 샌더슨의 영상.", "영상",
  "https://i.ytimg.com/vi/UOuxo6SA8Uc/hqdefault.jpg"),
 (12, "49946567", "https://claude.dev/blog/getting-the-most-out-of-opus-5-5/", "claude.dev", 175, 125, "8시간 전", "saikatsg",
  "클로드·클로드 코드에서 ‘오퍼스 5.5’ 제대로 쓰는 법",
  "Getting the most out of Opus 5.5 in Claude and Claude Code",
  "오퍼스 5.5에 프롬프트를 주는 법, 긴 작업을 조율하는 법, 결과를 검증하는 법을 정리한 가이드.", "AI",
  "https://claude.dev/blog/getting-the-most-out-of-opus-5-5/og.png"),
 (13, "49908570", "https://photoni.st/index.php/2026/09/25/your-body-of-work-thinks-back-at-you/", "photoni.st", 24, 1, "4시간 전", "surprisetalk",
  "당신의 작업물이 당신을 되돌아본다",
  "Your body of work thinks back at you",
  "사진 블로그 ‘포토니스트’에 실린 에세이.", "에세이", ""),
 (14, "49942103", "https://thoreaubasic.com/", "thoreaubasic.com", 37, 13, "7시간 전", "Gorsefound",
  "[Show HN] BASIC이 유행에서 밀려나지 않았다면?…‘소로 베이직’",
  "Show HN: Thoreau BASIC – What if BASIC hadn't gone out of fashion?",
  "윈도 x64와 베어메탈 x64 UEFI에서 돌아가는 무료 64비트 BASIC.", "Show HN", ""),
 (15, "49944912", "https://ftl-os.org/", "ftl-os.org", 152, 61, "12시간 전", "romac",
  "클라우드를 위한 새 운영체제 ‘FTL’",
  "FTL: A new operating system for clouds",
  "클라우드 환경을 겨냥해 새로 설계한 운영체제 프로젝트.", "인프라", "https://ftl-os.org/og.png"),
 (16, "49940877", "https://blog.willmeye.rs/new-york-city-should-carefully-measure-a-new-tree/", "willmeye.rs", 53, 10, "6시간 전", "willmeyers",
  "“뉴욕시, 새 나무 키 정밀 측정해야”…최고 기록은 22년 전 것",
  "New York City should carefully measure a new tree",
  "위키백과 기준 뉴욕시에서 가장 큰 나무 ‘퀸스 자이언트’(40.8m)의 기록은 2004년 측정치다. 다시 잴 때가 됐다는 주장.",
  "과학", "https://blog.willmeye.rs/static/og-image.png"),
 (17, "49938326", "https://royapakzad.substack.com/p/multilingual-ai-agents", "royapakzad.substack.com", 9, 0, "2시간 전", "effects",
  "AI 에이전트 셋, 나라 둘…‘기울어진’ 월드와이드웹",
  "Three AI agents, two countries, and one uneven world wide web",
  "GPT·클로드·뮤즈가 다국어 리서치, 출처 접근, 사람 개입, 관찰 가능성 측면에서 어떻게 다른지 비교했다.", "AI",
  "https://substackcdn.com/image/fetch/$s_!PRgX!,w_1200,h_675,c_fill,f_jpg,q_auto:good,fl_progressive:steep,g_auto/https%3A%2F%2Fsubstack-post-media.s3.amazonaws.com%2Fpublic%2Fimages%2F3a015e49-be95-466c-b3ad-5b5993668e9b_2114x1314.png"),
 (18, "49947472", "https://sjg.io/writing/binrange-have-you-actually-put-the-bins-out/", "sjg.io", 48, 25, "6시간 전", "simonjgreen",
  "“설마 쓰레기통에 UWB 무선 안 달았어요?”",
  "Surely you have ultra-wideband radios on your bins too?",
  "쓰레기통을 내놨는지 확인하는 사소한 문제를 초광대역(UWB) 무선으로 ‘과잉 설계’한 개발자의 고백.", "메이커", ""),
 (19, "49945933", "https://liao.gg/blog/agents-dont-need-memory", "liao.gg", 67, 51, "6시간 전", "kmeh",
  "“AI 에이전트에 필요한 건 기억 아닌 문서”",
  "Agents don't need memory, they need documentation",
  "지난 대화를 검색해 오는 ‘메모리’보다 프로젝트 문서가 에이전트에 더 쓸모 있다는 주장.", "칼럼", ""),
 (20, "49937304", "https://pipod.dev/", "pipod.dev", 80, 30, "9시간 전", "edverma2",
  "[Show HN] 내 서버 샌드박스에서 코딩 에이전트 돌리는 ‘파이 팟’",
  "Show HN: Pi pod – Run your pi coding agent in sandboxes on your own server",
  "pi 코딩 에이전트 세션을 자체 서버의 샌드박스에서 실행해 주는 도구.", "Show HN", ""),
 (21, "49948254", "https://techcrunch.com/2026/10/03/federal-judge-calls-flock-indiscriminate-mass-surveillance/", "techcrunch.com", 335, 208, "4시간 전", "sbulaev",
  "美 연방판사 “번호판 인식 카메라 ‘플록’은 무차별 대량감시”",
  "Federal judge calls Flock 'indiscriminate mass surveillance'",
  "보안관 대리가 영장 없이 플록으로 한 여성의 차량 번호판을 조회한 것은 수정헌법 4조 위반이라고 연방 판사가 판결했다.",
  "프라이버시", "https://techcrunch.com/wp-content/uploads/2026/08/flock-camera-pole.jpg?resize=1200,799"),
 (22, "49945352", "https://dave.recoil.org/docker-has-always-used-microvms/", "recoil.org", 24, 18, "5시간 전", "avsm",
  "“도커는 원래부터 마이크로VM을 썼다”(2016년부터)",
  "Docker has always used microVMs (well since 2016)",
  "도커 데스크톱은 처음부터 최소화한 전용 VM 안에서 컨테이너를 돌려 왔다. 그 이유와 얻은 것을 짚었다.", "인프라",
  "https://dave.recoil.org/docker-has-always-used-microvms/microvms.jpg"),
 (23, "49938399", "http://www.darbiansphotography.com/woking-electrical-control-room-urbex", "darbiansphotography.com", 126, 25, "13시간 전", "NaOH",
  "버려진 영국 워킹 철도 전기관제실 (2016)",
  "Woking Electrical Control Room (2016)",
  "영국 서리주에 남아 있는 폐쇄된 철도 전기관제실을 담은 폐허 탐험 사진.", "포토",
  "http://static1.squarespace.com/static/53e2c0c6e4b04523ac5b9dd9/t/5ee8f2350363d068a9c67d30/1592324671297/DSC_5143.jpg?format=1500w"),
 (24, "49946845", "https://kevincox.ca/2022/05/06/rss-feed-best-practices/", "kevincox.ca", 41, 8, "7시간 전", "KomoD",
  "RSS 피드 모범 사례 (2022)",
  "RSS Feed Best Practices (2022)",
  "RSS 피드를 제대로 만들고 운영하는 요령을 정리한 글.", "웹", ""),
 (25, "49945904", "https://www.ycombinator.com/companies/retailready/jobs/bFcgIe4-implementations", "ycombinator.com", None, None, "10시간 전", "",
  "리테일레디(YC W24), 구현 담당 인재 채용",
  "RetailReady (YC W24) Is Hiring",
  "AI 기반 공급망 컴플라이언스 엔진 스타트업. 660만 달러 투자 유치, 고객사 50곳 이상, 최근 1년 매출 4배 성장.", "채용", ""),
 (26, "49941641", "https://halide.cx/blog/wpd/", "halide.cx", 42, 14, "6시간 전", "computerbuster",
  "메모리 안전한 WebP 디코딩",
  "Memory-Safe WebP Decoding",
  "누구나 쓸 수 있는, 더 빠르고 안전한 WebP 디코더.", "보안", "https://halide.cx/img/hero/bouquet-med.avif"),
 (27, "49949152", "https://www.gadgetreview.com/apples-clean-design-is-stupidity-when-it-comes-to-hiding-fire-extinguishers", "gadgetreview.com", 60, 23, "2시간 전", "josephcsible",
  "“소화기까지 숨기는 애플 ‘클린 디자인’은 어리석다”",
  "Apple's \"Clean Design\" Is Stupidity When It Comes to Hiding Fire Extinguishers",
  "숨겨 둔 소화기는 세련돼 보일지 몰라도 미 산업안전보건청(OSHA)과 방화협회(NFPA)는 명확한 표시와 접근성을 요구한다.", "칼럼",
  "https://www.gadgetreview.com/wp-content/uploads/Clean-Design-Hides-the-Fire-Extinguisher-Someone-Gets-Burned-scaled.jpg"),
 (28, "49947051", "https://blog.cloudflare.com/next-git-platform-on-cloudflare/", "cloudflare.com", 104, 96, "7시간 전", "geoffbp",
  "클라우드플레어 “우리 위에서 차세대 깃 플랫폼 만들어 달라”",
  "We want you to build the next Git platform on Cloudflare",
  "AI 에이전트 시대의 차세대 깃(Git) 플랫폼을 뽑는 공모전을 연다. 저장소 서비스 ‘아티팩츠’는 오픈 베타.", "개발",
  "https://blog.cloudflare.com/_emdash/api/media/file/01M3VGEQ8Q6FVNPPSWW71YRRKT.01M3VGER1EC7W9TEZXNR3GGMK7.png"),
 (29, "49946707", "https://www.da.vidbuchanan.co.uk/blog/hacking-time.html", "da.vidbuchanan.co.uk", 25, 3, "6시간 전", "Retr0id",
  "C2PA로 ‘시간’을 해킹하는 법",
  "How to hack time, with C2PA",
  "콘텐츠 출처 증명 표준 C2PA를 파고든 보안 연구 글.", "보안", ""),
 (30, "49928361", "https://github.com/andreasfertig/cppinsights", "github.com/andreasfertig", 141, 28, "16시간 전", "rramadass",
  "컴파일러의 눈으로 소스코드 본다…‘C++ 인사이트’",
  "C++ Insights – See your source code with the eyes of a Compiler",
  "C++ 코드가 컴파일러 눈에 실제로 어떻게 보이는지 펼쳐 보여주는 오픈소스 도구.", "개발",
  "https://opengraph.githubassets.com/0ccd9fe6ba90655b81faa58ad92da0e81842adb0c4bfb4bc42082364121dd3b0/andreasfertig/cppinsights"),
]

S = {}
for r in RAW:
    rank, id_, url, site, pts, cmt, age, by, ko, en, summ, kicker, img = r
    S[rank] = dict(rank=rank, id=id_, url=url, site=site, pts=pts, cmt=cmt, age=age, by=by,
                   ko=ko, en=en, sum=summ, kicker=kicker, img=img)


def link(s, text=None):
    return f'<a href="{E(s["url"])}" target="_blank" rel="noopener">{E(text or s["ko"])}</a>'


def meta(s, by=False):
    parts = [f'<span class="site">{E(s["site"])}</span>']
    if s["pts"] is not None:
        parts.append(f'<span>▲ {s["pts"]}</span>')
        c = f'댓글 {s["cmt"]}' if s["cmt"] else '토론하기'
        parts.append(f'<a href="{HN}{s["id"]}" target="_blank" rel="noopener">{c}</a>')
    parts.append(f'<span>{s["age"]}</span>')
    if by and s["by"]:
        parts.append(f'<span>{E(s["by"])}</span>')
    return '<p class="meta">' + ''.join(parts) + '</p>'


def thumb(s, cls="thumb"):
    img = ""
    if s["img"]:
        img = (f'<img src="{E(s["img"])}" alt="" loading="lazy" referrerpolicy="no-referrer" '
               f'onerror="this.remove()">')
    return (f'<a class="{cls}" href="{E(s["url"])}" target="_blank" rel="noopener" tabindex="-1" aria-hidden="true">'
            f'<span class="ph-label">{E(s["site"])}</span>{img}</a>')


def kicker(s):
    return f'<span class="kicker">{E(s["kicker"])}</span>'


HERO_SVG = """<svg viewBox="0 0 560 320" role="img" aria-label="소프트 상한과 하드 상한에서의 월 누적 요금 비교 그래픽">
  <rect width="560" height="320" fill="var(--chart-bg)"/>
  <text x="28" y="40" fill="var(--chart-fg)" font-size="17" font-weight="700">월 누적 사용 요금</text>
  <text x="28" y="62" fill="var(--chart-mute)" font-size="12.5">에이전트가 띄운 서비스가 밤새 폭주한다면</text>
  <g stroke="var(--chart-grid)" stroke-width="1">
    <line x1="28" y1="282" x2="532" y2="282"/>
    <line x1="28" y1="232" x2="532" y2="232"/>
    <line x1="28" y1="132" x2="532" y2="132"/>
  </g>
  <line x1="352" y1="84" x2="352" y2="282" stroke="var(--chart-mute)" stroke-width="1" stroke-dasharray="2 4"/>
  <text x="358" y="98" fill="var(--chart-mute)" font-size="12">00:00 잠든 사이</text>
  <line x1="28" y1="182" x2="532" y2="182" stroke="var(--chart-cap)" stroke-width="1.5" stroke-dasharray="6 5"/>
  <text x="32" y="174" fill="var(--chart-cap)" font-size="12.5" font-weight="700">월 예산 한도 $X</text>
  <polyline fill="none" stroke="var(--chart-mute)" stroke-width="2.5" stroke-dasharray="7 6" stroke-linecap="round"
    points="28,278 90,274 150,268 210,258 270,242 330,214 352,198 390,160 430,122 470,92 520,70"/>
  <polyline fill="none" stroke="var(--chart-hot)" stroke-width="4" stroke-linecap="round" stroke-linejoin="round"
    points="28,278 90,274 150,268 210,258 270,242 330,214 352,198 371,182 520,182"/>
  <circle cx="371" cy="182" r="6" fill="var(--chart-hot)"/>
  <text x="404" y="64" fill="var(--chart-mute)" font-size="12.5">소프트 상한: 경고 메일만</text>
  <text x="392" y="208" fill="var(--chart-hot)" font-size="13" font-weight="700">하드 상한: 여기서 차단</text>
  <text x="392" y="226" fill="var(--chart-fg)" font-size="12">초과분은 요금 대신 에러 반환</text>
</svg>"""

CSS = r"""
:root{
  --bg:#fff;--fg:#111;--sub:#3d3d3d;--muted:#767676;--line:#e3e3e3;--rule:#111;
  --accent:#c2410c;--ph:#f2f0eb;--ph-fg:#9a9384;--hover:#c2410c;
  --chart-bg:#141c28;--chart-fg:#e9eef5;--chart-mute:#8593a6;--chart-grid:#26324a;--chart-hot:#ff7a1a;--chart-cap:#f05252;
  --memorial-bg:#1b1b1b;--memorial-fg:#f1f1f1;
}
@media (prefers-color-scheme:dark){
  :root:not([data-theme="light"]){
    --bg:#121212;--fg:#ececec;--sub:#c9c9c9;--muted:#9b9b9b;--line:#2c2c2c;--rule:#e6e6e6;
    --accent:#fb923c;--ph:#1e1e1e;--ph-fg:#7d7d7d;--hover:#fb923c;--memorial-bg:#232323;
  }
}
:root[data-theme="dark"]{
  --bg:#121212;--fg:#ececec;--sub:#c9c9c9;--muted:#9b9b9b;--line:#2c2c2c;--rule:#e6e6e6;
  --accent:#fb923c;--ph:#1e1e1e;--ph-fg:#7d7d7d;--hover:#fb923c;--memorial-bg:#232323;
}
*{box-sizing:border-box}
html{scroll-behavior:smooth}
body{margin:0;background:var(--bg);color:var(--fg);
  font-family:"Pretendard","Apple SD Gothic Neo","Noto Sans KR","Malgun Gothic",sans-serif;
  -webkit-font-smoothing:antialiased;word-break:keep-all;overflow-wrap:anywhere}
a{color:inherit;text-decoration:none}
a:hover{color:var(--hover)}
h1,h2,h3,h4,p{margin:0}
ol,ul{margin:0;padding:0;list-style:none}
.wrap{max-width:1312px;margin:0 auto;padding:0 16px}

/* 상단 유틸 바 */
.util{display:flex;justify-content:space-between;align-items:center;gap:16px;padding:16px 0 0;font-size:13px;color:var(--muted)}
.util nav{display:flex;flex-wrap:wrap}
.util nav a+a::before{content:"";display:inline-block;width:1px;height:10px;background:var(--line);margin:0 10px;vertical-align:middle}

/* 마스트헤드 */
.masthead{display:flex;align-items:center;gap:40px;padding:18px 0 26px;border-bottom:3px solid var(--rule)}
.logo{display:flex;align-items:baseline;gap:8px;flex:none;font-family:"Noto Serif KR",serif;font-weight:900;font-size:42px;letter-spacing:-.02em;line-height:1}
.logo b{display:inline-grid;place-items:center;width:38px;height:38px;background:#ff6600;color:#fff;font:700 26px/1 Verdana,sans-serif;align-self:center}
.logo small{font:700 12px/1 "Pretendard",sans-serif;letter-spacing:.24em;color:var(--accent)}
.gnb{display:flex;gap:30px;flex:1;justify-content:center;font-size:16.5px;font-weight:700;white-space:nowrap}
.tools{display:flex;gap:20px;flex:none}
.tools svg{width:23px;height:23px;display:block}

/* 1면 톱 */
.top{display:grid;grid-template-columns:minmax(0,1fr) 328px;padding-bottom:36px}
.lead{padding-right:30px;border-right:1px solid var(--line)}
.side{padding-left:30px;padding-top:28px}
.hero h1{font-size:37px;font-weight:800;letter-spacing:-.035em;line-height:1.28;margin:28px 0 22px}
.hero-body{display:grid;grid-template-columns:minmax(0,1.42fr) minmax(0,1fr);gap:24px}
.hero-graphic{display:block;aspect-ratio:16/9.2;overflow:hidden}
.hero-graphic svg{display:block;width:100%;height:100%;font-family:inherit}
.lede{font-size:14.5px;line-height:1.72;color:var(--sub)}
.lede p+p{margin-top:10px}
.lede .meta{margin-top:14px}
.subs{display:grid;grid-template-columns:1fr 1fr;gap:28px;margin-top:24px;padding-top:24px;border-top:1px solid var(--line)}
.sub{display:grid;grid-template-columns:200px minmax(0,1fr);gap:20px}
.sub h3{font-size:18.5px;font-weight:700;line-height:1.45;letter-spacing:-.025em}
.sub p.desc{margin-top:8px;font-size:14px;line-height:1.6;color:var(--muted);display:-webkit-box;-webkit-line-clamp:3;-webkit-box-orient:vertical;overflow:hidden}
.card+.card{margin-top:22px;padding-top:22px;border-top:1px solid var(--line)}
.card .thumb{margin-bottom:14px}
.card h3{font-size:19px;font-weight:700;line-height:1.42;letter-spacing:-.03em;margin-top:4px}

/* 썸네일 */
.thumb{position:relative;display:block;aspect-ratio:16/9;background:var(--ph);overflow:hidden}
.thumb img{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;transition:transform .4s}
a.thumb:hover img{transform:scale(1.04)}
.ph-label{position:absolute;inset:0;display:grid;place-items:center;padding:12px;text-align:center;
  font-family:"Noto Serif KR",serif;font-weight:700;font-size:15px;color:var(--ph-fg);letter-spacing:-.01em}
.memorial{position:relative;display:grid;place-content:center;aspect-ratio:16/9;background:var(--memorial-bg);color:var(--memorial-fg);text-align:center;gap:6px}
.memorial span{font-size:10px;letter-spacing:.32em;opacity:.6}
.memorial strong{font-family:"Noto Serif KR",serif;font-size:19px;font-weight:700}

.kicker{display:inline-block;font-size:13px;font-weight:700;color:var(--accent);margin-bottom:4px}
.meta{display:flex;flex-wrap:wrap;align-items:center;font-size:12.5px;color:var(--muted);margin-top:8px;line-height:1.5}
.meta>*+*::before{content:"·";margin:0 6px;color:var(--line)}
.meta a{text-decoration:underline;text-decoration-color:var(--line);text-underline-offset:3px}
.meta .site{font-weight:600}

/* 섹션 공통 */
.band{border-top:3px solid var(--rule);padding:32px 0 40px}
.sec-head{display:flex;justify-content:space-between;align-items:baseline;margin-bottom:18px}
.sec-head h2{font-size:23px;font-weight:800;letter-spacing:-.03em}
.sec-head a.more{font-size:13px;color:var(--muted)}
.sec-head a.more::after{content:" ›"}

/* 2단 띠: 사진기사 | 헤드라인 목록 | 오피니언 */
.band-3{display:grid;grid-template-columns:300px minmax(0,1fr) 328px}
.band-3>*{min-width:0}
.band-3 .col-a{padding-right:30px;border-right:1px solid var(--line)}
.band-3 .col-b{padding:0 30px;border-right:1px solid var(--line)}
.band-3 .col-c{padding-left:30px}
.photo-lead h3{font-size:18px;font-weight:700;line-height:1.45;letter-spacing:-.025em;margin-top:14px}
.photo-lead p.desc{margin-top:8px;font-size:14px;line-height:1.6;color:var(--muted)}
.headlines li{padding:15px 0;border-bottom:1px solid var(--line)}
.headlines li:first-child{padding-top:0}
.headlines h3{font-size:18.5px;font-weight:500;letter-spacing:-.03em;line-height:1.45}
.headlines .meta{margin-top:5px}
.opinion li{padding:16px 0;border-bottom:1px solid var(--line)}
.opinion li:first-child{padding-top:0}
.opinion .kicker{color:#c0392b}
.opinion h3{font-size:16.5px;font-weight:500;line-height:1.5;letter-spacing:-.025em}

/* 테크·라이프 섹션 */
.split{display:grid;grid-template-columns:minmax(0,1fr) 328px}
.split>.main{padding-right:30px;border-right:1px solid var(--line);min-width:0}
.split>.aside{padding-left:30px;min-width:0}
.grid4{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:24px}
.grid4 h3{font-size:16.5px;font-weight:700;line-height:1.45;letter-spacing:-.025em;margin-top:12px}
.grid4 .kicker{margin:12px 0 0;display:block}
.grid4 .kicker+h3{margin-top:2px}
.textlist{display:grid;grid-template-columns:1fr 1fr;column-gap:28px;margin-top:26px;border-top:1px solid var(--line)}
.textlist li{padding:14px 0;border-bottom:1px solid var(--line)}
.textlist h3{font-size:16px;font-weight:500;line-height:1.5;letter-spacing:-.02em}
.textlist p.desc{font-size:13.5px;color:var(--muted);line-height:1.55;margin-top:4px}

/* 많이 본 / 댓글 많은 */
.ranking li{display:grid;grid-template-columns:30px minmax(0,1fr);gap:8px;padding:11px 0;border-bottom:1px solid var(--line)}
.ranking li:first-child{padding-top:0}
.ranking .n{font-family:"Noto Serif KR",serif;font-weight:900;font-size:19px;line-height:1.25;color:var(--accent)}
.ranking h3{font-size:15px;font-weight:500;line-height:1.45;letter-spacing:-.02em}
.ranking .meta{margin-top:3px;font-size:12px}
.aside .box+.box{margin-top:34px}
.jobs{border:1px solid var(--line);padding:18px}
.jobs h3{font-size:16px;font-weight:700;line-height:1.45}
.jobs p.desc{font-size:13.5px;line-height:1.6;color:var(--muted);margin-top:6px}

/* 전체 순위 */
.fulllist{counter-reset:none;border-top:1px solid var(--line);display:grid;grid-template-columns:1fr 1fr;column-gap:40px}
.fulllist li{display:grid;grid-template-columns:44px minmax(0,1fr);gap:6px;padding:14px 0;border-bottom:1px solid var(--line)}
.fulllist .n{font-family:"Noto Serif KR",serif;font-weight:900;font-size:22px;line-height:1.2;color:var(--muted)}
.fulllist li:nth-child(-n+3) .n{color:var(--accent)}
.fulllist h3{font-size:16px;font-weight:600;line-height:1.45;letter-spacing:-.02em}
.fulllist .en{font-size:13px;color:var(--muted);margin-top:3px;line-height:1.45}
.fulllist .meta{margin-top:4px;font-size:12px}

footer{border-top:3px solid var(--rule);padding:26px 0 48px;font-size:13px;line-height:1.7;color:var(--muted)}
footer .logo{font-size:24px;margin-bottom:10px;color:var(--fg)}
footer .logo b{width:24px;height:24px;font-size:16px}

/* 반응형 */
@media (max-width:1180px){
  .gnb{gap:20px;font-size:15.5px}
  .band-3{grid-template-columns:260px minmax(0,1fr)}
  .band-3 .col-b{border-right:0;padding-right:0}
  .band-3 .col-c{grid-column:1/-1;padding:28px 0 0;margin-top:28px;border-top:1px solid var(--line)}
  .opinion{display:grid;grid-template-columns:1fr 1fr;column-gap:28px}
  .opinion li:nth-child(2){padding-top:0}
  .grid4{grid-template-columns:repeat(2,minmax(0,1fr))}
}
@media (max-width:1000px){
  .masthead{flex-wrap:wrap;gap:16px 24px}
  .gnb{order:3;flex-basis:100%;justify-content:flex-start;overflow-x:auto;padding-bottom:4px;scrollbar-width:none}
  .tools{margin-left:auto}
  .top,.split{grid-template-columns:minmax(0,1fr)}
  .lead,.split>.main{padding-right:0;border-right:0}
  .side{padding:28px 0 0;margin-top:28px;border-top:1px solid var(--line);display:grid;grid-template-columns:1fr 1fr;gap:28px}
  .card+.card{margin-top:0;padding-top:0;border-top:0}
  .split>.aside{padding:28px 0 0;margin-top:28px;border-top:1px solid var(--line);display:grid;grid-template-columns:1fr 1fr;gap:28px}
  .aside .box+.box{margin-top:0}
}
@media (max-width:720px){
  .util .date{display:none}
  .logo{font-size:32px}
  .logo b{width:30px;height:30px;font-size:20px}
  .hero h1{font-size:27px;margin:22px 0 16px}
  .hero-body,.subs,.textlist,.fulllist,.opinion{grid-template-columns:minmax(0,1fr)}
  .sub{grid-template-columns:128px minmax(0,1fr);gap:14px}
  .sub h3{font-size:16.5px}
  .sub p.desc{-webkit-line-clamp:2}
  .band-3{grid-template-columns:minmax(0,1fr)}
  .band-3 .col-a{padding-right:0;border-right:0;padding-bottom:24px;margin-bottom:24px;border-bottom:1px solid var(--line)}
  .band-3 .col-b{padding:0}
  .opinion li:nth-child(2){padding-top:16px}
  .side,.split>.aside{grid-template-columns:minmax(0,1fr)}
  .card+.card{padding-top:22px;border-top:1px solid var(--line)}
  .aside .box+.box{margin-top:34px}
  .grid4{gap:20px 16px}
  .grid4 h3{font-size:15px}
}
"""

ICON_SEARCH = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="10.5" cy="10.5" r="6.5"/><path d="M20 20l-4.6-4.6"/></svg>'
ICON_HN = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 4h6v6"/><path d="M20 4l-9 9"/><path d="M18 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5"/></svg>'


def sub(s, visual):
    return (f'<article class="sub">{visual}<div>{kicker(s)}<h3>{link(s)}</h3>'
            f'<p class="desc">{E(s["sum"])}</p>{meta(s)}</div></article>')


def card(s):
    return f'<article class="card">{thumb(s)}{kicker(s)}<h3>{link(s)}</h3>{meta(s)}</article>'


def grid_item(s):
    return f'<article>{thumb(s)}{kicker(s)}<h3>{link(s)}</h3>{meta(s)}</article>'


def headline(s):
    return f'<li><h3>{link(s)}</h3>{meta(s)}</li>'


def opinion(s):
    return f'<li>{kicker(s)}<h3>{link(s)}</h3>{meta(s)}</li>'


def textitem(s):
    return f'<li>{kicker(s)}<h3>{link(s)}</h3><p class="desc">{E(s["sum"])}</p>{meta(s)}</li>'


def ranking(items):
    return ''.join(f'<li><span class="n">{i}</span><div><h3>{link(s)}</h3>{meta(s)}</div></li>'
                   for i, s in enumerate(items, 1))


s1 = S[1]
memorial = ('<a class="memorial" href="' + HN + S[2]["id"] + '" target="_blank" rel="noopener" tabindex="-1" aria-hidden="true">'
            '<span>IN MEMORIAM</span><strong>밥 크린절리</strong></a>')

by_points = sorted([s for s in S.values() if s["pts"]], key=lambda s: -s["pts"])[:10]
by_comments = sorted([s for s in S.values() if s["cmt"]], key=lambda s: -s["cmt"])[:5]

full = ''.join(
    f'<li><span class="n">{s["rank"]}</span><div><h3>{link(s)}</h3>'
    f'<p class="en">{E(s["en"])}</p>{meta(s, by=True)}</div></li>'
    for s in (S[i] for i in range(1, 31)))

page = f"""<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>해커뉴스 페이퍼</title>
<meta name="description" content="Hacker News 1면 상위 30개 글을 한국어로 옮긴 신문형 페이지 (2026년 10월 4일 낮 12시 기준)">
<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/pretendard@1.3.9/dist/web/static/pretendard.min.css">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Noto+Serif+KR:wght@700;900&display=swap">
<style>{CSS}</style>
</head>
<body>
<div class="wrap">
<header>
  <div class="util">
    <span class="date">2026년 10월 4일 일요일 · 낮 12시 기준</span>
    <nav aria-label="바로가기">
      <a href="https://news.ycombinator.com/" target="_blank" rel="noopener">Hacker News 원문</a>
      <a href="https://news.ycombinator.com/newest" target="_blank" rel="noopener">최신</a>
      <a href="https://news.ycombinator.com/ask" target="_blank" rel="noopener">Ask</a>
      <a href="https://news.ycombinator.com/show" target="_blank" rel="noopener">Show</a>
    </nav>
  </div>
  <div class="masthead">
    <a class="logo" href="#top"><b>Y</b>해커뉴스<small>PAPER</small></a>
    <nav class="gnb" aria-label="섹션">
      <a href="#top">주요뉴스</a>
      <a href="#opinion">오피니언</a>
      <a href="#tech">테크</a>
      <a href="#life">라이프</a>
      <a href="#popular">많이 본 뉴스</a>
      <a href="#jobs">채용</a>
      <a href="#rank">전체 순위</a>
    </nav>
    <div class="tools">
      <a href="https://hn.algolia.com/" target="_blank" rel="noopener" aria-label="Hacker News 검색">{ICON_SEARCH}</a>
      <a href="https://news.ycombinator.com/" target="_blank" rel="noopener" aria-label="Hacker News 원문 열기">{ICON_HN}</a>
    </div>
  </div>
</header>

<main>
<section class="top" id="top">
  <div class="lead">
    <article class="hero">
      <h1>{link(s1)}</h1>
      <div class="hero-body">
        <a class="hero-graphic" href="{E(s1["url"])}" target="_blank" rel="noopener" tabindex="-1">{HERO_SVG}</a>
        <div class="lede">
          <p>코딩 에이전트가 유료 API와 클라우드 자원을 손쉽게 띄우는 시대에는 ‘월 X달러를 넘으면 서비스를 끊고 에러를 반환하는’ 하드 예산 상한이 기본값이 돼야 한다는 주장이 나왔다. 장고(Django) 공동 창시자인 개발자 사이먼 윌리슨은 3일(현지시간) 블로그에서 경고 메일만 보내는 소프트 상한으로는 부족하다고 지적했다.</p>
          <p>자는 사이 폭주한 서비스가 수백~수천 달러를 써 버리는 일을 막으려면 상한을 기본으로 켜 두고, 위험을 감수하려는 사용자만 명시적으로 해제하게 해야 한다는 것이다. 앱이 에러를 내는 걸 꺼리는 기업도 있겠지만, 대부분은 1만 달러가 넘는 ‘요금 폭탄’보다 에러를 택할 것이라고 그는 봤다. 특히 개인 프로젝트에 AWS를 쓰지 않는 이유로 이 공포를 꼽으며, AWS가 지난달 16일 프로젝트 단위 월 지출 한도 기능을 내놓은 점을 함께 소개했다.</p>
          {meta(s1, by=True)}
        </div>
      </div>
    </article>
    <div class="subs">
      {sub(S[2], memorial)}
      {sub(S[3], thumb(S[3]))}
    </div>
  </div>
  <aside class="side" aria-label="주요 기사">
    {card(S[10])}
    {card(S[8])}
  </aside>
</section>

<section class="band band-3">
  <div class="col-a photo-lead">
    {thumb(S[21])}
    {kicker(S[21]).replace('class="kicker"', 'class="kicker" style="margin-top:14px"')}
    <h3 style="margin-top:0">{link(S[21])}</h3>
    <p class="desc">{E(S[21]["sum"])}</p>
    {meta(S[21])}
  </div>
  <div class="col-b">
    <ul class="headlines">
      {''.join(headline(S[i]) for i in (4, 5, 7, 12, 15, 28))}
    </ul>
  </div>
  <div class="col-c" id="opinion">
    <div class="sec-head"><h2>오피니언</h2></div>
    <ul class="opinion">
      {''.join(opinion(S[i]) for i in (6, 9, 19, 27, 13))}
    </ul>
  </div>
</section>

<section class="band split" id="tech">
  <div class="main">
    <div class="sec-head"><h2>테크</h2><a class="more" href="https://news.ycombinator.com/" target="_blank" rel="noopener">원문 1면</a></div>
    <div class="grid4">
      {''.join(grid_item(S[i]) for i in (22, 26, 30, 17))}
    </div>
    <ul class="textlist">
      {''.join(textitem(S[i]) for i in (29, 24, 14, 20))}
    </ul>
  </div>
  <aside class="aside">
    <div class="box" id="popular">
      <div class="sec-head"><h2>많이 본 뉴스</h2><span class="meta" style="margin:0">포인트순</span></div>
      <ol class="ranking">{ranking(by_points)}</ol>
    </div>
  </aside>
</section>

<section class="band split" id="life">
  <div class="main">
    <div class="sec-head"><h2>라이프·과학</h2></div>
    <div class="grid4">
      {''.join(grid_item(S[i]) for i in (23, 16, 11, 18))}
    </div>
  </div>
  <aside class="aside">
    <div class="box">
      <div class="sec-head"><h2>댓글 많은 뉴스</h2></div>
      <ol class="ranking">{ranking(by_comments)}</ol>
    </div>
    <div class="box" id="jobs">
      <div class="sec-head"><h2>채용</h2><a class="more" href="https://news.ycombinator.com/jobs" target="_blank" rel="noopener">전체보기</a></div>
      <div class="jobs">{kicker(S[25])}<h3>{link(S[25])}</h3><p class="desc">{E(S[25]["sum"])}</p>{meta(S[25])}</div>
    </div>
  </aside>
</section>

<section class="band" id="rank">
  <div class="sec-head"><h2>Hacker News 1면 전체 순위</h2><span class="meta" style="margin:0">원제 병기 · 30위까지</span></div>
  <ol class="fulllist">{full}</ol>
</section>
</main>

<footer>
  <div class="logo"><b>Y</b>해커뉴스<small>PAPER</small></div>
  <p>Hacker News(news.ycombinator.com) 1면 상위 30개 글의 제목을 한국어로 옮기고 짧게 요약한 비공식 페이지입니다. 2026년 10월 4일 낮 12시(KST) 기준이며, 포인트와 댓글 수는 수집 시점의 값입니다.</p>
  <p>각 글의 저작권은 원 출처에 있습니다. 제목을 누르면 원문으로, ‘댓글’을 누르면 Hacker News 토론으로 이동합니다.</p>
</footer>
</div>
</body>
</html>
"""

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "index.html")
with open(out, "w", encoding="utf-8") as f:
    f.write(page)
print("wrote", out, len(page))
