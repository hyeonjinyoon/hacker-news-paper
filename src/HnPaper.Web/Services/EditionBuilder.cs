using HnPaper.Web.Models;

namespace HnPaper.Web.Services;

/// <summary>수집본과 번역본을 합치고 지면에 배치한다.</summary>
public static class EditionBuilder
{
    private const int GridSize = 4;

    public static EditionView Build(RawEdition raw, KoEdition? ko)
    {
        var koById = new Dictionary<long, KoStory>();
        foreach (var story in ko?.Stories ?? [])
            koById.TryAdd(story.Id, story);

        var stories = raw.Stories
            .OrderBy(s => s.Rank)
            .Select(r =>
            {
                koById.TryGetValue(r.Id, out var k);
                return new StoryView
                {
                    Raw = r,
                    Title = Nonblank(k?.Title) ?? r.Title,
                    Summary = Nonblank(k?.Summary) ?? (ko is null ? Nonblank(r.Description) : null),
                    Kicker = Nonblank(k?.Kicker) ?? DefaultKicker(r),
                    Section = Nonblank(k?.Section),
                    Placeholder = Nonblank(k?.Placeholder),
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

        var lead = ko?.Lead?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        view.Lead = lead is { Count: > 0 } ? lead
            : view.Hero?.Summary is { } summary ? [summary]
            : [];
        return view;
    }

    /// <summary>
    /// 번역본이 정한 지면을 먼저 따르고, 정원을 넘친 기사는 More로 보낸다.
    /// 지면이 없는 기사(번역 전 호 포함)는 순위·포인트·이미지를 기준으로 빈 자리를 채운다.
    /// </summary>
    private static void Plan(EditionView view)
    {
        var slots = Sections.Capacity.Keys.ToDictionary(k => k, _ => new List<StoryView>());
        var pool = new List<StoryView>();

        foreach (var story in view.All)
        {
            if (story.Section is { } section && slots.TryGetValue(section, out var slot))
            {
                if (Sections.Capacity[section] is { } cap && slot.Count >= cap)
                    view.More.Add(story);
                else
                    slot.Add(story);
            }
            else
            {
                pool.Add(story);
            }
        }

        Take(slots[Sections.Jobs], int.MaxValue, pool, pool.Where(s => s.IsJob));
        Take(slots[Sections.Hero], 1, pool, pool);
        Take(slots[Sections.Sub], 2, pool, pool);
        var withImage = pool.Where(s => s.HasImage).OrderByDescending(s => s.Raw.Points ?? 0);
        Take(slots[Sections.Side], 2, pool, withImage);
        Take(slots[Sections.Photo], 1, pool, withImage);
        Take(slots[Sections.Headline], 6, pool, pool);
        slots[Sections.Tech].AddRange(pool);

        // 1면 톱이 비면 나머지 지면에서 가장 순위가 높은 기사를 올린다.
        if (slots[Sections.Hero].Count == 0)
        {
            var best = slots.Where(kv => kv.Key != Sections.Jobs)
                .SelectMany(kv => kv.Value.Select(s => (List: kv.Value, Story: s)))
                .OrderBy(x => x.Story.Raw.Rank)
                .FirstOrDefault();
            if (best.Story is not null)
            {
                best.List.Remove(best.Story);
                slots[Sections.Hero].Add(best.Story);
            }
        }

        view.Hero = slots[Sections.Hero].FirstOrDefault();
        view.Subs.AddRange(slots[Sections.Sub]);
        view.Side.AddRange(slots[Sections.Side]);
        view.Photo = slots[Sections.Photo].FirstOrDefault();
        view.Headlines.AddRange(slots[Sections.Headline]);
        view.Opinion.AddRange(slots[Sections.Opinion]);
        view.Jobs.AddRange(slots[Sections.Jobs]);
        SplitGrid(slots[Sections.Tech], view.TechGrid, view.TechList);
        SplitGrid(slots[Sections.Life], view.LifeGrid, view.LifeList);
    }

    private static void Take(List<StoryView> slot, int cap, List<StoryView> pool, IEnumerable<StoryView> order)
    {
        foreach (var story in order.ToList())
        {
            if (slot.Count >= cap)
                break;
            slot.Add(story);
            pool.Remove(story);
        }
    }

    /// <summary>이미지 있는 기사를 우선해 사진 칸 4개를 채우고 나머지는 글 목록으로 보낸다.</summary>
    private static void SplitGrid(List<StoryView> items, List<StoryView> grid, List<StoryView> list)
    {
        var picked = items.Where(s => s.HasImage).Take(GridSize).ToList();
        picked.AddRange(items.Where(s => !picked.Contains(s)).Take(GridSize - picked.Count));
        grid.AddRange(items.Where(picked.Contains));
        list.AddRange(items.Where(s => !picked.Contains(s)));
    }

    private static string DefaultKicker(RawStory story) => story switch
    {
        { Type: "job" } => "채용",
        _ when story.Title.StartsWith("Show HN", StringComparison.Ordinal) => "Show HN",
        _ when story.Title.StartsWith("Ask HN", StringComparison.Ordinal) => "Ask HN",
        _ => "",
    };

    private static string RelativeAge(DateTimeOffset at, long unixTime)
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
