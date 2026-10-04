namespace HnPaper.Web.Models;

/// <summary>
/// 기사별 수집본(data/raw/{date}/{id}.json). HN 본문 글과 댓글을 HN 표시 순서(답글 포함, 깊이 우선)로 담는다.
/// </summary>
/// <param name="TotalComments">HN에 달린 전체 댓글 수. Comments는 그중 앞에서부터 최대 N개.</param>
/// <param name="Text">Ask HN·Show HN 같은 HN 본문 글의 전문(마크다운). 외부 링크 기사는 null.</param>
public sealed record RawItem(long Id, int TotalComments, string? Text, IReadOnlyList<RawComment> Comments);

/// <param name="Parent">부모 아이템 id. 최상위 댓글이면 기사 id.</param>
/// <param name="Depth">0이 최상위 댓글.</param>
/// <param name="Text">댓글 본문(마크다운). 삭제된 댓글이면 null.</param>
/// <param name="Deleted">삭제됐지만 답글이 있어 자리만 남긴 댓글.</param>
public sealed record RawComment(long Id, long Parent, int Depth, string By, long Time, string? Text, bool Deleted);

/// <summary>기사별 본문 번역본(data/ko/{date}/{id}.json). hn-paper-article 서브에이전트가 쓴다.</summary>
/// <param name="Body">중간 페이지 본문(마크다운). 외부 기사는 한국어 요약, HN 본문 글은 전문 번역.</param>
public sealed record KoItem(long Id, string Body);

/// <summary>기사별 댓글 번역본(data/ko/{date}/{id}.comments.json). hn-paper-comments 서브에이전트가 쓴다.</summary>
public sealed record KoComments(long Id, IReadOnlyList<KoComment>? Comments);

public sealed record KoComment(long Id, string Text);

/// <summary>중간 페이지에 그릴 기사 본문과 댓글.</summary>
public sealed class ItemView
{
    public required bool Collected { get; init; }
    /// <summary>본문 번역본이 있다.</summary>
    public required bool Translated { get; init; }
    /// <summary>댓글 번역본이 있다.</summary>
    public required bool CommentsTranslated { get; init; }
    public required bool IsSelfPost { get; init; }
    public string? Body { get; init; }
    public required int TotalComments { get; init; }
    public required IReadOnlyList<CommentView> Comments { get; init; }
}

public sealed class CommentView
{
    public required long Id { get; init; }
    public required int Depth { get; init; }
    public required string By { get; init; }
    /// <summary>HN 작성 시각(유닉스 초). "3시간 전" 같은 표기는 화면에서 보는 시각 기준으로 계산한다(_Ago).</summary>
    public required long Time { get; init; }
    public required bool Deleted { get; init; }
    /// <summary>번역문. 번역 전이면 원문.</summary>
    public string? Text { get; init; }

    public string HnUrl => $"https://news.ycombinator.com/item?id={Id}";
}
