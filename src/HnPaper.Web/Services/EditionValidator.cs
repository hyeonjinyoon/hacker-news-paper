using System.Text.RegularExpressions;
using HnPaper.Web.Models;

namespace HnPaper.Web.Services;

/// <summary>스킬이 쓴 번역본(data/ko)이 수집본과 지면 규칙에 맞는지 검사한다.</summary>
public static partial class EditionValidator
{
    private const int MaxSummary = 220;
    private const int MaxKicker = 10;
    private const int MaxLeadParagraph = 450;

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
        foreach (var missing in raw.Stories.Where(s => !koIds.Contains(s.Id)))
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
            if (string.IsNullOrWhiteSpace(story.Summary))
                errors.Add($"{label}: summary가 비었습니다.");
            else if (story.Summary.Length > MaxSummary)
                errors.Add($"{label}: summary가 {story.Summary.Length}자입니다(최대 {MaxSummary}자).");
            if (string.IsNullOrWhiteSpace(story.Kicker))
                errors.Add($"{label}: kicker가 비었습니다.");
            else if (story.Kicker.Length > MaxKicker)
                errors.Add($"{label}: kicker가 너무 깁니다(최대 {MaxKicker}자): {story.Kicker}");

            if (!Sections.Capacity.ContainsKey(story.Section ?? ""))
                errors.Add($"{label}: 알 수 없는 section입니다: {story.Section}");
            else if (source.Type == "job" && story.Section != Sections.Jobs)
                errors.Add($"{label}: 채용 글은 section이 jobs여야 합니다.");
            else if (source.Type != "job" && story.Section == Sections.Jobs)
                errors.Add($"{label}: 채용 글이 아닌데 section이 jobs입니다.");

            if (story.Section is Sections.Side or Sections.Photo && string.IsNullOrEmpty(source.Image))
                warnings.Add($"{label}: 이미지가 없는 글을 {story.Section}에 배치했습니다.");

            foreach (var prefix in KeptPrefixes)
            {
                if (source.Title.StartsWith(prefix, StringComparison.Ordinal) && !story.Title.StartsWith(prefix, StringComparison.Ordinal))
                    errors.Add($"{label}: 원제의 \"{prefix}\" 접두어를 그대로 두어야 합니다.");
            }

            var year = TrailingYear().Match(source.Title);
            if (year.Success && !story.Title.Contains(year.Value, StringComparison.Ordinal))
                errors.Add($"{label}: 원제의 연도 표기 {year.Value}를 그대로 두어야 합니다.");
        }

        foreach (var (section, cap) in Sections.Capacity)
        {
            var count = stories.Count(s => s.Section == section);
            if (cap is { } max && count > max)
                errors.Add($"{section} 지면에 {count}개를 배치했습니다(최대 {max}개).");
        }
        var heroes = stories.Count(s => s.Section == Sections.Hero);
        if (heroes != 1)
            errors.Add($"hero는 정확히 1개여야 합니다(현재 {heroes}개).");

        var lead = ko.Lead ?? [];
        if (lead.Count is < 1 or > 3)
            errors.Add($"lead 문단은 1~3개여야 합니다(현재 {lead.Count}개).");
        foreach (var (paragraph, i) in lead.Select((p, i) => (p, i + 1)))
        {
            if (string.IsNullOrWhiteSpace(paragraph))
                errors.Add($"lead {i}번째 문단이 비었습니다.");
            else if (paragraph.Length > MaxLeadParagraph)
                errors.Add($"lead {i}번째 문단이 {paragraph.Length}자입니다(최대 {MaxLeadParagraph}자).");
        }

        return (errors, warnings);
    }

    [GeneratedRegex(@"\((?:19|20)\d{2}\)")]
    private static partial Regex TrailingYear();
}
