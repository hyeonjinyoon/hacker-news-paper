using System.Text.RegularExpressions;
using HnPaper.Web.Models;

namespace HnPaper.Web.Services;

/// <summary>
/// 중간 페이지 본문이 hn-paper-article 지침의 틀을 지키는지 검사한다.
/// 굵은 한 줄 요약 → 도입 → ### 왜 중요한가 → ### 핵심 내용 → ### HN 반응, 합니다체, 분량.
/// </summary>
public static partial class SummaryLint
{
    public const string WhyHeading = "왜 중요한가";
    public const string PointsHeading = "핵심 내용";
    public const string AuthorHeading = "작성자 설명";
    public const string ReactionHeading = "HN 반응";
    /// <summary>원문을 읽지 못한 기사는 이 문장을 넣고, 왜 중요한가·핵심 내용을 생략할 수 있다.</summary>
    public const string UnreadableMarker = "원문을 읽지 못했습니다";

    private const int MinCommentsForReaction = 5;
    private const int MaxIntro = 220;
    private const int SoftMaxSummary = 1100;
    private const int MaxSummary = 1500;
    private const int MaxBullet = 100;

    private static readonly string[] HeadingOrder = [WhyHeading, PointsHeading, AuthorHeading, ReactionHeading];

    public static void Check(string label, RawStory story, RawItem? raw, string body, List<string> errors, List<string> warnings)
    {
        var isSelfPost = story.Url.StartsWith("https://news.ycombinator.com/item?id=", StringComparison.Ordinal);
        var doc = Parse(body);

        // 1. 굵은 한 줄 요약(1면 요약으로도 쓰인다)
        if (doc.OneLiner is null)
            errors.Add($"{label}: 본문 첫 줄은 **굵은 한 줄 요약**이어야 합니다.");
        else if (doc.OneLiner.Length is < 15 or > 80)
            errors.Add($"{label}: 한 줄 요약이 {doc.OneLiner.Length}자입니다(15~80자).");

        // 2. 외부 기사 요약의 틀과 분량
        if (!isSelfPost && !body.Contains(UnreadableMarker, StringComparison.Ordinal))
        {
            if (doc.IntroLength == 0)
                errors.Add($"{label}: 한 줄 요약 다음에 도입 문단이 없습니다.");
            else if (doc.IntroLength > MaxIntro)
                errors.Add($"{label}: 도입 문단이 {doc.IntroLength}자입니다(최대 {MaxIntro}자, 2문장 안팎).");
            RequireSection(doc, label, WhyHeading, 1, 3, errors);
            RequireSection(doc, label, PointsHeading, 3, 5, errors);
            if (body.Length > MaxSummary)
                errors.Add($"{label}: 요약이 {body.Length}자입니다(최대 {MaxSummary}자, 목표 500~1,000자).");
            else if (body.Length > SoftMaxSummary)
                warnings.Add($"{label}: 요약이 {body.Length}자입니다(목표 500~1,000자).");
        }

        // 3. HN 반응: 댓글이 어느 정도 있으면 반드시 넣는다.
        var comments = raw?.Comments.Count(c => !c.Deleted) ?? 0;
        if (comments >= MinCommentsForReaction)
            RequireSection(doc, label, ReactionHeading, 1, 2, errors);

        // 4. 소제목은 정해진 것만, 정해진 순서로
        var known = doc.Sections.Select(s => s.Heading).Where(h => HeadingOrder.Contains(h)).ToList();
        if (!known.SequenceEqual(known.OrderBy(h => Array.IndexOf(HeadingOrder, h))))
            errors.Add($"{label}: 소제목 순서는 {string.Join(" → ", HeadingOrder)}입니다.");
        foreach (var unknown in doc.Sections.Select(s => s.Heading).Where(h => !HeadingOrder.Contains(h)))
            warnings.Add($"{label}: 틀에 없는 소제목입니다: ### {unknown}");

        // 5. 글머리표는 한 문장으로 짧게
        var longBullets = doc.Sections.SelectMany(s => s.Bullets).Where(b => b.Length > MaxBullet).ToList();
        if (longBullets.Count > 0)
            warnings.Add($"{label}: {MaxBullet}자가 넘는 글머리표가 {longBullets.Count}개 있습니다(예: {Clip(longBullets[0])}).");

        // 6. 합니다체
        var plain = doc.Sentences.Where(s => PlainEnding().IsMatch(s)).ToList();
        if (plain.Count > 0)
            errors.Add($"{label}: 합니다체가 아닌 문장이 {plain.Count}개 있습니다(예: {string.Join(" / ", plain.Take(2).Select(Clip))}).");
    }

    private static void RequireSection(Doc doc, string label, string heading, int min, int max, List<string> errors)
    {
        var section = doc.Sections.FirstOrDefault(s => s.Heading == heading);
        if (section is null)
            errors.Add($"{label}: ### {heading} 소제목이 없습니다.");
        else if (section.Bullets.Count < min || section.Bullets.Count > max)
            errors.Add($"{label}: ### {heading} 글머리표가 {section.Bullets.Count}개입니다({min}~{max}개).");
    }

    private sealed record Section(string Heading, List<string> Bullets);

    private sealed record Doc(string? OneLiner, int IntroLength, List<Section> Sections, List<string> Sentences);

    /// <summary>스킬이 쓰는 단순한 마크다운(굵은 첫 줄, 문단, ### 소제목, - 글머리표, 코드 블록, 인용)을 줄 단위로 읽는다.</summary>
    private static Doc Parse(string body)
    {
        string? oneLiner = null;
        var introLength = 0;
        var sections = new List<Section>();
        var sentences = new List<string>();
        var inCode = false;
        var sawFirst = false;

        foreach (var rawLine in body.Replace("\r", "").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inCode = !inCode;
                continue;
            }
            if (inCode || line.Length == 0)
                continue;

            if (!sawFirst)
            {
                sawFirst = true;
                var bold = OneLinerPattern().Match(line);
                if (bold.Success)
                {
                    oneLiner = bold.Groups[1].Value.Trim();
                    AddSentences(oneLiner, sentences);
                    continue;
                }
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                sections.Add(new Section(line[4..].Trim(), []));
                continue;
            }
            if (line.StartsWith('>'))
                continue;   // 인용은 남의 말이라 문체 검사에서 뺀다

            var bullet = BulletPattern().Match(line);
            var text = bullet.Success ? bullet.Groups[1].Value.Trim() : line;
            if (bullet.Success && sections.Count > 0)
                sections[^1].Bullets.Add(StripMarks(text));
            else if (sections.Count == 0)
                introLength += StripMarks(text).Length;
            AddSentences(StripMarks(text), sentences);
        }

        return new Doc(oneLiner, introLength, sections, sentences);
    }

    private static void AddSentences(string text, List<string> sentences)
    {
        foreach (var sentence in SentenceSplit().Split(text))
        {
            var trimmed = TrailingQuotes().Replace(sentence.Trim(), "");
            if (trimmed.Length > 0)
                sentences.Add(trimmed);
        }
    }

    private static string StripMarks(string text) => text.Replace("**", "").Replace("`", "");

    private static string Clip(string text) => text.Length <= 40 ? text : text[..40] + "…";

    [GeneratedRegex(@"^\*\*(.+)\*\*$")]
    private static partial Regex OneLinerPattern();

    [GeneratedRegex(@"^(?:[-*]|\d+\.)\s+(.*)$")]
    private static partial Regex BulletPattern();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceSplit();

    [GeneratedRegex(@"[""'”’)\]]+$")]
    private static partial Regex TrailingQuotes();

    // 합니다체가 아닌 끝: ~한다/~했다/~이다(~니다 제외), ~요/~죠
    [GeneratedRegex(@"(?:(?<!니)다|[요죠])[.!?]?$")]
    private static partial Regex PlainEnding();
}
