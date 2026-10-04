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
        var items = await Task.WhenAll(ids.Take(count).Select(id =>
            http.GetFromJsonAsync<HnItem>($"{Api}item/{id}.json", PaperJson.Options, ct)));

        var live = items.OfType<HnItem>().Where(i => i is { Dead: not true, Deleted: not true, Title: not null }).ToList();

        using var gate = new SemaphoreSlim(8);
        var stories = await Task.WhenAll(live.Select((item, i) => BuildStoryAsync(item, i + 1, gate, ct)));

        return new RawEdition(collectedAt.ToString("yyyy-MM-dd"), collectedAt, stories);
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

        var isJob = item.Type == "job";
        return new RawStory(
            Rank: rank,
            Id: item.Id,
            Type: item.Type ?? "story",
            Title: item.Title!,
            Url: external ? uri!.ToString() : hnUrl,
            Site: external ? SiteOf(uri!) : "news.ycombinator.com",
            Points: isJob ? null : item.Score,
            Comments: isJob ? null : item.Descendants ?? 0,
            By: item.By ?? "",
            Time: item.Time,
            Image: image,
            Description: description,
            Text: item.Text is null ? null : Truncate(PlainText(item.Text), MaxText));
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
        bool? Deleted);
}
