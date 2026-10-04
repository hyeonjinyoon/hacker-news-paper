using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using HnPaper.Web.Models;

namespace HnPaper.Web.Services;

/// <summary>HN 1면 상위 글과 각 원문의 대표 이미지·설명을 모은다.</summary>
public sealed partial class HnCollector(HttpClient http)
{
    private const string Api = "https://hacker-news.firebaseio.com/v0/";
    private const int MaxHtmlBytes = 512 * 1024;
    private const int MaxDescription = 500;
    private const int MaxText = 1200;
    // 싣지 않는 글·지운 글을 빼고도 count개를 채우려고 한 번에 더 받아 두는 수
    private const int Spare = 10;

    // 사용자 이름까지 붙여야 출처가 구분되는 호스트
    private static readonly HashSet<string> UserHosts = ["github.com", "gitlab.com", "codeberg.org", "medium.com", "x.com", "twitter.com"];

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

    public async Task<RawEdition> CollectAsync(int count, CancellationToken ct)
    {
        var collectedAt = Kst.Now;
        var ids = await http.GetFromJsonAsync<long[]>(Api + "topstories.json", ct) ?? [];
        var stories = await FetchStoriesAsync(ids, count, 1, ct);
        return new RawEdition(collectedAt.ToString("yyyy-MM-dd"), collectedAt, stories);
    }

    /// <summary>
    /// 이미 수집한 호에서 싣지 않는 글(RawStory.IsExcluded)을 빼고 순위를 다시 매긴 뒤, 모자란 자리를 지금 HN 1면에서
    /// 이 호에 없는 글로 순위대로 채운다. 수집 시각 뒤에 올라온 글은 그 호의 기사가 아니므로 넣지 않는다.
    /// </summary>
    public async Task<(RawEdition Edition, IReadOnlyList<RawStory> Added)> FillAsync(RawEdition edition, int count, CancellationToken ct)
    {
        var kept = edition.Stories.Where(s => !s.Excluded).OrderBy(s => s.Rank).Select((s, i) => s with { Rank = i + 1 }).ToList();
        if (kept.Count >= count)
            return (edition with { Stories = kept }, []);

        var known = edition.Stories.Select(s => s.Id).ToHashSet();
        var ids = await http.GetFromJsonAsync<long[]>(Api + "topstories.json", ct) ?? [];
        var before = edition.CollectedAt.ToUnixTimeSeconds();
        var added = await FetchStoriesAsync(ids.Where(id => !known.Contains(id)), count - kept.Count, kept.Count + 1, ct,
            item => item.Time <= before);
        return (edition with { Stories = [.. kept, .. added] }, added);
    }

    private async Task<RawStory[]> FetchStoriesAsync(IEnumerable<long> ids, int count, int firstRank, CancellationToken ct,
        Func<HnItem, bool>? accept = null)
    {
        // 채용 글·구인 스레드·포인트 미달 글은 싣지 않고, count개가 찰 때까지 순위대로 더 받아 온다.
        // 순위는 남은 기사끼리 다시 매긴다.
        var live = new List<HnItem>();
        foreach (var batch in ids.Chunk(count + Spare))
        {
            var items = await Task.WhenAll(batch.Select(id =>
                http.GetFromJsonAsync<HnItem>($"{Api}item/{id}.json", PaperJson.Options, ct)));
            live.AddRange(items.OfType<HnItem>().Where(i =>
                i is { Dead: not true, Deleted: not true, Title: not null }
                && !RawStory.IsExcluded(i.Type, i.By, i.Score)
                && (accept?.Invoke(i) ?? true)));
            if (live.Count >= count)
                break;
        }

        using var gate = new SemaphoreSlim(8);
        return await Task.WhenAll(live.Take(count).Select((item, i) => BuildStoryAsync(item, firstRank + i, gate, ct)));
    }

    private async Task<RawStory> BuildStoryAsync(HnItem item, int rank, SemaphoreSlim gate, CancellationToken ct)
    {
        var hnUrl = $"https://news.ycombinator.com/item?id={item.Id}";
        var external = Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

        string? image = null, description = null;
        if (external)
        {
            await gate.WaitAsync(ct);
            try
            {
                (image, description) = await FetchPreviewAsync(uri!, ct);
            }
            finally
            {
                gate.Release();
            }
            image ??= YouTubeThumbnail(uri!);
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
            foreach (var child in children)
            {
                if (comments.Count >= max)
                    return;
                if (child is null || child.Dead == true)
                    continue;
                // 지워진 댓글은 답글이 있을 때만 자리를 남긴다(HN과 같다).
                var deleted = child.Deleted == true || child.Text is null;
                if (deleted && child.Kids is not { Length: > 0 })
                    continue;
                comments.Add(new RawComment(child.Id, parent, depth, child.By ?? "", child.Time,
                    deleted ? null : HnHtmlToMarkdown(child.Text!), deleted));
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

    private async Task<(string? Image, string? Description)> FetchPreviewAsync(Uri uri, CancellationToken ct)
    {
        var html = await FetchHtmlAsync(uri, ct);
        if (html is null)
            return (null, null);

        var meta = ParseMeta(html);
        var image = First(meta, "og:image", "og:image:url", "twitter:image", "twitter:image:src");
        var description = First(meta, "og:description", "description", "twitter:description");

        string? imageUrl = null;
        if (image is not null && Uri.TryCreate(uri, image, out var abs) && abs.Scheme is "http" or "https")
            imageUrl = abs.ToString();

        return (imageUrl, description is null ? null : Truncate(Collapse(description), MaxDescription));
    }

    private async Task<string?> FetchHtmlAsync(Uri uri, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return null;
            if (response.Content.Headers.ContentType?.MediaType?.Contains("html") != true)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var buffer = new byte[MaxHtmlBytes];
            int total = 0, read;
            while (total < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(total), timeout.Token)) > 0)
                total += read;
            return Encoding.UTF8.GetString(buffer, 0, total);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
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

    private static string SiteOf(Uri uri)
    {
        var host = uri.Host.StartsWith("www.", StringComparison.Ordinal) ? uri.Host[4..] : uri.Host;
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

    [GeneratedRegex(@"<meta\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex MetaTag();

    [GeneratedRegex(@"([\w:-]+)\s*=\s*(?:""([^""]*)""|'([^']*)')")]
    private static partial Regex Attribute();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

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
