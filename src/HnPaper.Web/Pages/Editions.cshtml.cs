using HnPaper.Web.Models;
using HnPaper.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HnPaper.Web.Pages;

public sealed class EditionsModel(EditionStore store) : PageModel
{
    public IReadOnlyList<(EditionInfo Info, string? Headline)> Items { get; private set; } = [];

    public void OnGet() =>
        Items = store.List().Select(e => (e, store.Load(e.Date)?.Hero?.Title)).ToList();
}
