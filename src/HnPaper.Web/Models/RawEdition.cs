namespace HnPaper.Web.Models;

/// <summary>수집기 출력(data/raw/{date}.json). HN 1면의 사실 정보만 담고, 번역은 담지 않는다.</summary>
public sealed record RawEdition(string Date, DateTimeOffset CollectedAt, IReadOnlyList<RawStory> Stories);

/// <param name="Type">HN 아이템 종류: story, job 등.</param>
/// <param name="Url">원문 주소. 본문만 있는 HN 글이면 HN 토론 주소.</param>
/// <param name="Points">채용 글은 null.</param>
/// <param name="Time">HN 제출 시각(유닉스 초).</param>
/// <param name="Image">원문의 og:image.</param>
/// <param name="Description">원문의 og:description.</param>
/// <param name="Text">HN 본문 글(Ask HN 등)의 텍스트.</param>
public sealed record RawStory(
    int Rank,
    long Id,
    string Type,
    string Title,
    string Url,
    string Site,
    int? Points,
    int? Comments,
    string By,
    long Time,
    string? Image,
    string? Description,
    string? Text);
