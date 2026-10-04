using System.Text.Json;
using HnPaper.Web.Models;

namespace HnPaper.Web.Services;

/// <summary>
/// 이미 번역한 기사의 번역을 다시 쓴다. HN 1면 글은 며칠씩 남고, 같은 날 다시 수집해도 대부분 그대로다.
/// 제목·본문은 그 기사(같은 id)의 가장 최근 번역(이 호에 있으면 이 호, 없으면 최근 호부터)을 가져오고,
/// 댓글은 지금 수집본에 있는 댓글의 번역만 모은다. 남은 것(제목·본문이 없는 기사, 번역이 없는 댓글)만 새로 번역한다.
/// </summary>
public static class TranslationReuse
{
    /// <summary>거슬러 올라가 볼 지난 호 수. 1면 글이 이보다 오래 남는 일은 드물다.</summary>
    private const int Lookback = 7;

    /// <param name="Kept">이 호 번역본에 이미 있던 것.</param>
    /// <param name="Carried">지난 호에서 가져온 것.</param>
    /// <param name="Missing">새로 번역해야 하는 것.</param>
    public sealed record Counts(int Kept, int Carried, int Missing);

    public sealed record Result(Counts Titles, Counts Bodies, Counts Comments, IReadOnlyList<RawStory> NeedTitle);

    public static Result Apply(PaperOptions options, string date)
    {
        var raw = PaperJson.Read<RawEdition>(options.RawPath(date))!;
        var stories = raw.Stories.Where(s => !s.Excluded).ToList();
        var earlier = EarlierDates(options, date);

        // 제목: 이 호 → 최근 호 순서로 찾는다. 수집본에 없는 기사의 제목은 버린다.
        var current = TryRead<KoEdition>(options.KoPath(date));
        var titleSources = earlier.Select(d => TitlesById(TryRead<KoEdition>(options.KoPath(d)))).Prepend(TitlesById(current)).ToList();
        var titled = new List<KoStory>();
        var needTitle = new List<RawStory>();
        int titlesKept = 0, titlesCarried = 0;
        foreach (var story in stories)
        {
            var i = titleSources.FindIndex(s => s.ContainsKey(story.Id));
            if (i < 0)
            {
                needTitle.Add(story);
                continue;
            }
            titled.Add(titleSources[i][story.Id]);
            if (i == 0)
                titlesKept++;
            else
                titlesCarried++;
        }
        PaperJson.Write(options.KoPath(date), new KoEdition(date, current?.Lead, titled));

        // 본문: 이 호에 없으면 가장 최근 호의 번역본을 그대로 복사한다.
        int bodiesKept = 0, bodiesCarried = 0, bodiesMissing = 0;
        foreach (var story in stories)
        {
            var target = options.KoItemPath(date, story.Id);
            if (File.Exists(target))
            {
                bodiesKept++;
                continue;
            }
            var source = earlier.Select(d => options.KoItemPath(d, story.Id)).FirstOrDefault(File.Exists);
            if (source is null)
            {
                bodiesMissing++;
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
            bodiesCarried++;
        }

        // 댓글: 지금 수집본에 있는 댓글마다 이 호 → 최근 호 순서로 번역을 찾고, 수집본 순서로 다시 쓴다.
        int commentsKept = 0, commentsCarried = 0, commentsMissing = 0;
        foreach (var story in stories)
        {
            if (TryRead<RawItem>(options.RawItemPath(date, story.Id)) is not { } item)
                continue;
            var wanted = item.Comments.Where(c => !c.Deleted).Select(c => c.Id).ToList();
            var target = options.KoCommentsPath(date, story.Id);
            var mine = Translations(TryRead<KoComments>(target));
            var found = wanted.Where(mine.ContainsKey).ToDictionary(id => id, id => mine[id]);
            var kept = found.Count;
            foreach (var d in earlier)
            {
                if (found.Count == wanted.Count)
                    break;
                var theirs = Translations(TryRead<KoComments>(options.KoCommentsPath(d, story.Id)));
                foreach (var id in wanted)
                {
                    if (!found.ContainsKey(id) && theirs.TryGetValue(id, out var text))
                        found[id] = text;
                }
            }
            commentsKept += kept;
            commentsCarried += found.Count - kept;
            commentsMissing += wanted.Count - found.Count;
            if (found.Count > 0 || File.Exists(target))
                WriteComments(target, story.Id, wanted, found);
        }

        return new Result(
            new Counts(titlesKept, titlesCarried, needTitle.Count),
            new Counts(bodiesKept, bodiesCarried, bodiesMissing),
            new Counts(commentsKept, commentsCarried, commentsMissing),
            needTitle);
    }

    /// <summary>
    /// hn-paper-comments가 새로 번역한 댓글({id}.comments.new.json)을 댓글 번역본에 합친다.
    /// 지금 수집본에 있는 댓글만 수집본 순서로 남기고(같은 댓글이면 새 번역을 쓴다), 합친 뒤 새 번역 파일은 지운다.
    /// </summary>
    public static Counts MergeComments(PaperOptions options, string date, long id)
    {
        var item = PaperJson.Read<RawItem>(options.RawItemPath(date, id))!;
        var wanted = item.Comments.Where(c => !c.Deleted).Select(c => c.Id).ToList();
        var target = options.KoCommentsPath(date, id);
        var newPath = options.KoNewCommentsPath(date, id);
        var existing = Translations(TryRead<KoComments>(target));
        var added = Translations(PaperJson.Read<KoComments>(newPath));

        var merged = new Dictionary<long, string>();
        int kept = 0, fresh = 0;
        foreach (var commentId in wanted)
        {
            if (added.TryGetValue(commentId, out var text))
            {
                merged[commentId] = text;
                fresh++;
            }
            else if (existing.TryGetValue(commentId, out text))
            {
                merged[commentId] = text;
                kept++;
            }
        }
        WriteComments(target, id, wanted, merged);
        File.Delete(newPath);
        return new Counts(kept, fresh, wanted.Count - merged.Count);
    }

    private static void WriteComments(string path, long id, List<long> order, Dictionary<long, string> texts) =>
        PaperJson.Write(path, new KoComments(id, [.. order.Where(texts.ContainsKey).Select(c => new KoComment(c, texts[c]))]));

    private static Dictionary<long, KoStory> TitlesById(KoEdition? edition)
    {
        var titles = new Dictionary<long, KoStory>();
        foreach (var story in edition?.Stories ?? [])
        {
            if (!string.IsNullOrWhiteSpace(story.Title))
                titles.TryAdd(story.Id, story);
        }
        return titles;
    }

    private static Dictionary<long, string> Translations(KoComments? comments)
    {
        var texts = new Dictionary<long, string>();
        foreach (var comment in comments?.Comments ?? [])
        {
            if (!string.IsNullOrWhiteSpace(comment.Text))
                texts.TryAdd(comment.Id, comment.Text);
        }
        return texts;
    }

    /// <summary>이 호보다 앞선 호를 최근 것부터 Lookback개. 1면 번역본(data/ko/{date}.json)이나 기사별 폴더가 있는 날짜를 본다.</summary>
    private static List<string> EarlierDates(PaperOptions options, string date)
    {
        if (!Directory.Exists(options.KoDirectory))
            return [];
        var files = Directory.EnumerateFiles(options.KoDirectory, "*.json").Select(Path.GetFileNameWithoutExtension);
        var dirs = Directory.EnumerateDirectories(options.KoDirectory).Select(Path.GetFileName);
        return files.Concat(dirs)
            .OfType<string>()
            .Where(d => Kst.DatePattern.IsMatch(d) && string.CompareOrdinal(d, date) < 0)
            .Distinct()
            .OrderDescending(StringComparer.Ordinal)
            .Take(Lookback)
            .ToList();
    }

    private static T? TryRead<T>(string path) where T : class
    {
        if (!File.Exists(path))
            return null;
        try
        {
            return PaperJson.Read<T>(path);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
