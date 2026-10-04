using HnPaper.Web.Models;
using HnPaper.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HnPaper.Web.Pages;

/// <summary>기사를 누르면 원문으로 가기 전에 보이는 중간 페이지: 한국어 요약(또는 HN 본문 번역)과 번역 댓글.</summary>
public sealed class StoryModel(EditionStore store) : PageModel
{
    public EditionView Edition { get; private set; } = default!;
    public StoryView Story { get; private set; } = default!;
    public ItemView Item { get; private set; } = default!;
    public StoryView? Prev { get; private set; }
    public StoryView? Next { get; private set; }

    public IActionResult OnGet(string date, long id)
    {
        var edition = store.Load(date);
        if (edition is null)
            return NotFound();

        var index = -1;
        for (var i = 0; i < edition.All.Count; i++)
        {
            if (edition.All[i].Raw.Id == id)
            {
                index = i;
                break;
            }
        }
        if (index < 0)
            return NotFound();

        Edition = edition;
        Story = edition.All[index];
        Prev = index > 0 ? edition.All[index - 1] : null;
        Next = index + 1 < edition.All.Count ? edition.All[index + 1] : null;
        Item = store.LoadItem(edition, Story);
        return Page();
    }
}
