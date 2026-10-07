using System.Globalization;
using System.Xml.Linq;

namespace HnPaper.Web.Services;

/// <summary>
/// 검색엔진(구글 서치 콘솔 등)에 알려 줄 sitemap.xml. 1면, 지난 호, 번역까지 끝난 호의 1면과 기사 페이지를 싣는다.
/// 번역 전인 호는 영어 제목만 있어 뺀다. lastmod는 그 페이지가 쓰는 번역본 파일을 마지막으로 고친 시각이다.
/// </summary>
public static class Sitemap
{
    private static readonly XNamespace Ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

    public static string Build(string siteUrl, EditionStore store, PaperOptions options)
    {
        var editionUrls = new List<XElement>();
        var stamps = new List<DateTime>();
        foreach (var info in store.List().Where(e => e.Translated))
        {
            if (store.Load(info.Date) is not { } edition)
                continue;

            // 기사 페이지는 본문 번역본과 댓글 번역본을, 호의 1면은 제목 번역본과 기사들의 본문 번역본(요약 첫 문단)을 쓴다.
            var editionStamp = Stamp(options.KoPath(info.Date));
            var storyUrls = new List<XElement>();
            foreach (var story in edition.All)
            {
                var storyStamp = Max(Stamp(options.KoItemPath(info.Date, story.Raw.Id)), Stamp(options.KoCommentsPath(info.Date, story.Raw.Id)));
                editionStamp = Max(editionStamp, storyStamp);
                storyUrls.Add(Url(siteUrl + story.Href, storyStamp == DateTime.MinValue ? editionStamp : storyStamp));
            }
            editionUrls.Add(Url($"{siteUrl}/{info.Date}", editionStamp));
            editionUrls.AddRange(storyUrls);
            stamps.Add(editionStamp);
        }

        // 1면(/)은 가장 최근 호를, 지난 호(/editions)는 모든 호의 머리기사를 보여 준다.
        var latest = stamps.Count > 0 ? stamps[0] : DateTime.MinValue;
        var newest = stamps.Count > 0 ? stamps.Max() : DateTime.MinValue;
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(Ns + "urlset", [Url(siteUrl + "/", latest), Url(siteUrl + "/editions", newest), .. editionUrls]));
        return doc.Declaration + "\n" + doc;
    }

    private static XElement Url(string loc, DateTime lastModified) =>
        new(Ns + "url",
            new XElement(Ns + "loc", loc),
            lastModified == DateTime.MinValue
                ? null
                : new XElement(Ns + "lastmod", lastModified.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));

    private static DateTime Stamp(string path) =>
        File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
