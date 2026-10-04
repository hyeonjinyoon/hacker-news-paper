using HnPaper.Web.Models;

namespace HnPaper.Web.Services;

/// <summary>hn-paper-update 스킬이 부르는 명령: collect(수집), validate(번역본 검사).</summary>
public static class Cli
{
    private const int StoryCount = 30;

    public static async Task<int> RunAsync(string[] args)
    {
        var options = PaperOptions.Resolve(Environment.GetEnvironmentVariable("Paper__DataDirectory"));
        return args[0] switch
        {
            "collect" => await CollectAsync(options, args),
            _ => Validate(options, args),
        };
    }

    private static async Task<int> CollectAsync(PaperOptions options, string[] args)
    {
        using var http = HnCollector.CreateHttpClient();
        var edition = await new HnCollector(http).CollectAsync(StoryCount, CancellationToken.None);

        var outIndex = Array.IndexOf(args, "--out");
        var path = outIndex > 0 && outIndex + 1 < args.Length
            ? Path.GetFullPath(args[outIndex + 1])
            : options.RawPath(edition.Date);
        PaperJson.Write(path, edition);

        var images = edition.Stories.Count(s => s.Image is not null);
        var descriptions = edition.Stories.Count(s => s.Description is not null);
        Console.WriteLine($"수집 완료: {edition.Date} · {edition.Stories.Count}개 (이미지 {images}, 설명 {descriptions})");
        Console.WriteLine(path);
        return 0;
    }

    private static int Validate(PaperOptions options, string[] args)
    {
        var date = args.Length > 1 ? args[1] : LatestRawDate(options);
        if (date is null || !Kst.DatePattern.IsMatch(date))
        {
            Console.Error.WriteLine("검사할 날짜를 찾지 못했습니다. 예: validate 2026-10-04");
            return 2;
        }

        var rawPath = options.RawPath(date);
        var koPath = options.KoPath(date);
        if (!File.Exists(rawPath))
        {
            Console.Error.WriteLine($"수집본이 없습니다: {rawPath}");
            return 2;
        }
        if (!File.Exists(koPath))
        {
            Console.Error.WriteLine($"번역본이 없습니다: {koPath}");
            return 1;
        }

        RawEdition raw;
        KoEdition ko;
        try
        {
            raw = PaperJson.Read<RawEdition>(rawPath)!;
            ko = PaperJson.Read<KoEdition>(koPath)!;
        }
        catch (System.Text.Json.JsonException e)
        {
            Console.Error.WriteLine($"JSON을 읽지 못했습니다: {e.Message}");
            return 1;
        }

        var (errors, warnings) = EditionValidator.Validate(raw, ko);
        foreach (var warning in warnings)
            Console.WriteLine($"경고: {warning}");
        foreach (var error in errors)
            Console.WriteLine($"오류: {error}");
        Console.WriteLine(errors.Count == 0
            ? $"검사 통과: {date} · 기사 {ko.Stories?.Count ?? 0}개, 경고 {warnings.Count}개"
            : $"검사 실패: 오류 {errors.Count}개, 경고 {warnings.Count}개");
        return errors.Count == 0 ? 0 : 1;
    }

    private static string? LatestRawDate(PaperOptions options) =>
        Directory.Exists(options.RawDirectory)
            ? Directory.EnumerateFiles(options.RawDirectory, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .Where(d => Kst.DatePattern.IsMatch(d))
                .OrderDescending(StringComparer.Ordinal)
                .FirstOrDefault()
            : null;
}
