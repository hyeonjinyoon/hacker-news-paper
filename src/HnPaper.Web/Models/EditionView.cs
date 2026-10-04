using System.Globalization;
using HnPaper.Web.Services;

namespace HnPaper.Web.Models;

/// <summary>수집본과 번역본을 합친, 화면에 그릴 기사 하나.</summary>
public sealed class StoryView
{
    public required RawStory Raw { get; init; }
    public required string Title { get; init; }
    public string? Summary { get; init; }
    public required string Kicker { get; init; }
    public string? Section { get; init; }
    public string? Placeholder { get; init; }
    public required string Age { get; init; }

    public string Href => Raw.Url;
    public string HnUrl => $"https://news.ycombinator.com/item?id={Raw.Id}";
    public bool IsJob => Raw.Type == "job";
    public bool HasImage => !string.IsNullOrEmpty(Raw.Image);
    public bool IsMemorial => Kicker == "부고" && !HasImage;
    public string PlaceholderText => Placeholder ?? Raw.Site;
}

public sealed record Meta(StoryView Story, bool ShowBy = false);

public sealed record EditionInfo(string Date, bool Translated);

/// <summary>지면 배치까지 끝난 한 호.</summary>
public sealed class EditionView
{
    private static readonly CultureInfo Korean = CultureInfo.GetCultureInfo("ko-KR");

    public required string Date { get; init; }
    public required DateTimeOffset CollectedAt { get; init; }
    public required bool Translated { get; init; }
    public required IReadOnlyList<StoryView> All { get; init; }

    public StoryView? Hero { get; set; }
    public IReadOnlyList<string> Lead { get; set; } = [];
    public List<StoryView> Subs { get; } = [];
    public List<StoryView> Side { get; } = [];
    public StoryView? Photo { get; set; }
    public List<StoryView> Headlines { get; } = [];
    public List<StoryView> Opinion { get; } = [];
    public List<StoryView> TechGrid { get; } = [];
    public List<StoryView> TechList { get; } = [];
    public List<StoryView> LifeGrid { get; } = [];
    public List<StoryView> LifeList { get; } = [];
    public List<StoryView> Jobs { get; } = [];
    /// <summary>지면 정원을 넘쳐 밀려난 기사.</summary>
    public List<StoryView> More { get; } = [];

    public IEnumerable<StoryView> ByPoints =>
        All.Where(s => s.Raw.Points > 0).OrderByDescending(s => s.Raw.Points).Take(10);

    public IEnumerable<StoryView> ByComments =>
        All.Where(s => s.Raw.Comments > 0).OrderByDescending(s => s.Raw.Comments).Take(5);

    public string DateLabel
    {
        get
        {
            var at = TimeZoneInfo.ConvertTime(CollectedAt, Kst.Zone);
            return $"{at.ToString("yyyy년 M월 d일 dddd", Korean)} · {at.ToString("tt h시", Korean)} 기준";
        }
    }
}
