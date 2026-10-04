using System.Globalization;
using HnPaper.Web.Services;

namespace HnPaper.Web.Models;

/// <summary>수집본과 번역본을 합친, 화면에 그릴 기사 하나.</summary>
public sealed class StoryView
{
    public required string Date { get; init; }
    public required RawStory Raw { get; init; }
    public required string Title { get; init; }
    /// <summary>본문 요약의 첫 소제목 앞 문단들(최대 2개).</summary>
    public IReadOnlyList<string> Intro { get; init; } = [];
    /// <summary>1면에 쓰는 한 문단 요약. 본문 요약의 첫 문단이다.</summary>
    public string? Summary { get; init; }
    public required string Age { get; init; }

    /// <summary>기사를 누르면 가는 중간 페이지. 원문 링크는 그 페이지에 있다.</summary>
    public string Href => $"/{Date}/{Raw.Id}";
    public string SourceUrl => Raw.Url;
    public string HnUrl => $"https://news.ycombinator.com/item?id={Raw.Id}";
    public bool IsSelfPost => Raw.Url == HnUrl;
    /// <summary>대표 이미지(큰 것). 줄여 둔 파일이 있으면 /thumbs/, 없으면 원본 og 이미지 주소.</summary>
    public string? ImageUrl { get; init; }
    /// <summary>작은 썸네일. 줄여 둔 파일이 없으면 ImageUrl과 같다.</summary>
    public string? ImageSmallUrl { get; init; }
    /// <summary>줄여 둔 두 크기가 있을 때만 쓰는 srcset.</summary>
    public string? ImageSrcset { get; init; }
    public bool HasImage => !string.IsNullOrEmpty(ImageUrl);
}

public sealed record Meta(StoryView Story, bool ShowBy = false);

public sealed record EditionInfo(string Date, bool Translated);

/// <summary>
/// 지면 배치까지 끝난 한 호. 기사를 분류하지 않고 HN 순위대로 고정된 자리에 놓는다(EditionBuilder.Plan).
/// </summary>
public sealed class EditionView
{
    private static readonly CultureInfo Korean = CultureInfo.GetCultureInfo("ko-KR");

    public required string Date { get; init; }
    public required DateTimeOffset CollectedAt { get; init; }
    public required bool Translated { get; init; }
    public required IReadOnlyList<StoryView> All { get; init; }

    /// <summary>1위: 1면 톱</summary>
    public StoryView? Hero { get; set; }
    public IReadOnlyList<string> Lead { get; set; } = [];
    /// <summary>2–3위: 톱 아래 보조 기사</summary>
    public List<StoryView> Subs { get; } = [];
    /// <summary>4–5위: 1면 오른쪽 사진 기사</summary>
    public List<StoryView> Side { get; } = [];
    /// <summary>6위: 둘째 띠 왼쪽 사진 기사</summary>
    public StoryView? Photo { get; set; }
    /// <summary>7–12위: 둘째 띠 가운데 헤드라인 목록</summary>
    public List<StoryView> Headlines { get; } = [];
    /// <summary>13–18위: 둘째 띠 오른쪽 목록</summary>
    public List<StoryView> Briefs { get; } = [];
    /// <summary>19–22위: 셋째 띠 사진 칸</summary>
    public List<StoryView> GridA { get; } = [];
    /// <summary>23–26위: 셋째 띠 글 목록</summary>
    public List<StoryView> ListA { get; } = [];
    /// <summary>27–30위: 넷째 띠 사진 칸</summary>
    public List<StoryView> GridB { get; } = [];
    /// <summary>31위부터: 고정 자리를 넘친 기사</summary>
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
