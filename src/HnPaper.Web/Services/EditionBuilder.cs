using HnPaper.Web.Models;

namespace HnPaper.Web.Services;

/// <summary>수집본과 번역본을 합치고 지면에 배치한다.</summary>
public static class EditionBuilder
{
    /// <param name="bodies">기사별 본문 번역본(마크다운). 1면 요약과 리드는 이 본문의 첫 문단들을 쓴다.</param>
    /// <param name="thumbs">대표 이미지를 줄여 저장해 둔 기사 id.</param>
    public static EditionView Build(RawEdition raw, KoEdition? ko, IReadOnlyDictionary<long, string>? bodies = null,
        IReadOnlySet<long>? thumbs = null)
    {
        var koById = new Dictionary<long, KoStory>();
        foreach (var story in ko?.Stories ?? [])
            koById.TryAdd(story.Id, story);

        // 채용 글은 수집 단계에서 빼지만, 그 전에 수집한 호에 남은 것도 지면에 싣지 않는다.
        var stories = raw.Stories
            .Where(s => !s.IsJob)
            .OrderBy(s => s.Rank)
            .Select(r =>
            {
                koById.TryGetValue(r.Id, out var k);
                var intro = bodies is not null && bodies.TryGetValue(r.Id, out var body)
                    ? MarkdownRenderer.IntroParagraphs(body, 2)
                    : [];
                var local = thumbs?.Contains(r.Id) == true;
                var large = local ? $"/thumbs/{raw.Date}/{PaperOptions.ThumbFileName(r.Id, ThumbnailMaker.LargeWidth)}" : r.Image;
                var small = local ? $"/thumbs/{raw.Date}/{PaperOptions.ThumbFileName(r.Id, ThumbnailMaker.SmallWidth)}" : r.Image;
                return new StoryView
                {
                    Date = raw.Date,
                    Raw = r,
                    ImageUrl = large,
                    ImageSmallUrl = small,
                    ImageSrcset = local ? $"{small} {ThumbnailMaker.SmallWidth}w, {large} {ThumbnailMaker.LargeWidth}w" : null,
                    Title = Nonblank(k?.Title) ?? r.Title,
                    Intro = intro,
                    // 본문 요약의 첫 문단 → 예전 호의 1면 요약 → (번역 전 호면) 원문 설명
                    Summary = intro.FirstOrDefault() ?? Nonblank(k?.Summary) ?? (ko is null ? Nonblank(r.Description) : null),
                    Age = RelativeAge(raw.CollectedAt, r.Time),
                };
            })
            .ToList();

        var view = new EditionView
        {
            Date = raw.Date,
            CollectedAt = raw.CollectedAt,
            Translated = ko is not null,
            All = stories,
        };
        Plan(view);

        // 1면 리드: 톱기사 본문 요약의 첫 문단들 → 예전 호의 리드 → 요약 한 줄
        var lead = ko?.Lead?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        view.Lead = view.Hero?.Intro is { Count: > 0 } intro ? intro
            : lead is { Count: > 0 } ? lead
            : view.Hero?.Summary is { } summary ? [summary]
            : [];
        return view;
    }

    /// <summary>
    /// 기사를 분류하지 않고 HN 순위대로 고정된 자리에 차례로 채운다.
    /// 1 톱 · 2–3 보조 · 4–5 오른쪽 사진 · 6 둘째 띠 사진 · 7–12 헤드라인 · 13–18 오른쪽 목록 ·
    /// 19–22 사진 칸 · 23–26 글 목록 · 27–30 사진 칸 · 31~ 그 밖의 뉴스
    /// </summary>
    private static void Plan(EditionView view)
    {
        var queue = new Queue<StoryView>(view.All);

        List<StoryView> Take(int count)
        {
            var taken = new List<StoryView>(count);
            while (taken.Count < count && queue.Count > 0)
                taken.Add(queue.Dequeue());
            return taken;
        }

        view.Hero = Take(1).FirstOrDefault();
        view.Subs.AddRange(Take(2));
        view.Side.AddRange(Take(2));
        view.Photo = Take(1).FirstOrDefault();
        view.Headlines.AddRange(Take(6));
        view.Briefs.AddRange(Take(6));
        view.GridA.AddRange(Take(4));
        view.ListA.AddRange(Take(4));
        view.GridB.AddRange(Take(4));
        view.More.AddRange(queue);
    }

    /// <summary>댓글 수집본과 본문·댓글 번역본을 합친다. 번역이 없으면 원문 댓글과 원문 소개를 보여 준다.</summary>
    public static ItemView BuildItem(DateTimeOffset collectedAt, StoryView story, RawItem? raw, KoItem? ko, KoComments? koComments)
    {
        var translations = new Dictionary<long, string>();
        foreach (var comment in koComments?.Comments ?? [])
        {
            if (!string.IsNullOrWhiteSpace(comment.Text))
                translations.TryAdd(comment.Id, comment.Text);
        }

        var comments = new List<CommentView>();
        foreach (var c in raw?.Comments ?? [])
        {
            string? translated = null;
            if (c.Text is not null)
                translations.TryGetValue(c.Id, out translated);
            comments.Add(new CommentView
            {
                Id = c.Id,
                Depth = c.Depth,
                By = c.By,
                Age = RelativeAge(collectedAt, c.Time),
                Deleted = c.Deleted,
                Text = translated ?? c.Text,
            });
        }

        var fallback = story.IsSelfPost
            ? Nonblank(raw?.Text) ?? Nonblank(story.Raw.Text)
            : Nonblank(story.Raw.Description);

        return new ItemView
        {
            Collected = raw is not null,
            Translated = ko is not null,
            CommentsTranslated = koComments is not null,
            IsSelfPost = story.IsSelfPost,
            Body = Nonblank(ko?.Body) ?? fallback,
            TotalComments = raw?.TotalComments ?? story.Raw.Comments ?? 0,
            Comments = comments,
        };
    }

    internal static string RelativeAge(DateTimeOffset at, long unixTime)
    {
        var span = at - DateTimeOffset.FromUnixTimeSeconds(unixTime);
        if (span.TotalHours < 1)
            return $"{Math.Max(1, (int)span.TotalMinutes)}분 전";
        if (span.TotalDays < 1)
            return $"{(int)span.TotalHours}시간 전";
        return $"{(int)span.TotalDays}일 전";
    }

    private static string? Nonblank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
