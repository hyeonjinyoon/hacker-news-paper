using HnPaper.Web.Models;

namespace HnPaper.Web.Services;

/// <summary>
/// hn-paper-update 스킬이 부르는 명령.
///   collect [--comments N] [--out path]   전날(UTC) HN 과거 1면 상위 30개와 기사별 댓글 수집
///   collect-items yyyy-MM-dd [--comments N]  이미 수집한 호의 기사별 댓글만 다시 수집
///   collect-thumbs yyyy-MM-dd               이미 수집한 호의 대표 이미지만 WebP로 압축해 저장
///   collect-fill yyyy-MM-dd [--comments N]   이미 수집한 호에서 싣지 않는 글과 중복 글을 빼고, 모자란 자리를 그 호의 HN 과거 1면 글로 채움
///   reuse yyyy-MM-dd                        이미 번역한 기사의 제목·본문과 지금 수집본에 있는 댓글의 번역을 이 호로 가져옴
///   merge-comments yyyy-MM-dd id            새로 번역한 댓글({id}.comments.new.json)을 댓글 번역본에 합침
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
            "collect-fill" => await CollectFillAsync(options, args),
            "reuse" => Reuse(options, args),
            "merge-comments" => MergeComments(options, args),
            _ => Validate(options, args),
        };
    }

    private static async Task<int> CollectAsync(PaperOptions options, string[] args)
    {
        using var http = HnCollector.CreateHttpClient();
        await using var browser = new BrowserFetcher();
        var collector = new HnCollector(http, browser);
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
        Console.WriteLine($"수집 완료: {edition.Date} · {edition.Stories.Count}개 (이미지 {images}, WebP 이미지 {thumbs.Made}, 설명 {descriptions}, 기사별 댓글 파일 {items}, 브라우저로 다시 연 원문 {browser.Opened}, HN 본문 링크에서 찾은 이미지 {collector.FromTextLinks})");
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
        Console.WriteLine($"대표 이미지 압축 완료: {date} · {made}개 저장, {skipped}개는 원본 주소 사용(내려받기·디코딩 실패)");
        return 0;
    }

    private static async Task<int> CollectFillAsync(PaperOptions options, string[] args)
    {
        var date = args.Length > 1 ? args[1] : null;
        if (date is null || !Kst.DatePattern.IsMatch(date) || !File.Exists(options.RawPath(date)))
        {
            Console.Error.WriteLine("수집본이 있는 날짜를 주세요. 예: collect-fill 2026-10-04");
            return 2;
        }

        var edition = PaperJson.Read<RawEdition>(options.RawPath(date))!;
        using var http = HnCollector.CreateHttpClient();
        await using var browser = new BrowserFetcher();
        var collector = new HnCollector(http, browser);
        var (filled, added) = await collector.FillAsync(edition, StoryCount, CancellationToken.None);
        PaperJson.Write(options.RawPath(date), filled);

        // 새로 넣은 기사만 댓글과 대표 이미지를 모은다. 이미 번역한 기사의 댓글 수집본은 건드리지 않는다.
        var addedOnly = edition with { Stories = added };
        var items = await collector.CollectItemsAsync(addedOnly, CommentLimit(args),
            id => options.RawItemPath(date, id), CancellationToken.None);
        var thumbs = await new ThumbnailMaker(http, options).MakeAllAsync(addedOnly, CancellationToken.None);

        var keptIds = filled.Stories.Select(s => s.Id).ToHashSet();
        var removed = edition.Stories.Where(s => !keptIds.Contains(s.Id)).ToList();
        foreach (var story in removed)
            Console.WriteLine($"뺌: {story.Rank}위 {story.Id} {story.Title} ({(story.Excluded ? $"{story.Type}, {story.By}, {story.Points?.ToString() ?? "-"}포인트" : "앞 순위 글과 같은 원문")})");
        Console.WriteLine($"보충 완료: {date} · {removed.Count}개 뺌, {added.Count}개 추가 (WebP 이미지 {thumbs.Made}, 기사별 댓글 파일 {items})");
        foreach (var story in added)
            Console.WriteLine($"{story.Rank}위 {story.Id} {story.Title}");
        return 0;
    }

    private static int Reuse(PaperOptions options, string[] args)
    {
        var date = args.Length > 1 ? args[1] : null;
        if (date is null || !Kst.DatePattern.IsMatch(date) || !File.Exists(options.RawPath(date)))
        {
            Console.Error.WriteLine("수집본이 있는 날짜를 주세요. 예: reuse 2026-10-04");
            return 2;
        }

        var result = TranslationReuse.Apply(options, date);
        Console.WriteLine($"번역 재사용: {date}");
        Console.WriteLine($"제목: 이 호 {result.Titles.Kept}개, 지난 호에서 가져옴 {result.Titles.Carried}개, 새로 번역 {result.Titles.Missing}개");
        Console.WriteLine($"본문: 이 호 {result.Bodies.Kept}개, 지난 호에서 가져옴 {result.Bodies.Carried}개, 새로 작성 {result.Bodies.Missing}개");
        Console.WriteLine($"댓글: 이 호 {result.Comments.Kept}개, 지난 호에서 가져옴 {result.Comments.Carried}개, 새로 번역 {result.Comments.Missing}개");
        if (result.NeedTitle.Count > 0)
        {
            Console.WriteLine("제목을 새로 번역할 기사:");
            foreach (var story in result.NeedTitle)
                Console.WriteLine($"{story.Rank}위 {story.Id} {story.Title}");
        }
        return 0;
    }

    private static int MergeComments(PaperOptions options, string[] args)
    {
        var date = args.Length > 1 ? args[1] : null;
        long id = 0;
        if (date is null || !Kst.DatePattern.IsMatch(date) || args.Length < 3 || !long.TryParse(args[2], out id)
            || !File.Exists(options.RawItemPath(date, id)) || !File.Exists(options.KoNewCommentsPath(date, id)))
        {
            Console.Error.WriteLine("날짜와 기사 id를 주고, 새로 번역한 댓글을 data/ko/{날짜}/{id}.comments.new.json에 먼저 쓰세요. 예: merge-comments 2026-10-04 49949235");
            return 2;
        }

        try
        {
            var counts = TranslationReuse.MergeComments(options, date, id);
            Console.WriteLine($"댓글 병합: {id} · 새 번역 {counts.Carried}개 + 기존 {counts.Kept}개, 빠진 댓글 {counts.Missing}개");
            return counts.Missing == 0 ? 0 : 1;
        }
        catch (System.Text.Json.JsonException e)
        {
            Console.Error.WriteLine($"새 댓글 번역 파일을 읽지 못했습니다: {e.Message}");
            return 1;
        }
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

        var items = raw.Stories.Where(s => !s.Excluded && (onlyIds.Count == 0 || onlyIds.Contains(s.Id))).ToList();
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
