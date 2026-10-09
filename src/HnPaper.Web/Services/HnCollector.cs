using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using HnPaper.Web.Models;

namespace HnPaper.Web.Services;

/// <summary>HN 과거 1면(front?day=)의 상위 글과 각 원문의 대표 이미지·설명을 모은다.</summary>
/// <param name="browser">봇 차단에 막힌 원문의 대표 이미지·설명을 다시 읽을 브라우저. 없으면 다시 읽지 않는다.</param>
public sealed partial class HnCollector(HttpClient http, BrowserFetcher? browser = null)
{
    private const string Api = "https://hacker-news.firebaseio.com/v0/";
    // 그날(UTC) 1면에 오른 글을 모은 목록. API에는 없어서 HTML에서 글 id만 읽고, 나머지는 API로 받는다.
    private const string Front = "https://news.ycombinator.com/front";
    // 싣지 않는 글을 빼고도 count개를 채우려고 읽는 쪽 수(한 쪽에 30개)
    private const int FrontPages = 2;
    private const int MaxHtmlBytes = 512 * 1024;
    private const int MaxDescription = 500;
    private const int MaxText = 1200;
    // 싣지 않는 글·지운 글을 빼고도 count개를 채우려고 한 번에 더 받아 두는 수
    private const int Spare = 10;

    // 원문에 og 이미지가 없을 때 HN 본문 글에서 찾아볼 링크 수
    private const int MaxTextLinks = 3;
    // 같은 댓글에 달린 답글은 앞에서부터 이만큼만 담는다. 최상위 댓글은 전체 개수 제한만 받는다.
    private const int MaxReplies = 3;

    // 사용자 이름까지 붙여야 출처가 구분되는 호스트
    private static readonly HashSet<string> UserHosts = ["github.com", "gitlab.com", "codeberg.org", "medium.com", "x.com", "twitter.com"];

    // HN 본문 링크에서 대표 이미지를 찾지 않는 호스트. archive.today의 og 이미지는 페이지 전체를 찍은 스크린숏이다.
    private static readonly HashSet<string> SkippedLinkHosts =
        ["archive.ph", "archive.today", "archive.is", "archive.li", "archive.md", "archive.vn", "archive.fo", "news.ycombinator.com"];

    // 같은 기사인지 제목으로 볼 때 세지 않는 흔한 단어
    private static readonly HashSet<string> CommonWords = ["that", "this", "with", "from", "what", "your", "have", "into", "about", "when", "than", "they"];

    private int _fromTextLinks;
    private readonly HashSet<long> _dupes = [];

    /// <summary>원문에 og 이미지가 없어 HN 본문 글의 링크에서 대표 이미지를 찾은 글 수</summary>
    public int FromTextLinks => _fromTextLinks;

    /// <summary>읽은 과거 1면에서 HN이 [dupe]로 표시한 글. 순위가 더 높아도 싣지 않는다.</summary>
    public IReadOnlyCollection<long> Dupes => _dupes;

    public static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            MaxAutomaticRedirections = 5,
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.8");
        return client;
    }

    /// <summary>
    /// 호 날짜(한국 시간 오늘)의 전날 HN 과거 1면에서 상위 count개를 모은다. HN의 하루는 UTC라서 전날 목록은
    /// 한국 시간 오전 9시에 닫힌다. 그 전에 수집하면 그날 마지막 몇 시간 동안 1면에 오른 글이 빠질 수 있다.
    /// </summary>
    public async Task<RawEdition> CollectAsync(int count, CancellationToken ct)
    {
        var collectedAt = Kst.Now;
        var date = collectedAt.ToString("yyyy-MM-dd");
        var day = DayBefore(date);
        var ids = await FrontIdsAsync(day, ct);
        var stories = await FetchStoriesAsync(ids, count, 1, [], ct);
        return new RawEdition(date, day, collectedAt, stories);
    }

    /// <summary>
    /// 이미 수집한 호에서 싣지 않는 글(RawStory.IsExcluded), HN이 [dupe]로 표시한 글, 앞 순위 글과 같은 원문을 가리키는 글을 빼고 순위를 다시 매긴 뒤,
    /// 모자란 자리를 그 호의 HN 과거 1면에서 이 호에 없는 글로 순위대로 채운다. 메인 1면에서 수집한 예전 호(Day 없음)는 호 날짜의 전날 목록에서 채운다.
    /// </summary>
    public async Task<(RawEdition Edition, IReadOnlyList<RawStory> Added)> FillAsync(RawEdition edition, int count, CancellationToken ct)
    {
        var ids = await FrontIdsAsync(edition.Day ?? DayBefore(edition.Date), ct);
        var kept = edition.Stories.Where(s => !s.Excluded && !_dupes.Contains(s.Id)).OrderBy(s => s.Rank).DistinctBy(s => UrlKey(s.Url))
            .Select((s, i) => s with { Rank = i + 1 }).ToList();
        if (kept.Count >= count)
            return (edition with { Stories = kept }, []);

        var known = edition.Stories.Select(s => s.Id).ToHashSet();
        var added = await FetchStoriesAsync(ids.Where(id => !known.Contains(id)), count - kept.Count, kept.Count + 1,
            kept.Select(s => UrlKey(s.Url)), ct);
        return (edition with { Stories = [.. kept, .. added] }, added);
    }

    private static string DayBefore(string date) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd").AddDays(-1).ToString("yyyy-MM-dd");

    /// <summary>
    /// HN 과거 1면(front?day=)의 글 id를 순위대로 FrontPages쪽까지 읽는다. HN이 제목 앞에 [dupe]를 붙인 글은
    /// 순위가 더 높아도 원래 글을 두고 따로 올라온 중복 글이므로 빼고 Dupes에 모은다.
    /// </summary>
    private async Task<List<long>> FrontIdsAsync(string day, CancellationToken ct)
    {
        var ids = new List<long>();
        for (var page = 1; page <= FrontPages; page++)
        {
            var html = await http.GetStringAsync($"{Front}?day={day}&p={page}", ct);
            var rows = FrontRow().Matches(html);
            for (var i = 0; i < rows.Count; i++)
            {
                var id = long.Parse(rows[i].Groups[1].Value);
                var end = i + 1 < rows.Count ? rows[i + 1].Index : html.Length;
                if (DupeMark().IsMatch(html.AsSpan(rows[i].Index, end - rows[i].Index)))
                    _dupes.Add(id);
                else
                    ids.Add(id);
            }
            if (!html.Contains("morelink"))
                break;
        }

        // HTML 구조가 바뀌었거나 HN이 요청을 막았으면 빈 호를 쓰지 않고 멈춘다.
        if (ids.Count == 0)
            throw new InvalidOperationException($"{Front}?day={day}에서 글을 찾지 못했습니다.");
        return ids;
    }

    /// <param name="knownUrls">이미 실은 글의 UrlKey. 이 원문을 가리키는 글은 싣지 않는다.</param>
    private async Task<RawStory[]> FetchStoriesAsync(IEnumerable<long> ids, int count, int firstRank, IEnumerable<string> knownUrls, CancellationToken ct)
    {
        // 채용 글·구인 스레드·포인트 미달 글·Show HN 글과, 순위가 더 높은 글과 같은 원문을 가리키는 글은 싣지 않고([dupe] 글은 ids에 없다),
        // count개가 찰 때까지 순위대로 더 받아 온다. 순위는 남은 기사끼리 다시 매긴다.
        var seen = knownUrls.ToHashSet();
        var live = new List<HnItem>();
        foreach (var batch in ids.Chunk(count + Spare))
        {
            var items = await Task.WhenAll(batch.Select(id =>
                http.GetFromJsonAsync<HnItem>($"{Api}item/{id}.json", PaperJson.Options, ct)));
            live.AddRange(items.OfType<HnItem>().Where(i =>
                i is { Dead: not true, Deleted: not true, Title: not null }
                && !RawStory.IsExcluded(i.Type, i.By, i.Score, i.Title)
                && seen.Add(UrlKey(i.Url ?? HnUrl(i.Id)))));
            if (live.Count >= count)
                break;
        }

        using var gate = new SemaphoreSlim(8);
        return await Task.WhenAll(live.Take(count).Select((item, i) => BuildStoryAsync(item, firstRank + i, gate, ct)));
    }

    private async Task<RawStory> BuildStoryAsync(HnItem item, int rank, SemaphoreSlim gate, CancellationToken ct)
    {
        var hnUrl = HnUrl(item.Id);
        var external = Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

        string? image = null, description = null;
        if (external)
        {
            (image, description, _) = await FetchPreviewAsync(uri!, gate, ct);
            image ??= YouTubeThumbnail(uri!);
            if (image is null && item.Text is not null)
                (image, description) = await FetchFromTextLinksAsync(item, uri!, description, gate, ct);
        }

        return new RawStory(
            Rank: rank,
            Id: item.Id,
            Type: item.Type ?? "story",
            Title: item.Title!,
            Url: external ? uri!.ToString() : hnUrl,
            Site: external ? SiteOf(uri!) : "news.ycombinator.com",
            Points: item.Score,
            Comments: item.Descendants ?? 0,
            By: item.By ?? "",
            Time: item.Time,
            Image: image,
            Description: description,
            Text: item.Text is null ? null : Truncate(PlainText(item.Text), MaxText));
    }

    /// <summary>
    /// 기사마다 HN 본문 글과 댓글을 모아 기사별 수집본을 쓴다. 댓글은 HN 댓글란과 같은 순서
    /// (kids가 표시 순서이므로 깊이 우선 전위 순회)로 앞에서부터 최대 maxComments개를 담는다.
    /// 같은 댓글에 달린 답글은 앞에서부터 MaxReplies개까지만 담는다.
    /// </summary>
    public async Task<int> CollectItemsAsync(RawEdition edition, int maxComments, Func<long, string> pathFor, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(16);
        var written = 0;
        await Parallel.ForEachAsync(edition.Stories, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (story, token) =>
        {
            var item = await FetchItemAsync(story.Id, gate, token);
            if (item is null)
                return;
            var comments = await FetchCommentsAsync(item, maxComments, gate, token);
            var text = item.Text is null ? null : HnHtmlToMarkdown(item.Text);
            PaperJson.Write(pathFor(story.Id), new RawItem(story.Id, item.Descendants ?? comments.Count, text, comments));
            Interlocked.Increment(ref written);
        });
        return written;
    }

    private async Task<List<RawComment>> FetchCommentsAsync(HnItem story, int max, SemaphoreSlim gate, CancellationToken ct)
    {
        var comments = new List<RawComment>();

        async Task WalkAsync(long[]? kids, long parent, int depth)
        {
            if (kids is not { Length: > 0 } || comments.Count >= max)
                return;
            var children = await Task.WhenAll(kids.Select(id => FetchItemAsync(id, gate, ct)));
            var added = 0;
            foreach (var child in children)
            {
                if (comments.Count >= max || (depth > 0 && added >= MaxReplies))
                    return;
                if (child is null || child.Dead == true)
                    continue;
                // 지워진 댓글은 답글이 있을 때만 자리를 남긴다(HN과 같다).
                var deleted = child.Deleted == true || child.Text is null;
                if (deleted && child.Kids is not { Length: > 0 })
                    continue;
                comments.Add(new RawComment(child.Id, parent, depth, child.By ?? "", child.Time,
                    deleted ? null : HnHtmlToMarkdown(child.Text!), deleted));
                added++;
                await WalkAsync(child.Kids, child.Id, depth + 1);
            }
        }

        await WalkAsync(story.Kids, story.Id, 0);
        return comments;
    }

    private async Task<HnItem?> FetchItemAsync(long id, SemaphoreSlim gate, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            return await http.GetFromJsonAsync<HnItem>($"{Api}item/{id}.json", PaperJson.Options, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>HN 댓글·본문 HTML(p, i, a, pre/code)을 마크다운으로 바꾼다.</summary>
    internal static string HnHtmlToMarkdown(string html)
    {
        var codes = new List<string>();
        var text = CodeBlock().Replace(html, m =>
        {
            codes.Add(WebUtility.HtmlDecode(Tags().Replace(m.Groups[1].Value, "")).TrimEnd());
            return $"\u0000{codes.Count - 1}\u0000";
        });
        text = text.Replace("<p>", "\n\n");
        text = Italic().Replace(text, m => $"*{m.Groups[1].Value.Trim()}*");
        text = Anchor().Replace(text, m => " " + WebUtility.HtmlDecode(m.Groups[1].Value) + " ");
        text = WebUtility.HtmlDecode(Tags().Replace(text, ""));
        text = CodePlaceholder().Replace(text, m => $"\n\n```\n{codes[int.Parse(m.Groups[1].Value)]}\n```\n\n");
        return BlankLines().Replace(text, "\n\n").Trim();
    }

    /// <summary>
    /// 원문에서 og 이미지를 얻지 못했을 때(봇 차단·구독 벽 포함), 올린 사람이 HN 본문 글에 붙인 링크(다른 언론사의 같은 기사 등)에서
    /// 찾는다. 링크 페이지의 제목이 HN 제목과 겹칠 때만 같은 기사로 보고 쓴다. 설명은 원문에 없을 때만 채운다.
    /// </summary>
    private async Task<(string? Image, string? Description)> FetchFromTextLinksAsync(
        HnItem item, Uri original, string? description, SemaphoreSlim gate, CancellationToken ct)
    {
        foreach (var link in TextLinks(item.Text!, original).Take(MaxTextLinks))
        {
            var preview = await FetchPreviewAsync(link, gate, ct);
            if (preview.Image is not null && SameStory(item.Title!, preview.Title))
            {
                Interlocked.Increment(ref _fromTextLinks);
                return (preview.Image, description ?? preview.Description);
            }
        }
        return (null, description);
    }

    /// <summary>HN 본문 글의 링크 가운데 원문과 다른 사이트의 것. archive.today·HN 링크는 뺀다.</summary>
    private static IEnumerable<Uri> TextLinks(string html, Uri original) =>
        Anchor().Matches(html)
            .Select(m => Uri.TryCreate(WebUtility.HtmlDecode(m.Groups[1].Value), UriKind.Absolute, out var link) ? link : null)
            .OfType<Uri>()
            .Where(link => link.Scheme is "http" or "https"
                && !SkippedLinkHosts.Contains(BareHost(link))
                && BareHost(link) != BareHost(original))
            .DistinctBy(link => link.ToString());

    /// <summary>두 제목이 4글자 이상의 단어를 둘 이상 함께 쓰면 같은 기사로 본다.</summary>
    private static bool SameStory(string title, string? other)
    {
        if (other is null)
            return false;
        var words = TitleWords(title);
        return TitleWords(other).Count(words.Contains) >= 2;
    }

    private static HashSet<string> TitleWords(string title) =>
        Word().Matches(title.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(w => w.Length >= 4 && !CommonWords.Contains(w))
            .ToHashSet();

    private async Task<(string? Image, string? Description, string? Title)> FetchPreviewAsync(Uri uri, SemaphoreSlim gate, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        (string? Html, bool Blocked) fetched;
        try
        {
            fetched = await FetchHtmlAsync(uri, ct);
        }
        finally
        {
            gate.Release();
        }

        // 봇 차단에 막혔거나 접속하지 못한 원문은 창을 띄운 브라우저로 한 번 더 연다.
        // 브라우저는 한 번에 한 페이지씩 열므로, 기다리는 동안 다른 글의 요청을 막지 않게 gate 밖에서 연다.
        var (html, baseUri) = (fetched.Html, uri);
        if (fetched.Blocked && browser is not null && await browser.FetchHtmlAsync(uri, ct) is { } page)
            (html, baseUri) = (page.Html, page.Url);
        if (html is null)
            return (null, null, null);

        var meta = ParseMeta(html);
        var image = First(meta, "og:image", "og:image:url", "twitter:image", "twitter:image:src");
        var description = First(meta, "og:description", "description", "twitter:description");

        string? imageUrl = null;
        if (image is not null && Uri.TryCreate(baseUri, image, out var abs) && abs.Scheme is "http" or "https")
            imageUrl = abs.ToString();

        return (imageUrl, description is null ? null : Truncate(Collapse(description), MaxDescription),
            First(meta, "og:title", "twitter:title"));
    }

    /// <returns>HTML(받지 못하면 null)과, 봇 차단에 막혔거나 접속하지 못해 브라우저로 다시 열어 볼 만한지.</returns>
    private async Task<(string? Html, bool Blocked)> FetchHtmlAsync(Uri uri, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return (null, response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable);
            if (response.Content.Headers.ContentType?.MediaType?.Contains("html") != true)
                return (null, false);

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var buffer = new byte[MaxHtmlBytes];
            int total = 0, read;
            while (total < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(total), timeout.Token)) > 0)
                total += read;
            return (Encoding.UTF8.GetString(buffer, 0, total), false);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return (null, true);
        }
    }

    private static Dictionary<string, string> ParseMeta(string html)
    {
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match tag in MetaTag().Matches(html))
        {
            string? key = null, content = null;
            foreach (Match attr in Attribute().Matches(tag.Value))
            {
                var name = attr.Groups[1].Value.ToLowerInvariant();
                var value = attr.Groups[2].Success ? attr.Groups[2].Value : attr.Groups[3].Value;
                if (name is "property" or "name")
                    key = value;
                else if (name == "content")
                    content = value;
            }
            if (key is not null && !string.IsNullOrWhiteSpace(content))
                meta.TryAdd(key, WebUtility.HtmlDecode(content).Trim());
        }
        return meta;
    }

    private static string? First(Dictionary<string, string> meta, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (meta.TryGetValue(key, out var value) && value.Length > 0)
                return value;
        }
        return null;
    }

    private static string? YouTubeThumbnail(Uri uri)
    {
        var host = uri.Host.Replace("www.", "");
        string? id = host switch
        {
            "youtu.be" => uri.AbsolutePath.Trim('/'),
            "youtube.com" or "m.youtube.com" => System.Web.HttpUtility.ParseQueryString(uri.Query)["v"],
            _ => null,
        };
        return string.IsNullOrEmpty(id) ? null : $"https://i.ytimg.com/vi/{Uri.EscapeDataString(id)}/hqdefault.jpg";
    }

    private static string HnUrl(long id) => $"https://news.ycombinator.com/item?id={id}";

    /// <summary>
    /// 같은 원문인지 가릴 때 쓰는 주소. HN은 주소가 글자까지 같아야 중복으로 막으므로, http/https·www·끝 슬래시·조각(#)·
    /// 추적 매개변수(utm_* 등)만 다른 주소는 따로 올라와 함께 1면에 오를 수 있다(예: …/mistral-large-4/ 와 …/mistral-large-4//).
    /// </summary>
    internal static string UrlKey(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return url;
        var path = Slashes().Replace(uri.AbsolutePath, "/").TrimEnd('/');
        var query = string.Join('&', uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !TrackingParam().IsMatch(p)));
        return query.Length == 0 ? BareHost(uri) + path : $"{BareHost(uri)}{path}?{query}";
    }

    private static string BareHost(Uri uri) =>
        uri.Host.StartsWith("www.", StringComparison.Ordinal) ? uri.Host[4..] : uri.Host;

    private static string SiteOf(Uri uri)
    {
        var host = BareHost(uri);
        if (UserHosts.Contains(host))
        {
            var user = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (user is not null)
                return $"{host}/{user}";
        }
        return host;
    }

    private static string PlainText(string html) =>
        Collapse(WebUtility.HtmlDecode(Tags().Replace(html.Replace("<p>", "\n\n"), " ")));

    private static string Collapse(string text) => Whitespace().Replace(text, " ").Trim();

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max].TrimEnd() + "…";

    [GeneratedRegex(@"<tr\s+class=""athing\b[^""]*""\s+id=""(\d+)""")]
    private static partial Regex FrontRow();

    // 과거 1면에서 중복 글의 제목 앞에 붙는 표시: <span class="titleline"> [dupe] <a href=...
    [GeneratedRegex(@"class=""titleline"">\s*\[dupe\]")]
    private static partial Regex DupeMark();

    [GeneratedRegex(@"<meta\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex MetaTag();

    [GeneratedRegex(@"([\w:-]+)\s*=\s*(?:""([^""]*)""|'([^']*)')")]
    private static partial Regex Attribute();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"/{2,}")]
    private static partial Regex Slashes();

    [GeneratedRegex(@"^(utm_[^=]*|fbclid|gclid)(=|$)", RegexOptions.IgnoreCase)]
    private static partial Regex TrackingParam();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();

    [GeneratedRegex(@"<pre><code>(.*?)</code></pre>", RegexOptions.Singleline)]
    private static partial Regex CodeBlock();

    [GeneratedRegex(@"<i>(.*?)</i>", RegexOptions.Singleline)]
    private static partial Regex Italic();

    [GeneratedRegex(@"<a\s[^>]*href=""([^""]*)""[^>]*>.*?</a>", RegexOptions.Singleline)]
    private static partial Regex Anchor();

    [GeneratedRegex("\u0000(\\d+)\u0000")]
    private static partial Regex CodePlaceholder();

    [GeneratedRegex(@"[ \t]*\n[ \t]*\n\s*")]
    private static partial Regex BlankLines();

    /// <param name="Kids">하위 댓글 id. HN 댓글란에 보이는 순서와 같다.</param>
    private sealed record HnItem(
        long Id,
        string? Type,
        string? By,
        long Time,
        string? Title,
        string? Url,
        int? Score,
        int? Descendants,
        string? Text,
        bool? Dead,
        bool? Deleted,
        long[]? Kids);
}
