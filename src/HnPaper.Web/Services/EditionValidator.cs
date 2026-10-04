using System.Text.RegularExpressions;
using HnPaper.Web.Models;

namespace HnPaper.Web.Services;

/// <summary>스킬이 쓴 번역본(data/ko)이 수집본과 번역 규칙에 맞는지 검사한다.</summary>
public static partial class EditionValidator
{
    private const int MaxBody = 3000;

    private static readonly string[] KeptPrefixes = ["Show HN:", "Ask HN:", "Tell HN:", "Launch HN:"];

    public static (List<string> Errors, List<string> Warnings) Validate(RawEdition raw, KoEdition ko)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var stories = ko.Stories ?? [];

        if (ko.Date != raw.Date)
            errors.Add($"date가 수집본({raw.Date})과 다릅니다: {ko.Date}");

        foreach (var dup in stories.GroupBy(s => s.Id).Where(g => g.Count() > 1))
            errors.Add($"id {dup.Key}가 {dup.Count()}번 나옵니다.");

        var rawById = raw.Stories.ToDictionary(s => s.Id);
        var koIds = stories.Select(s => s.Id).ToHashSet();
        foreach (var missing in raw.Stories.Where(s => !s.IsJob && !koIds.Contains(s.Id)))
            errors.Add($"{missing.Rank}위(id {missing.Id}) 번역이 없습니다: {missing.Title}");

        foreach (var story in stories)
        {
            if (!rawById.TryGetValue(story.Id, out var source))
            {
                errors.Add($"수집본에 없는 id입니다: {story.Id}");
                continue;
            }
            var label = $"{source.Rank}위(id {story.Id})";

            if (string.IsNullOrWhiteSpace(story.Title))
                errors.Add($"{label}: title이 비었습니다.");

            foreach (var prefix in KeptPrefixes)
            {
                if (source.Title.StartsWith(prefix, StringComparison.Ordinal) && !story.Title.StartsWith(prefix, StringComparison.Ordinal))
                    errors.Add($"{label}: 원제의 \"{prefix}\" 접두어를 그대로 두어야 합니다.");
            }

            var year = TrailingYear().Match(source.Title);
            if (year.Success && !story.Title.Contains(year.Value, StringComparison.Ordinal))
                errors.Add($"{label}: 원제의 연도 표기 {year.Value}를 그대로 두어야 합니다.");

            // 제목 문체: 합니다체로 통일하고 마침표를 찍지 않는다. 끝의 (2016)·[영상]은 떼고 본다.
            var body = TrailingNote().Replace(story.Title, "").TrimEnd();
            if (body.EndsWith('.'))
                warnings.Add($"{label}: 제목 끝에 마침표를 찍지 않습니다: {story.Title}");
            else if (PlainEnding().IsMatch(body))
                warnings.Add($"{label}: 문장형 제목은 합니다체로 씁니다: {story.Title}");
        }

        return (errors, warnings);
    }

    /// <summary>
    /// 중간 페이지 번역본을 검사한다. 본문(data/ko/{date}/{id}.json)은 외부 기사면 요약이어야 하므로 길이를 제한하고,
    /// 댓글(data/ko/{date}/{id}.comments.json)은 수집한 댓글이 하나도 빠짐없이 번역돼 있어야 한다.
    /// </summary>
    /// <param name="part">"body"나 "comments"면 그쪽만 검사한다. 본문·댓글을 따로 쓰는 서브에이전트가 자기 몫만 확인할 때 쓴다.</param>
    public static void ValidateItem(RawStory story, RawItem? raw, KoItem? ko, KoComments? koComments,
        List<string> errors, List<string> warnings, string? part = null)
    {
        var label = $"{story.Rank}위(id {story.Id}) 중간 페이지";
        if (raw is null)
            warnings.Add($"{label}: 댓글 수집본이 없습니다(collect-items로 수집).");
        if (part is null or "body")
            ValidateBody(label, story, raw, ko, errors, warnings);
        if (part is null or "comments")
            ValidateComments(label, story, raw, koComments, errors);
    }

    private static void ValidateBody(string label, RawStory story, RawItem? raw, KoItem? ko, List<string> errors, List<string> warnings)
    {
        if (ko is null)
        {
            errors.Add($"{label}: 본문 번역본(data/ko/{{날짜}}/{story.Id}.json)이 없습니다.");
            return;
        }

        if (ko.Id != story.Id)
            errors.Add($"{label}: id가 다릅니다: {ko.Id}");
        if (string.IsNullOrWhiteSpace(ko.Body))
        {
            errors.Add($"{label}: body가 비었습니다.");
        }
        else
        {
            // HN 본문 글은 전문 번역이라 원문 길이에 맞춰 늘려 준다.
            var limit = Math.Max(MaxBody, (raw?.Text?.Length ?? 0) * 2);
            if (ko.Body.Length > limit)
                errors.Add($"{label}: body가 {ko.Body.Length}자입니다(최대 {limit}자). 외부 기사는 전문 번역이 아니라 요약을 씁니다.");

            // 틀(한 줄 요약·소제목·글머리표·HN 반응)과 합니다체
            SummaryLint.Check(label, story, raw, ko.Body, errors, warnings);
        }
    }

    private static void ValidateComments(string label, RawStory story, RawItem? raw, KoComments? ko, List<string> errors)
    {
        if (raw is null)
            return;
        var expected = raw.Comments.Where(c => !c.Deleted).Select(c => c.Id).ToHashSet();
        if (ko is null)
        {
            if (expected.Count > 0)
                errors.Add($"{label}: 댓글 번역본(data/ko/{{날짜}}/{story.Id}.comments.json)이 없습니다.");
            return;
        }
        if (ko.Id != story.Id)
            errors.Add($"{label}: 댓글 번역본 id가 다릅니다: {ko.Id}");

        var translated = new HashSet<long>();
        foreach (var comment in ko.Comments ?? [])
        {
            if (!expected.Contains(comment.Id))
                errors.Add($"{label}: 수집본에 없는 댓글 id입니다: {comment.Id}");
            else if (!translated.Add(comment.Id))
                errors.Add($"{label}: 댓글 {comment.Id}가 두 번 나옵니다.");
            if (string.IsNullOrWhiteSpace(comment.Text))
                errors.Add($"{label}: 댓글 {comment.Id} 번역이 비었습니다.");
        }
        var missing = expected.Where(id => !translated.Contains(id)).ToList();
        if (missing.Count > 0)
            errors.Add($"{label}: 댓글 {missing.Count}개가 번역되지 않았습니다(예: {string.Join(", ", missing.Take(3))}).");
    }

    [GeneratedRegex(@"\((?:19|20)\d{2}\)")]
    private static partial Regex TrailingYear();

    // 제목 끝에 덧붙은 (2016), [영상] 같은 괄호
    [GeneratedRegex(@"(?:\s*(?:\([^)]*\)|\[[^\]]*\]))+$")]
    private static partial Regex TrailingNote();

    // 합니다체가 아닌 문장 끝: ~한다/~했다(~니다 제외), ~요/~죠, ~는가?/~냐?/~나?/~니?
    [GeneratedRegex(@"(?:(?<!니)다|[요죠]|(?:는가|냐|나|니)\?)[?!]?$")]
    private static partial Regex PlainEnding();
}
