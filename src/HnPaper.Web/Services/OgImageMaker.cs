using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HnPaper.Web.Models;
using SkiaSharp;

namespace HnPaper.Web.Services;

/// <summary>
/// 기사 페이지를 공유할 때 보이는 이미지(og:image, 1200×630 JPEG).
/// 사이트 공유 이미지(wwwroot/og.png)와 같은 지면 모양으로, 마스트헤드와 호 날짜·순위 아래
/// 왼쪽에 한국어 제목(자리가 남으면 그 아래 한 줄 요약)을, 오른쪽에 대표 이미지와 출처·포인트·댓글 수를 그린다.
/// 처음 요청할 때 그려 data/img/{date}/{id}-og-{key}.jpg에 저장하고, 다음부터는 그 파일을 준다.
/// key는 그림에 들어가는 내용으로 만들므로, 제목이 번역되거나 대표 이미지가 바뀌면 key와 주소가 함께 바뀐다.
/// </summary>
public sealed class OgImageMaker(PaperOptions options, GradientLibrary gradients)
{
    /// <summary>그리는 규칙을 바꾸면 올린다. key에 들어가므로 저장해 둔 옛 이미지 대신 새로 그린다.</summary>
    private const string Version = "v2";
    public const int Width = 1200;
    public const int Height = 630;
    private const int Quality = 90;

    // 색은 사이트(paper.css)와 같다.
    private static readonly SKColor Fg = SKColor.Parse("#111111");
    private static readonly SKColor Sub = SKColor.Parse("#5a5a5a");
    private static readonly SKColor Muted = SKColor.Parse("#767676");
    private static readonly SKColor Line = SKColor.Parse("#e5e5e5");
    private static readonly SKColor Accent = SKColor.Parse("#008689");
    private static readonly SKColor Placeholder = SKColor.Parse("#f2f9f9");

    // 지면 글꼴 Pretendard(SIL OFL, Fonts/LICENSE.txt)를 함께 배포한다. 파일이 없으면 시스템 글꼴로 그린다.
    private static readonly SKTypeface ExtraBold = LoadFont("Pretendard-ExtraBold.otf", SKFontStyleWeight.ExtraBold);
    private static readonly SKTypeface Bold = LoadFont("Pretendard-Bold.otf", SKFontStyleWeight.Bold);
    private static readonly SKTypeface SemiBold = LoadFont("Pretendard-SemiBold.otf", SKFontStyleWeight.SemiBold);
    // Pretendard에 없는 글자(한자, 가나, 이모지 등)를 대신 그릴 시스템 글꼴
    private static readonly ConcurrentDictionary<(SKTypeface, int), SKTypeface> Fallbacks = new();

    // 제목은 들어가는 가장 큰 크기로 쓴다.
    private static readonly int[] TitleSizes = [64, 60, 56, 52, 48, 44, 40];
    private const float TitleLineHeight = 1.32f;
    private static readonly System.Buffers.SearchValues<char> WordBreaks = System.Buffers.SearchValues.Create("·/-–—");

    /// <summary>지금 내용으로 그린 이미지를 가리키는 key. 기사 페이지가 og:image 주소의 ?v=로 쓴다.</summary>
    public string Key(StoryView story)
    {
        var image = ImagePath(story);
        var stamp = image is null ? 0 : File.GetLastWriteTimeUtc(image).Ticks;
        var text = string.Join('\n', Version, story.Date, story.Raw.Rank, story.Title, story.Summary, story.Raw.Site,
            story.Raw.Points, story.Raw.Comments, image is null ? "" : Path.GetFileName(image), stamp);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12];
    }

    /// <summary>key에 맞는 이미지 파일. 없으면 그려서 저장하고, 같은 기사의 옛 이미지는 지운다.</summary>
    public string GetOrCreate(StoryView story, string key)
    {
        var path = options.OgPath(story.Date, story.Raw.Id, key);
        if (File.Exists(path))
            return path;

        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var bytes = Render(story, ImagePath(story));

        // 다 쓴 뒤에 이름을 바꿔, 같은 이미지를 동시에 요청해도 반쯤 쓴 파일을 주지 않게 한다.
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);

        foreach (var old in Directory.EnumerateFiles(dir, $"{story.Raw.Id}-og-*.jpg"))
        {
            if (old != path)
                File.Delete(old);
        }
        return path;
    }

    // 압축해 둔 대표 이미지가 있으면 그것을, 없으면(원문에 이미지가 없거나 압축하지 못한 형식) 1면과 같은 그라데이션 패널을 쓴다.
    private string? ImagePath(StoryView story)
    {
        var thumb = options.ThumbPath(story.Date, story.Raw.Id);
        return File.Exists(thumb) ? thumb : gradients.PathFor(story.Raw.Id);
    }

    private static byte[] Render(StoryView story, string? imagePath)
    {
        const float left = 64, right = Width - 64, top = 168, bottom = 566, masthead = 100;

        using var surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        // 마스트헤드: 왼쪽에 로고, 오른쪽에 호 날짜와 순위. 아래에 굵은 괘선.
        Draw(canvas, "Hacker Newspaper", left, masthead, new TextStyle(ExtraBold, 46, Fg, -0.045f));
        var rankStyle = new TextStyle(Bold, 24, Accent);
        var rank = $"{story.Raw.Rank}위";
        var rankWidth = Measure(rank, rankStyle);
        Draw(canvas, rank, right - rankWidth, masthead, rankStyle);
        var dateStyle = new TextStyle(SemiBold, 22, Muted);
        var date = DateOnly.ParseExact(story.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("M월 d일 호", CultureInfo.InvariantCulture);
        Draw(canvas, date, right - rankWidth - 12 - Measure(date, dateStyle), masthead, dateStyle);
        Fill(canvas, SKRect.Create(left, 124, right - left, 5), Fg);

        // 오른쪽: 16:9 대표 이미지와 그 아래 출처, 포인트·댓글 수
        var image = SKRect.Create(right - 432, top, 432, 243);
        DrawCover(canvas, imagePath, image);
        var siteStyle = new TextStyle(Bold, 25, Fg, -0.01f);
        Draw(canvas, Ellipsize(story.Raw.Site, siteStyle, image.Width), image.Left, image.Bottom + 48, siteStyle);
        var meta = new List<string>();
        if (story.Raw.Points is { } points)
            meta.Add($"▲ {points}");
        if (story.Raw.Comments is > 0 and var comments)
            meta.Add($"댓글 {comments}");
        if (meta.Count > 0)
            Draw(canvas, string.Join("   ", meta), image.Left, image.Bottom + 86, new TextStyle(SemiBold, 23, Muted));

        // 칸 사이 세로줄
        Fill(canvas, SKRect.Create(image.Left - 24, top, 1, bottom - top), Line);

        // 왼쪽: 제목. 글자 윗선을 이미지 윗선에 맞춘다.
        var column = image.Left - 48 - left;
        var (lines, style) = FitTitle(story.Title, column, bottom - top);
        var baseline = top + style.Size * 0.8f;
        foreach (var line in lines)
        {
            Draw(canvas, line, left, baseline, style);
            baseline += style.Size * TitleLineHeight;
        }

        // 제목이 짧아 자리가 남으면 1면의 리드처럼 한 줄 요약을 두 줄 이상 넣을 수 있을 때만 그 아래에 쓴다.
        if (story.Summary is { } summary)
        {
            var summaryStyle = new TextStyle(SemiBold, 27, Sub, -0.02f);
            var lineHeight = summaryStyle.Size * 1.6f;
            var summaryTop = top + (lines.Count - 1) * style.Size * TitleLineHeight + style.Size + 36;
            var room = (int)((bottom - summaryTop - summaryStyle.Size) / lineHeight) + 1;
            if (room >= 2)
            {
                var summaryLines = Wrap(summary, summaryStyle, column);
                if (summaryLines.Count > room)
                {
                    summaryLines = summaryLines.Take(room).ToList();
                    summaryLines[^1] = Ellipsize(summaryLines[^1] + "…", summaryStyle, column);
                }
                baseline = summaryTop + summaryStyle.Size * 0.8f;
                foreach (var line in summaryLines)
                {
                    Draw(canvas, line, left, baseline, summaryStyle);
                    baseline += lineHeight;
                }
            }
        }

        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Jpeg, Quality);
        return data.ToArray();
    }

    /// <summary>제목이 칸에 들어가는 가장 큰 크기와 줄. 가장 작은 크기로도 넘치면 들어가는 줄까지 쓰고 말줄임표를 붙인다.</summary>
    private static (List<string> Lines, TextStyle Style) FitTitle(string title, float width, float height)
    {
        foreach (var size in TitleSizes)
        {
            var style = new TextStyle(Bold, size, Fg, -0.03f);
            var lines = Wrap(title, style, width);
            if ((lines.Count - 1) * size * TitleLineHeight + size <= height)
                return (lines, style);
        }

        var smallest = new TextStyle(Bold, TitleSizes[^1], Fg, -0.03f);
        var maxLines = (int)((height - smallest.Size) / (smallest.Size * TitleLineHeight)) + 1;
        var kept = Wrap(title, smallest, width).Take(maxLines).ToList();
        kept[^1] = Ellipsize(kept[^1] + "…", smallest, width);
        return (kept, smallest);
    }

    /// <summary>
    /// 지면처럼 띄어쓰기에서만 줄을 바꾼다(word-break: keep-all). 한 줄보다 긴 낱말("키보드·트랙패드·마우스를")은
    /// 가운뎃점·빗금·붙임표 뒤에서 자르고, 그런 자리가 없을 때만 글자 단위로 자른다.
    /// </summary>
    private static List<string> Wrap(string text, TextStyle style, float width)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : $"{line} {word}";
            if (Measure(candidate, style) <= width)
            {
                line = candidate;
                continue;
            }
            if (line.Length > 0)
                lines.Add(line);
            line = word;
            while (Measure(line, style) > width)
            {
                var cut = FittingLength(line, style, width);
                var mark = line.AsSpan(0, cut - 1).LastIndexOfAny(WordBreaks);
                if (mark >= 0)
                    cut = mark + 1;
                lines.Add(line[..cut]);
                line = line[cut..];
            }
        }
        if (line.Length > 0)
            lines.Add(line);
        return lines;
    }

    /// <summary>폭을 넘으면 뒤를 잘라 말줄임표를 붙인다.</summary>
    private static string Ellipsize(string text, TextStyle style, float width)
    {
        if (Measure(text, style) <= width)
            return text;
        var body = text.TrimEnd('…');
        var cut = FittingLength(body, style, width - Measure("…", style));
        return body[..cut].TrimEnd() + "…";
    }

    /// <summary>폭 안에 들어가는 앞부분의 길이(글자 경계). 적어도 한 글자는 넣는다.</summary>
    private static int FittingLength(string text, TextStyle style, float width)
    {
        var length = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var next = length + rune.Utf16SequenceLength;
            if (length > 0 && Measure(text[..next], style) > width)
                break;
            length = next;
        }
        return length;
    }

    private sealed record TextStyle(SKTypeface Typeface, float Size, SKColor Color, float Tracking = 0);

    private static float Measure(string text, TextStyle style) => Draw(null, text, 0, 0, style);

    /// <summary>
    /// 한 줄을 그리고 폭을 돌려준다(canvas가 null이면 재기만 한다). 지면의 letter-spacing처럼 글자 사이를
    /// Tracking(em)만큼 좁히려고 한 글자씩 놓는다. Pretendard에 없는 글자는 시스템 글꼴로 그린다.
    /// </summary>
    private static float Draw(SKCanvas? canvas, string text, float x, float y, TextStyle style)
    {
        using var paint = new SKPaint { Color = style.Color, IsAntialias = true };
        var primary = NewFont(style.Typeface, style.Size);
        var fonts = new Dictionary<SKTypeface, SKFont> { [style.Typeface] = primary };
        try
        {
            var start = x;
            foreach (var rune in text.EnumerateRunes())
            {
                var typeface = rune.Value == ' ' || primary.ContainsGlyph(rune.Value)
                    ? style.Typeface
                    : Fallbacks.GetOrAdd((style.Typeface, rune.Value), key =>
                        SKFontManager.Default.MatchCharacter(null, key.Item1.FontStyle, ["ko"], key.Item2) ?? key.Item1);
                if (!fonts.TryGetValue(typeface, out var font))
                    fonts[typeface] = font = NewFont(typeface, style.Size);
                var glyph = rune.ToString();
                canvas?.DrawText(glyph, x, y, SKTextAlign.Left, font, paint);
                x += font.MeasureText(glyph) + style.Tracking * style.Size;
            }
            return x - start;
        }
        finally
        {
            foreach (var font in fonts.Values)
                font.Dispose();
        }
    }

    private static SKFont NewFont(SKTypeface typeface, float size) =>
        new(typeface, size) { Subpixel = true, Edging = SKFontEdging.Antialias };

    /// <summary>이미지를 칸에 꽉 차게 가운데를 잘라 그린다. 읽지 못하면 옅은 바탕만 남는다.</summary>
    private static void DrawCover(SKCanvas canvas, string? path, SKRect box)
    {
        Fill(canvas, box, Placeholder);
        if (path is null)
            return;

        SKImage? image;
        try
        {
            image = SKImage.FromEncodedData(File.ReadAllBytes(path));
        }
        catch (IOException)
        {
            return;
        }
        if (image is null)
            return;

        using (image)
        {
            var scale = Math.Max(box.Width / image.Width, box.Height / image.Height);
            var width = box.Width / scale;
            var height = box.Height / scale;
            var source = SKRect.Create((image.Width - width) / 2, (image.Height - height) / 2, width, height);
            canvas.DrawImage(image, source, box, new SKSamplingOptions(SKCubicResampler.Mitchell));
        }
    }

    private static void Fill(SKCanvas canvas, SKRect rect, SKColor color)
    {
        using var paint = new SKPaint { Color = color };
        canvas.DrawRect(rect, paint);
    }

    private static SKTypeface LoadFont(string file, SKFontStyleWeight weight)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fonts", file);
        return (File.Exists(path) ? SKFontManager.Default.CreateTypeface(path) : null)
            ?? SKFontManager.Default.MatchFamily(null, new SKFontStyle(weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright))
            ?? SKTypeface.Default;
    }
}
