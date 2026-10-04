using System.Collections.Concurrent;
using System.Text.Json;
using HnPaper.Web.Models;

namespace HnPaper.Web.Services;

/// <summary>data/ 폴더의 호를 읽어 화면용으로 합친다. 파일이 바뀌면 다음 요청에서 다시 읽는다.</summary>
public sealed class EditionStore(PaperOptions options, ILogger<EditionStore> logger)
{
    private sealed record CacheEntry(DateTime RawStamp, DateTime KoStamp, EditionView View);

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

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

        if (_cache.TryGetValue(date, out var hit) && hit.RawStamp == rawStamp && hit.KoStamp == koStamp)
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

        var view = EditionBuilder.Build(raw, ko);
        _cache[date] = new CacheEntry(rawStamp, koStamp, view);
        return view;
    }
}
