using System.Collections.Concurrent;
using System.Text.Json;
using HnPaper.Web.Models;

namespace HnPaper.Web.Services;

/// <summary>data/ 폴더의 호를 읽어 화면용으로 합친다. 파일이 바뀌면 다음 요청에서 다시 읽는다.</summary>
public sealed class EditionStore(PaperOptions options, ILogger<EditionStore> logger)
{
    private sealed record CacheEntry(DateTime RawStamp, DateTime KoStamp, (DateTime Stamp, int Count) Bodies, (DateTime Stamp, int Count) Thumbs, EditionView View);
    private sealed record ItemCacheEntry(DateTime RawStamp, DateTime KoStamp, DateTime CommentsStamp, ItemView View);

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly ConcurrentDictionary<(string Date, long Id), ItemCacheEntry> _items = new();

    /// <summary>중간 페이지용 기사 본문과 댓글. 수집본·번역본이 없으면 있는 것만으로 채운다.</summary>
    public ItemView LoadItem(EditionView edition, StoryView story)
    {
        var rawPath = options.RawItemPath(edition.Date, story.Raw.Id);
        var koPath = options.KoItemPath(edition.Date, story.Raw.Id);
        var commentsPath = options.KoCommentsPath(edition.Date, story.Raw.Id);
        var rawStamp = Stamp(rawPath);
        var koStamp = Stamp(koPath);
        var commentsStamp = Stamp(commentsPath);

        var key = (edition.Date, story.Raw.Id);
        if (_items.TryGetValue(key, out var hit)
            && hit.RawStamp == rawStamp && hit.KoStamp == koStamp && hit.CommentsStamp == commentsStamp)
            return hit.View;

        var raw = rawStamp == DateTime.MinValue ? null : TryRead<RawItem>(rawPath);
        var ko = koStamp == DateTime.MinValue ? null : TryRead<KoItem>(koPath);
        var comments = commentsStamp == DateTime.MinValue ? null : TryRead<KoComments>(commentsPath);
        var view = EditionBuilder.BuildItem(edition.CollectedAt, story, raw, ko, comments);
        _items[key] = new ItemCacheEntry(rawStamp, koStamp, commentsStamp, view);
        return view;
    }

    private static DateTime Stamp(string path) =>
        File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;

    private T? TryRead<T>(string path) where T : class
    {
        try
        {
            return PaperJson.Read<T>(path);
        }
        catch (JsonException e)
        {
            logger.LogWarning(e, "파일을 읽지 못해 건너뜁니다: {Path}", path);
            return null;
        }
    }

    public IReadOnlyList<EditionInfo> List()
    {
        if (!Directory.Exists(options.RawDirectory))
            return [];

        return Directory.EnumerateFiles(options.RawDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Where(d => Kst.DatePattern.IsMatch(d))
            .Select(date => new EditionInfo(date, File.Exists(options.KoPath(date))))
            .OrderByDescending(e => e.Date, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>번역까지 끝난 가장 최근 호. 번역된 호가 없으면 가장 최근 수집본.</summary>
    public string? LatestDate()
    {
        var all = List();
        return (all.FirstOrDefault(e => e.Translated) ?? all.FirstOrDefault())?.Date;
    }

    public EditionView? Load(string date)
    {
        if (!Kst.DatePattern.IsMatch(date))
            return null;

        var rawPath = options.RawPath(date);
        if (!File.Exists(rawPath))
            return null;

        var koPath = options.KoPath(date);
        var rawStamp = File.GetLastWriteTimeUtc(rawPath);
        var koStamp = File.Exists(koPath) ? File.GetLastWriteTimeUtc(koPath) : DateTime.MinValue;

        // 1면 요약·리드는 기사별 본문 번역본에서 가져오므로 그 파일들이 바뀌어도 다시 읽는다.
        var bodyFiles = BodyFiles(date);
        var bodies = (bodyFiles.Count == 0 ? DateTime.MinValue : bodyFiles.Max(File.GetLastWriteTimeUtc), bodyFiles.Count);

        var thumbFiles = ThumbFiles(date);
        var thumbs = (thumbFiles.Count == 0 ? DateTime.MinValue : thumbFiles.Max(File.GetLastWriteTimeUtc), thumbFiles.Count);

        if (_cache.TryGetValue(date, out var hit) && hit.RawStamp == rawStamp && hit.KoStamp == koStamp && hit.Bodies == bodies && hit.Thumbs == thumbs)
            return hit.View;

        var raw = PaperJson.Read<RawEdition>(rawPath);
        if (raw is null)
            return null;

        KoEdition? ko = null;
        if (koStamp != DateTime.MinValue)
        {
            try
            {
                ko = PaperJson.Read<KoEdition>(koPath);
            }
            catch (JsonException e)
            {
                logger.LogWarning(e, "번역 파일을 읽지 못해 원문으로 표시합니다: {Path}", koPath);
            }
        }

        var bodyById = new Dictionary<long, string>();
        foreach (var file in bodyFiles)
        {
            if (TryRead<KoItem>(file) is { Body: { } body } item)
                bodyById.TryAdd(item.Id, body);
        }

        // 큰 것과 작은 것이 둘 다 있는 기사만 줄인 이미지를 쓴다.
        var names = thumbFiles.Select(Path.GetFileName).ToHashSet();
        var thumbIds = raw.Stories.Select(s => s.Id)
            .Where(id => names.Contains($"{id}.webp") && names.Contains($"{id}-s.webp"))
            .ToHashSet();

        var view = EditionBuilder.Build(raw, ko, bodyById, thumbIds);
        _cache[date] = new CacheEntry(rawStamp, koStamp, bodies, thumbs, view);
        return view;
    }

    private List<string> ThumbFiles(string date)
    {
        var dir = Path.Combine(options.ThumbDirectory, date);
        return Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.webp").ToList() : [];
    }

    /// <summary>data/ko/{date}/의 기사별 본문 번역본({id}.json). 댓글 번역본(*.comments.json)은 뺀다.</summary>
    private List<string> BodyFiles(string date)
    {
        var dir = Path.Combine(options.KoDirectory, date);
        return Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.json").Where(f => !f.EndsWith(".comments.json", StringComparison.Ordinal)).ToList()
            : [];
    }
}
