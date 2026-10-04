namespace HnPaper.Web.Models;

/// <summary>
/// hn-paper-update 스킬 출력(data/ko/{date}.json). 제목 번역만 담는다.
/// 1면의 요약·리드는 기사별 본문(data/ko/{date}/{id}.json)의 첫 문단을 쓴다.
/// </summary>
/// <param name="Lead">예전 호에서 따로 쓰던 1면 리드. 본문이 없을 때만 대신 쓴다.</param>
public sealed record KoEdition(string Date, IReadOnlyList<string>? Lead, IReadOnlyList<KoStory>? Stories);

/// <param name="Id">HN 아이템 id. RawStory.Id와 맞춘다.</param>
/// <param name="Title">원제의 뉘앙스를 살려 한국어(합니다체·해요체)로 옮긴 제목.</param>
/// <param name="Summary">예전 호에서 따로 쓰던 1면 요약. 본문이 없을 때만 대신 쓴다.</param>
public sealed record KoStory(long Id, string Title, string? Summary = null);
