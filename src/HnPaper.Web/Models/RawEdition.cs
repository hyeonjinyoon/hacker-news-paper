using System.Text.Json.Serialization;

namespace HnPaper.Web.Models;

/// <summary>수집기 출력(data/raw/{date}.json). HN 1면의 사실 정보만 담고, 번역은 담지 않는다.</summary>
public sealed record RawEdition(string Date, DateTimeOffset CollectedAt, IReadOnlyList<RawStory> Stories);

/// <param name="Type">HN 아이템 종류: story, job 등.</param>
/// <param name="Url">원문 주소. 본문만 있는 HN 글이면 HN 토론 주소.</param>
/// <param name="Points">수집할 때의 포인트. 예전 수집본에 남은 채용 글은 null.</param>
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
    string? Text)
{
    /// <summary>이보다 포인트가 적은 글은 아직 반응이 적은 글(HN이 반응을 보려고 1면에 잠깐 올린 새 글 등)로 보고 싣지 않는다.</summary>
    public const int MinPoints = 50;

    /// <summary>지면과 중간 페이지에 싣지 않는 글. 수집 단계에서 빼지만, 그 전에 수집한 호에는 남아 있을 수 있다.</summary>
    [JsonIgnore]
    public bool Excluded => IsExcluded(Type, By, Points);

    /// <summary>
    /// 싣지 않는 글: YC 회사 채용 글(type job), 매달 올라오는 구인 스레드(whoishiring 계정의 "Who is hiring?" 등),
    /// 포인트가 MinPoints에 못 미치는 글.
    /// </summary>
    public static bool IsExcluded(string? type, string? by, int? points) =>
        type == "job" || by == "whoishiring" || points < MinPoints;
}
