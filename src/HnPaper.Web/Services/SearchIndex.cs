using System.Runtime.CompilerServices;
using System.Text;
using HnPaper.Web.Models;
using Microsoft.AspNetCore.Html;

namespace HnPaper.Web.Services;

/// <summary>
/// 모든 호의 기사에서 검색어를 찾는다. 찾는 곳은 제목(한국어 제목과 원제)과 콘텐츠(중간 페이지 본문 번역본, 번역 전이면 1면 요약이나 원문 설명)다.
/// 띄어 쓴 낱말마다 제목이나 콘텐츠 어딘가에 들어 있는 기사만 고른다. 대소문자는 가리지 않고, 낱말의 일부만 맞아도 된다("애플"로 "애플이"를 찾는다).
/// 낱말이 조금 달라도 찾는다.
/// - 띄어쓰기: "오픈소스"로 "오픈 소스"도 찾는다. 그대로 맞은 것보다 점수를 낮게 준다.
/// - 오타: 어느 기사에도 없는 낱말이면, 모든 기사에 나오는 낱말 가운데 자모가 가장 적게 다른 것으로 대신 찾는다
///   ("쿠버네테스" → "쿠버네티스", "깃헙" → "깃허브", "kubernets" → "kubernetes"). 맞는 기사가 있는 낱말은 고치지 않는다.
/// 같은 기사가 며칠 동안 1면에 남아 여러 호에 실리면 하나만 보여 준다(본문 번역본이 있는 가장 최근 호의 것).
/// </summary>
public sealed class SearchIndex(EditionStore store)
{
    public const int MaxQueryLength = 100;
    private const int MaxTerms = 8;
    /// <summary>제목에 든 낱말 하나의 점수. 콘텐츠에 든 낱말은 나온 횟수(최대 MaxBodyHits)만큼 1점씩이다.</summary>
    private const int TitleScore = 10;
    private const int MaxBodyHits = 5;
    /// <summary>오타를 고칠 때 낱말 하나 대신 찾는 비슷한 낱말의 최대 수.</summary>
    private const int MaxSimilar = 3;

    private sealed record Doc(StoryView Story, string Title, string Original, string Text)
    {
        // 띄어쓰기를 무시하고 찾을 때 쓰는, 공백을 모두 뺀 꼴
        public string CompactTitle { get; } = Compact(Title);
        public string CompactOriginal { get; } = Compact(Original);
        public string CompactText { get; } = Compact(Text);
    }

    /// <summary>기사에 나오는 낱말(소문자). Jamo는 자모로 푼 꼴, Ends[i]는 i번째 글자까지의 자모 수, Count는 나온 횟수.</summary>
    private readonly record struct Word(string Text, string Jamo, int[] Ends, int Count);

    /// <summary>오타를 고칠 후보 낱말 목록과, 그것을 만든 호들.</summary>
    private sealed record Vocabulary(EditionView[] Editions, Word[] Words);

    // 호마다 만든 검색 문서. EditionStore는 파일이 바뀐 호만 새 EditionView로 다시 만들므로, 호 객체에 붙여 두면
    // 바뀌지 않은 호의 문서는 그대로 쓰고, 버려진 옛 호 객체의 문서는 함께 사라진다.
    private readonly ConditionalWeakTable<EditionView, IReadOnlyList<Doc>> _docs = new();

    // 낱말 목록은 오타를 고칠 때만 만들고, 호가 바뀌기 전까지 다시 쓴다.
    private readonly Lock _vocabularyLock = new();
    private Vocabulary? _vocabulary;

    /// <summary>검색어를 NFC로 맞추고, 연이은 공백을 하나로 줄이고, 길이를 MaxQueryLength까지로 자른다.</summary>
    public static string Clean(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "";
        var text = string.Join(' ', Nfc(query).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length <= MaxQueryLength)
            return text;
        var end = char.IsHighSurrogate(text[MaxQueryLength - 1]) ? MaxQueryLength - 1 : MaxQueryLength;
        return text[..end].TrimEnd();
    }

    /// <param name="query">Clean을 거친 검색어.</param>
    /// <param name="recent">true면 최신 호 순, false면 정확도(점수) 순.</param>
    /// <param name="page">1부터 센다. 범위를 벗어나면 첫 쪽이나 마지막 쪽으로 맞춘다.</param>
    public SearchResults Search(string query, bool recent, int page, int pageSize)
    {
        var words = query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTerms)
            .ToList();
        if (words.Count == 0)
            return SearchResults.Empty;

        // List()는 최근 호부터 준다. 같은 기사는 먼저 나온(최근) 것을 두되, 그 호에 아직 본문 번역본이 없으면
        // 본문이 있는 지난 호의 것으로 바꾼다(아침 업데이트 중 번역이 덜 된 호).
        var editions = new List<EditionView>();
        var docs = new Dictionary<long, Doc>();
        foreach (var info in store.List())
        {
            if (store.Load(info.Date) is not { } edition)
                continue;
            editions.Add(edition);
            foreach (var doc in _docs.GetValue(edition, Build))
            {
                if (!docs.TryGetValue(doc.Story.Raw.Id, out var kept) || kept.Story.Body is null && doc.Story.Body is not null)
                    docs[doc.Story.Raw.Id] = doc;
            }
        }

        // 어느 기사에도 없는 낱말은 오타로 보고 비슷한 낱말로 바꿔 찾는다.
        var terms = words
            .Select(w => docs.Values.Any(d => FormScore(d, w) > 0)
                ? new SearchTerm(w)
                : new SearchTerm(w, Similar(w, VocabularyFor(editions))))
            .ToList();

        var hits = new List<(Doc Doc, int Score)>();
        foreach (var doc in docs.Values)
        {
            if (Score(doc, terms) is { } score)
                hits.Add((doc, score));
        }

        var ordered = recent
            ? hits.OrderByDescending(h => h.Doc.Story.Date, StringComparer.Ordinal).ThenBy(h => h.Doc.Story.Raw.Rank)
            : hits.OrderByDescending(h => h.Score).ThenByDescending(h => h.Doc.Story.Date, StringComparer.Ordinal).ThenBy(h => h.Doc.Story.Raw.Rank);

        var pageCount = (hits.Count + pageSize - 1) / pageSize;
        page = Math.Clamp(page, 1, Math.Max(1, pageCount));
        var shown = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(h => new SearchHit(h.Doc.Story, h.Doc.Title, h.Doc.Original, Snippet(h.Doc.Text, terms)))
            .ToList();
        return new SearchResults(terms, hits.Count, page, pageCount, shown);
    }

    /// <summary>모든 낱말이 제목이나 콘텐츠에 있으면 점수, 하나라도 없으면 null. 낱말마다 가장 잘 맞은 꼴의 점수를 더한다.</summary>
    private static int? Score(Doc doc, IReadOnlyList<SearchTerm> terms)
    {
        var score = 0;
        foreach (var term in terms)
        {
            var best = 0;
            foreach (var form in term.Forms)
                best = Math.Max(best, FormScore(doc, form));
            if (best == 0)
                return null;
            score += best;
        }
        return score;
    }

    /// <summary>그대로 맞으면 그 점수, 띄어쓰기만 달라서 맞으면 그 점수의 반(올림), 안 맞으면 0.</summary>
    private static int FormScore(Doc doc, string form)
    {
        var exact = Points(doc.Title, doc.Original, doc.Text, form);
        if (exact > 0 || !Loose(form))
            return exact;
        return (Points(doc.CompactTitle, doc.CompactOriginal, doc.CompactText, form) + 1) / 2;
    }

    private static int Points(string title, string original, string text, string form) =>
        (title.Contains(form, StringComparison.OrdinalIgnoreCase) || original.Contains(form, StringComparison.OrdinalIgnoreCase) ? TitleScore : 0)
        + Count(text, form, MaxBodyHits);

    private static int Count(string text, string term, int max)
    {
        var count = 0;
        for (var i = text.IndexOf(term, StringComparison.OrdinalIgnoreCase); i >= 0 && count < max;
             i = text.IndexOf(term, i + term.Length, StringComparison.OrdinalIgnoreCase))
            count++;
        return count;
    }

    /// <summary>
    /// 띄어쓰기를 무시하고 찾아도 되는 꼴. 한글이 든 것은 세 글자 이상, 그 밖(영어 등)은 여섯 글자 이상이다.
    /// 짧은 영어 낱말은 두 낱말에 걸쳐 우연히 맞기 쉬워서("guru stated" 안의 "rust") 뺀다.
    /// </summary>
    private static bool Loose(string form) => form.Length >= (form.Any(IsHangul) ? 3 : 6);

    private static bool IsHangul(char c) => c is >= '가' and <= '힣';

    private static IReadOnlyList<Doc> Build(EditionView edition) =>
        edition.All
            .Select(s => new Doc(
                s,
                Nfc(s.Title),
                Nfc(s.Raw.Title),
                Nfc(s.Body is null
                    ? s.Summary ?? s.Raw.Description ?? ""
                    : string.Join('\n', MarkdownRenderer.TextBlocks(s.Body)))))
            .ToList();

    /// <summary>
    /// 한글을 완성형(NFC)으로 맞춘다. macOS에서 복사한 글 등은 자모가 풀린 꼴(NFD)일 수 있어 그대로는 서로 찾지 못한다.
    /// 짝이 없는 서로게이트처럼 맞출 수 없는 글은 그대로 둔다.
    /// </summary>
    private static string Nfc(string text)
    {
        try
        {
            return text.Normalize();
        }
        catch (ArgumentException)
        {
            return text;
        }
    }

    private static string Compact(string text)
    {
        if (!text.Any(char.IsWhiteSpace))
            return text;
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (!char.IsWhiteSpace(c))
                builder.Append(c);
        }
        return builder.ToString();
    }

    // ── 오타 고치기 ──────────────────────────────────────────────

    private Vocabulary VocabularyFor(List<EditionView> editions)
    {
        lock (_vocabularyLock)
        {
            if (_vocabulary is { } cached && cached.Editions.SequenceEqual(editions, ReferenceEqualityComparer.Instance))
                return cached;

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var doc in editions.SelectMany(e => _docs.GetValue(e, Build)))
            {
                foreach (var text in new[] { doc.Title, doc.Original, doc.Text })
                {
                    foreach (var word in Words(text))
                        counts[word] = counts.GetValueOrDefault(word) + 1;
                }
            }
            var words = counts
                .Select(w =>
                {
                    var ends = new int[w.Key.Length];
                    return new Word(w.Key, Jamo(w.Key, ends), ends, w.Value);
                })
                .ToArray();
            return _vocabulary = new Vocabulary(editions.ToArray(), words);
        }
    }

    /// <summary>글자·숫자가 이어진 덩어리를 소문자로 꺼낸다. 한 글자짜리와 아주 긴 것(주소 조각 등)은 뺀다.</summary>
    private static IEnumerable<string> Words(string text)
    {
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && char.IsLetterOrDigit(text[i]))
            {
                if (start < 0)
                    start = i;
                continue;
            }
            if (start >= 0 && i - start is >= 2 and <= 40)
                yield return text[start..i].ToLowerInvariant();
            start = -1;
        }
    }

    /// <summary>
    /// 낱말 목록에서 term과 자모가 가장 적게 다른 낱말(의 앞부분)을 찾는다. "쿠버네티스를"에서는 "쿠버네티스"를 꺼낸다.
    /// 허용하는 차이는 자모 수에 따라 3개 이하 0, 4–7개 1, 8개 이상 2다. 같은 차이면 기사에 자주 나온 낱말부터 고른다.
    /// </summary>
    private static IReadOnlyList<string> Similar(string term, Vocabulary vocabulary)
    {
        var jamo = Jamo(term.ToLowerInvariant());
        var k = jamo.Length switch { < 4 => 0, < 8 => 1, _ => 2 };
        if (k == 0)
            return [];

        // "나무"가 "남"처럼 한 글자로 줄어 아무 데나 맞지 않게, 고친 낱말은 두 글자 이상이고 원래보다 한 글자 넘게 짧지 않아야 한다.
        var minChars = Math.Max(2, term.Length - 1);
        var d = new int[jamo.Length + 1, jamo.Length + k + 1];
        var found = new Dictionary<string, (int Distance, int Count)>(StringComparer.Ordinal);
        foreach (var word in vocabulary.Words)
        {
            if (word.Jamo.Length < jamo.Length - k || word.Text.Length < minChars)
                continue;
            var (distance, chars) = PrefixDistance(jamo, word, k, d);
            if (distance == 0 || distance > k || chars < minChars)
                continue;
            var fragment = word.Text[..chars];
            found[fragment] = found.TryGetValue(fragment, out var seen) && seen.Distance <= distance
                ? (seen.Distance, seen.Count + (seen.Distance == distance ? word.Count : 0))
                : (distance, word.Count);
        }
        if (found.Count == 0)
            return [];

        var best = found.Values.Min(f => f.Distance);
        return found
            .Where(f => f.Value.Distance == best)
            .OrderByDescending(f => f.Value.Count)
            .ThenBy(f => f.Key, StringComparer.Ordinal)
            .Take(MaxSimilar)
            .Select(f => f.Key)
            .ToList();
    }

    /// <summary>
    /// term(자모)과 word의 앞부분 사이의 편집 거리(바꾸기·넣기·빼기·이웃 자모 맞바꾸기를 하나로 센다).
    /// 앞부분은 글자 경계에서만 자르고, 거리가 같으면 긴 쪽을 고른다. 거리가 k를 넘으면 (int.MaxValue, 0).
    /// </summary>
    private static (int Distance, int Chars) PrefixDistance(string term, Word word, int k, int[,] d)
    {
        var m = term.Length;
        var w = word.Jamo;
        var n = Math.Min(w.Length, m + k);
        for (var j = 0; j <= n; j++)
            d[0, j] = j;
        for (var i = 1; i <= m; i++)
        {
            d[i, 0] = i;
            var rowMin = i;
            for (var j = 1; j <= n; j++)
            {
                var v = Math.Min(Math.Min(d[i - 1, j], d[i, j - 1]) + 1, d[i - 1, j - 1] + (term[i - 1] == w[j - 1] ? 0 : 1));
                if (i > 1 && j > 1 && term[i - 1] == w[j - 2] && term[i - 2] == w[j - 1])
                    v = Math.Min(v, d[i - 2, j - 2] + 1);
                d[i, j] = v;
                rowMin = Math.Min(rowMin, v);
            }
            // 한 줄의 최솟값은 아래 줄로 갈수록 줄지 않으므로, k를 넘으면 더 볼 것 없다.
            if (rowMin > k)
                return (int.MaxValue, 0);
        }

        var best = (Distance: int.MaxValue, Chars: 0);
        for (var c = 0; c < word.Ends.Length && word.Ends[c] <= n; c++)
        {
            if (word.Ends[c] >= m - k && d[m, word.Ends[c]] <= best.Distance)
                best = (d[m, word.Ends[c]], c + 1);
        }
        return best.Distance <= k ? best : (int.MaxValue, 0);
    }

    private const string Initials = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
    private const string Medials = "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ";
    private const string Finals = "ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ";

    /// <summary>
    /// 한글 음절을 초성·중성·종성 자모로 푼다. 받침은 초성과 같은 글자로 써서 "헙"과 "허브"가 자모 하나("ㅡ")만 다르게 한다.
    /// 한글이 아닌 글자는 소문자로 그대로 둔다. ends가 있으면 ends[i]에 i번째 글자까지의 자모 수를 적는다.
    /// </summary>
    private static string Jamo(string word, int[]? ends = null)
    {
        var builder = new StringBuilder(word.Length * 3);
        for (var i = 0; i < word.Length; i++)
        {
            var c = word[i];
            if (IsHangul(c))
            {
                var n = c - 0xAC00;
                builder.Append(Initials[n / 588]).Append(Medials[n % 588 / 28]);
                if (n % 28 > 0)
                    builder.Append(Finals[n % 28 - 1]);
            }
            else
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            if (ends is not null)
                ends[i] = builder.Length;
        }
        return builder.ToString();
    }

    // ── 화면에 보이기 ──────────────────────────────────────────────

    /// <summary>
    /// 콘텐츠에서 검색어가 처음 나온 곳 앞뒤를 잘라 낸 한 줄. 제목에만 있으면 콘텐츠 앞부분(굵은 한 줄 요약부터)을 쓴다.
    /// </summary>
    private static string Snippet(string text, IReadOnlyList<SearchTerm> terms)
    {
        const int before = 40;
        const int length = 160;

        var at = terms
            .SelectMany(t => t.Forms)
            .Select(f => Find(text, f).Select(o => o.Start).DefaultIfEmpty(-1).First())
            .Where(i => i >= 0)
            .DefaultIfEmpty(0)
            .Min();

        // 낱말 중간에서 자르지 않도록 앞은 공백 다음부터, 뒤는 공백 앞까지로 한다.
        var start = Math.Max(0, at - before);
        if (start > 0 && text.IndexOfAny(Spaces, start, at - start) is var space and >= 0)
            start = space + 1;
        if (start > 0 && char.IsLowSurrogate(text[start]))
            start++;
        var end = Math.Min(text.Length, start + length);
        if (end < text.Length && text.LastIndexOfAny(Spaces, end - 1, end - at) is var cut and > 0 && cut > at)
            end = cut;
        if (end < text.Length && char.IsHighSurrogate(text[end - 1]))
            end--;

        var snippet = text[start..end].Replace('\n', ' ').Trim();
        return (start > 0 ? "…" : "") + snippet + (end < text.Length ? "…" : "");
    }

    private static readonly char[] Spaces = [' ', '\n'];

    /// <summary>글에서 form이 든 곳(시작, 끝). 띄어쓰기를 무시해도 되는 꼴(Loose)이면 글자 사이의 공백을 건너뛰며 찾는다.</summary>
    private static IEnumerable<(int Start, int End)> Find(string text, string form)
    {
        if (form.Length == 0)
            yield break;

        if (!Loose(form))
        {
            for (var i = text.IndexOf(form, StringComparison.OrdinalIgnoreCase); i >= 0;
                 i = text.IndexOf(form, i + form.Length, StringComparison.OrdinalIgnoreCase))
                yield return (i, i + form.Length);
            yield break;
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (!Same(text[i], form[0]))
                continue;
            var j = i + 1;
            var matched = 1;
            while (matched < form.Length)
            {
                while (j < text.Length && char.IsWhiteSpace(text[j]))
                    j++;
                if (j >= text.Length || !Same(text[j], form[matched]))
                    break;
                j++;
                matched++;
            }
            if (matched == form.Length)
            {
                yield return (i, j);
                i = j - 1;
            }
        }
    }

    /// <summary>StringComparison.OrdinalIgnoreCase와 같은 규칙으로 글자 하나를 비교한다.</summary>
    private static bool Same(char a, char b) => a == b || char.ToUpperInvariant(a) == char.ToUpperInvariant(b);

    /// <summary>글에서 검색어(띄어쓰기만 다른 것, 오타를 고친 낱말 포함)가 든 곳을 &lt;mark&gt;로 감싼다. 나머지 글자는 그대로 HTML 인코딩된다.</summary>
    public static IHtmlContent Mark(string text, IReadOnlyList<SearchTerm> terms)
    {
        var html = new HtmlContentBuilder();
        if (text.Length == 0)
            return html;

        var marked = new bool[text.Length];
        foreach (var form in terms.SelectMany(t => t.Forms))
        {
            foreach (var (start, end) in Find(text, form))
                Array.Fill(marked, true, start, end - start);
        }

        var from = 0;
        for (var i = 1; i <= text.Length; i++)
        {
            if (i < text.Length && marked[i] == marked[from])
                continue;
            var part = text[from..i];
            if (marked[from])
                html.AppendHtml("<mark>").Append(part).AppendHtml("</mark>");
            else
                html.Append(part);
            from = i;
        }
        return html;
    }
}

/// <param name="Text">입력한 낱말.</param>
/// <param name="Similar">Text가 든 기사가 없어 대신 찾은 비슷한 낱말. 그대로 찾았으면 null, 비슷한 낱말도 없으면 빈 목록.</param>
public sealed record SearchTerm(string Text, IReadOnlyList<string>? Similar = null)
{
    /// <summary>기사에서 실제로 찾는 꼴.</summary>
    public IReadOnlyList<string> Forms { get; } = Similar ?? [Text];
}

/// <param name="Title">찾은 한국어 제목(NFC). 번역 전이면 원제.</param>
/// <param name="Original">원제(NFC).</param>
/// <param name="Snippet">콘텐츠에서 검색어 앞뒤를 잘라 낸 한 줄.</param>
public sealed record SearchHit(StoryView Story, string Title, string Original, string Snippet);

/// <param name="Terms">검색어를 띄어쓰기로 나눈 낱말(대소문자만 다른 것은 하나로)과, 오타를 고쳐 대신 찾은 낱말.</param>
/// <param name="Total">찾은 기사 수.</param>
/// <param name="Page">보여 주는 쪽(1부터).</param>
public sealed record SearchResults(IReadOnlyList<SearchTerm> Terms, int Total, int Page, int PageCount, IReadOnlyList<SearchHit> Hits)
{
    public static readonly SearchResults Empty = new([], 0, 1, 0, []);
}
