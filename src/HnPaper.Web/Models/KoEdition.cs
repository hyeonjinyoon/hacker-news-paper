namespace HnPaper.Web.Models;

/// <summary>hn-paper-update 스킬 출력(data/ko/{date}.json). 번역·요약·지면 배치만 담는다.</summary>
/// <param name="Lead">1면 톱 기사의 리드 문단.</param>
public sealed record KoEdition(string Date, IReadOnlyList<string>? Lead, IReadOnlyList<KoStory>? Stories);

/// <param name="Id">HN 아이템 id. RawStory.Id와 맞춘다.</param>
/// <param name="Title">원제의 직역.</param>
/// <param name="Section">지면. <see cref="Sections"/> 참고.</param>
/// <param name="Placeholder">이미지가 없을 때 썸네일 자리에 넣을 짧은 글(예: 부고 기사의 인물 이름).</param>
public sealed record KoStory(long Id, string Title, string Summary, string Kicker, string Section, string? Placeholder = null);

public static class Sections
{
    public const string Hero = "hero";
    public const string Sub = "sub";
    public const string Side = "side";
    public const string Photo = "photo";
    public const string Headline = "headline";
    public const string Opinion = "opinion";
    public const string Tech = "tech";
    public const string Life = "life";
    public const string Jobs = "jobs";

    /// <summary>지면별 최대 기사 수. null이면 제한 없음.</summary>
    public static readonly IReadOnlyDictionary<string, int?> Capacity = new Dictionary<string, int?>
    {
        [Hero] = 1,
        [Sub] = 2,
        [Side] = 2,
        [Photo] = 1,
        [Headline] = 6,
        [Opinion] = 5,
        [Tech] = null,
        [Life] = null,
        [Jobs] = null,
    };
}
