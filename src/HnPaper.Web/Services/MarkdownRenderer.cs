using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.AspNetCore.Html;

namespace HnPaper.Web.Services;

/// <summary>
/// 중간 페이지의 본문·댓글 마크다운을 HTML로 바꾼다. 번역본과 댓글은 외부에서 온 글이므로
/// 원시 HTML은 막고, http(s)·mailto가 아닌 링크는 지우고, 외부 이미지는 링크로만 남긴다.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseAutoLinks()
        .Build();

    public static IHtmlContent Render(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return HtmlString.Empty;

        var document = Markdown.Parse(markdown, Pipeline);
        foreach (var link in document.Descendants<LinkInline>())
        {
            link.IsImage = false;
            if (!IsSafe(link.Url))
                link.Url = "#";
            AddExternalAttributes(link);
        }
        foreach (var link in document.Descendants<AutolinkInline>())
        {
            if (!link.IsEmail && !IsSafe(link.Url))
                link.Url = "#";
            AddExternalAttributes(link);
        }

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();
        return new HtmlString(writer.ToString());
    }

    /// <summary>
    /// 본문 마크다운에서 첫 소제목 앞의 문단들을 평문으로 꺼낸다. 1면의 요약과 리드는 따로 쓰지 않고 이것을 쓴다.
    /// </summary>
    public static IReadOnlyList<string> IntroParagraphs(string? markdown, int max)
    {
        if (string.IsNullOrWhiteSpace(markdown) || max <= 0)
            return [];

        var paragraphs = new List<string>();
        foreach (var block in Markdown.Parse(markdown, Pipeline))
        {
            if (block is HeadingBlock || paragraphs.Count >= max)
                break;
            if (block is ParagraphBlock { Inline: { } inline })
            {
                var text = PlainText(inline).Trim();
                if (text.Length > 0)
                    paragraphs.Add(text);
            }
        }
        return paragraphs;
    }

    private static string PlainText(ContainerInline container)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content);
                    break;
                case CodeInline code:
                    builder.Append(code.Content);
                    break;
                case AutolinkInline autolink:
                    builder.Append(autolink.Url);
                    break;
                case LineBreakInline:
                    builder.Append(' ');
                    break;
                case ContainerInline nested:
                    builder.Append(PlainText(nested));
                    break;
            }
        }
        return builder.ToString();
    }

    private static void AddExternalAttributes(Inline link)
    {
        var attributes = link.GetAttributes();
        attributes.AddPropertyIfNotExist("target", "_blank");
        attributes.AddPropertyIfNotExist("rel", "noopener nofollow");
    }

    private static bool IsSafe(string? url) =>
        url is not null
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" or "mailto";
}
