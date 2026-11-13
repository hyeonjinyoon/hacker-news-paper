using HnPaper.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HnPaper.Web.Pages;

/// <summary>
/// 기사 검색(/search?q=검색어&amp;sort=recent&amp;p=2). 쪽 번호를 page가 아니라 p로 받는 것은 Razor Pages가 page를 페이지 경로 값으로 쓰기 때문이다.
/// </summary>
public sealed class SearchModel(SearchIndex index) : PageModel
{
    public const int PageSize = 20;

    public string Query { get; private set; } = "";
    /// <summary>최신순. 아니면 정확도순.</summary>
    public bool Recent { get; private set; }
    public SearchResults Results { get; private set; } = SearchResults.Empty;

    public void OnGet(string? q, string? sort, int p = 1)
    {
        Query = SearchIndex.Clean(q);
        Recent = sort == "recent";
        if (Query.Length > 0)
            Results = index.Search(Query, Recent, p, PageSize);
    }

    /// <summary>같은 검색어로 다른 쪽이나 다른 정렬을 보는 주소.</summary>
    public string Link(int page, bool? recent = null)
    {
        var url = "/search?q=" + Uri.EscapeDataString(Query);
        if (recent ?? Recent)
            url += "&sort=recent";
        if (page > 1)
            url += "&p=" + page;
        return url;
    }
}
