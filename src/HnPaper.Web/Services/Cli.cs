using HnPaper.Web.Models;

namespace HnPaper.Web.Services;

/// <summary>
/// hn-paper-update 스킬이 부르는 명령.
///   collect [--comments N] [--out path]   1면 상위 30개와 기사별 댓글 수집
///   collect-items yyyy-MM-dd [--comments N]  이미 수집한 호의 기사별 댓글만 다시 수집
///   collect-thumbs yyyy-MM-dd               이미 수집한 호의 대표 이미지만 줄여 저장
///   validate [yyyy-MM-dd] [id ...] [--part body|comments]
///                                         번역본 검사. id를 주면 그 기사의 중간 페이지만, --part를 주면 본문이나 댓글만 검사
/// </summary>
public static class Cli
{
    private const int StoryCount = 30;
    private const int DefaultComments = 30;

    public static async Task<int> RunAsync(string[] args)
    {
        var options = PaperOptions.Resolve(Environment.GetEnvironmentVariable("Paper__DataDirectory"));
        return args[0] switch
        {
            "collect" => await CollectAsync(options, args),
            "collect-items" => await CollectItemsAsync(options, args),
            "collect-thumbs" => await CollectThumbsAsync(options, args),
            _ => Validate(options, args),
        };
    }

    private static async Task<int> CollectAsync(PaperOptions options, string[] args)
    {
        using var http = HnCollector.CreateHttpClient();
        var collector = new HnCollector(http);
        var edition = await collector.CollectAsync(StoryCount, CancellationToken.None);

        var path = Option(args, "--out") is { } outPath ? Path.GetFullPath(outPath) : options.RawPath(edition.Date);
        PaperJson.Write(path, edition);

        var itemsDir = Path.Combine(Path.GetDirectoryName(path)!, edition.Date);
        var items = await collector.CollectItemsAsync(edition, CommentLimit(args),
            id => Path.Combine(itemsDir, id + ".json"), CancellationToken.None);

        // 시험 수집(--out)일 때는 data/img를 건드리지 않는다.
        var thumbs = Option(args, "--out") is null
            ? await new ThumbnailMaker(http, options).MakeAllAsync(edition, CancellationToken.None)
            : (Made: 0, Skipped: 0);

        var images = edition.Stories.Count(s => s.Image is not null);
        var descriptions = edition.Stories.Count(s => s.Description is not null);
        Console.WriteLine($"수집 완료: {edition.Date} · {edition.Stories.Count}개 (이미지 {images}, 줄인 이미지 {thumbs.Made}, 설명 {descriptions}, 기사별 댓글 파일 {items})");
        Console.WriteLine(path);
        return 0;
    }

    private static async Task<int> CollectItemsAsync(PaperOptions options, string[] args)
    {
        var date = args.Length > 1 ? args[1] : null;
        if (date is null || !Kst.DatePattern.IsMatch(date) || !File.Exists(options.RawPath(date)))
        {
            Console.Error.WriteLine("수집본이 있는 날짜를 주세요. 예: collect-items 2026-10-04");
            return 2;
        }

        var edition = PaperJson.Read<RawEdition>(options.RawPath(date))!;
        using var http = HnCollector.CreateHttpClient();
        var items = await new HnCollector(http).CollectItemsAsync(edition, CommentLimit(args),
            id => options.RawItemPath(date, id), CancellationToken.None);
        Console.WriteLine($"기사별 댓글 수집 완료: {date} · {items}/{edition.Stories.Count}개");
        return items == edition.Stories.Count ? 0 : 1;
    }

    private static async Task<int> CollectThumbsAsync(PaperOptions options, string[] args)
    {
        var date = args.Length > 1 ? args[1] : null;
        if (date is null || !Kst.DatePattern.IsMatch(date) || !File.Exists(options.RawPath(date)))
        {
            Console.Error.WriteLine("수집본이 있는 날짜를 주세요. 예: collect-thumbs 2026-10-04");
            return 2;
        }

        var edition = PaperJson.Read<RawEdition>(options.RawPath(date))!;
        using var http = HnCollector.CreateHttpClient();
        var (made, skipped) = await new ThumbnailMaker(http, options).MakeAllAsync(edition, CancellationToken.None);
        Console.WriteLine($"대표 이미지 줄이기 완료: {date} · {made}개 저장, {skipped}개는 원본 주소 사용(내려받기·디코딩 실패)");
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
        var onlyIds = args.Skip(2).Select(a => long.TryParse(a, out var id) ? id : (long?)null).OfType<long>().ToHashSet();
        var part = Option(args, "--part");
        if (part is not (null or "body" or "comments"))
        {
            Console.Error.WriteLine("--part는 body 또는 comments만 쓸 수 있습니다.");
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

        // 서브에이전트가 자기 몫(--part)만 검사할 때는 1면 번역본 검사를 건너뛴다.
        var (errors, warnings) = part is null ? EditionValidator.Validate(raw, ko) : ([], []);

        var items = raw.Stories.Where(s => onlyIds.Count == 0 || onlyIds.Contains(s.Id)).ToList();
        foreach (var story in items)
        {
            var rawItemPath = options.RawItemPath(date, story.Id);
            var koItemPath = options.KoItemPath(date, story.Id);
            try
            {
                var rawItem = File.Exists(rawItemPath) ? PaperJson.Read<RawItem>(rawItemPath) : null;
                var koItem = File.Exists(koItemPath) ? PaperJson.Read<KoItem>(koItemPath) : null;
                var commentsPath = options.KoCommentsPath(date, story.Id);
                var koComments = File.Exists(commentsPath) ? PaperJson.Read<KoComments>(commentsPath) : null;
                EditionValidator.ValidateItem(story, rawItem, koItem, koComments, errors, warnings, part);
            }
            catch (System.Text.Json.JsonException e)
            {
                errors.Add($"{story.Rank}위(id {story.Id}) 중간 페이지 JSON을 읽지 못했습니다: {e.Message}");
            }
        }

        foreach (var warning in warnings)
            Console.WriteLine($"경고: {warning}");
        foreach (var error in errors)
            Console.WriteLine($"오류: {error}");
        Console.WriteLine(errors.Count == 0
            ? $"검사 통과: {date} · 기사 {ko.Stories?.Count ?? 0}개, 중간 페이지 {items.Count}개, 경고 {warnings.Count}개"
            : $"검사 실패: 오류 {errors.Count}개, 경고 {warnings.Count}개");
        return errors.Count == 0 ? 0 : 1;
    }

    private static int CommentLimit(string[] args) =>
        Option(args, "--comments") is { } value && int.TryParse(value, out var n) && n >= 0 ? n : DefaultComments;

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i > 0 && i + 1 < args.Length ? args[i + 1] : null;
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
