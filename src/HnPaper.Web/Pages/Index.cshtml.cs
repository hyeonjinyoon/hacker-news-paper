using HnPaper.Web.Models;
using HnPaper.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HnPaper.Web.Pages;

public sealed class IndexModel(EditionStore store) : PageModel
{
    public EditionView? Edition { get; private set; }
    public string? PrevDate { get; private set; }
    public string? NextDate { get; private set; }
    /// <summary>1면(/)에 걸린 호인지. 가장 최근 호의 /{date}는 1면과 내용이 같아 검색엔진에는 1면 주소를 대표로 알린다.</summary>
    public bool IsLatest { get; private set; }

    public IActionResult OnGet(string? date)
    {
        var latest = store.LatestDate();
        IsLatest = date is null || date == latest;
        date ??= latest;
        if (date is null)
            return Page();

        Edition = store.Load(date);
        if (Edition is null)
            return NotFound();

        var editions = store.List();
        for (var i = 0; i < editions.Count; i++)
        {
            if (editions[i].Date != date)
                continue;
            NextDate = i > 0 ? editions[i - 1].Date : null;
            PrevDate = i + 1 < editions.Count ? editions[i + 1].Date : null;
            break;
        }
        return Page();
    }
}
