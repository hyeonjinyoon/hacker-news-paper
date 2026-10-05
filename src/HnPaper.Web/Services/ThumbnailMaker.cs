using HnPaper.Web.Models;
using SkiaSharp;

namespace HnPaper.Web.Services;

/// <summary>
/// 원문 사이트의 og 이미지를 내려받아 두 크기의 WebP로 줄여 data/img/{date}/에 저장한다.
/// 큰 것(1600px)은 1면 톱·기사 페이지처럼 넓은 칸을 레티나 화면에서도 선명하게, 작은 것(800px)은 썸네일에 쓴다.
/// 사이트는 이 파일이 있으면 원문 서버 대신 직접 제공한다(크기·대기 시간이 크게 준다).
/// 줄이지 못한 이미지(SVG, AVIF 등 디코딩할 수 없는 형식)는 건너뛰고, 사이트는 원본 주소를 그대로 쓴다.
/// </summary>
public sealed class ThumbnailMaker(HttpClient http, PaperOptions options)
{
    public const int LargeWidth = 1600;
    public const int SmallWidth = 800;
    /// <summary>파일 이름에 붙는 형식 버전. 크기·품질 규칙을 바꾸면 올려서 브라우저·CDN에 캐시된 옛 이미지를 피한다.</summary>
    public const string Version = "v2";
    // 사진은 84로 충분하지만, 글자·선이 많은 PNG·GIF(슬라이드, 로고, 화면 캡처)는 압축 티가 잘 나서 92로 둔다.
    private const int PhotoQuality = 84;
    private const int GraphicQuality = 92;
    private const int MaxDownloadBytes = 20 * 1024 * 1024;

    public async Task<(int Made, int Skipped)> MakeAllAsync(RawEdition edition, CancellationToken ct)
    {
        int made = 0, skipped = 0;
        await Parallel.ForEachAsync(edition.Stories.Where(s => s.Image is not null),
            new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = ct },
            async (story, token) =>
            {
                if (await MakeAsync(edition.Date, story, token))
                    Interlocked.Increment(ref made);
                else
                    Interlocked.Increment(ref skipped);
            });
        return (made, skipped);
    }

    private async Task<bool> MakeAsync(string date, RawStory story, CancellationToken ct)
    {
        var bytes = await DownloadAsync(story.Image!, ct);
        if (bytes is null)
            return false;

        try
        {
            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data);
            if (codec is null)
                return false;
            var quality = codec.EncodedFormat is SKEncodedImageFormat.Png or SKEncodedImageFormat.Gif or SKEncodedImageFormat.Bmp
                ? GraphicQuality
                : PhotoQuality;
            using var original = SKBitmap.Decode(codec);
            if (original is null || original.Width <= 0 || original.Height <= 0)
                return false;

            Directory.CreateDirectory(Path.GetDirectoryName(options.ThumbPath(date, story.Id, LargeWidth))!);
            Save(original, LargeWidth, quality, options.ThumbPath(date, story.Id, LargeWidth));
            Save(original, SmallWidth, quality, options.ThumbPath(date, story.Id, SmallWidth));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>저장해 둔 파일의 실제 폭(파일 머리만 읽는다). 원본보다 키우지 않으므로 이름의 폭(1600·800)보다 작을 수 있다. 읽지 못하면 null.</summary>
    public static int? ReadWidth(string path)
    {
        try
        {
            using var codec = SKCodec.Create(path);
            return codec is { Info.Width: > 0 } ? codec.Info.Width : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void Save(SKBitmap original, int boxWidth, int quality, string path)
    {
        // 폭만 맞추면 가로로 긴 이미지(배너 등)를 16:9 칸에 꽉 채울 때 높이가 모자라 늘어나 흐려진다.
        // 그래서 boxWidth×(16:9) 칸을 덮는 데 필요한 크기로 줄인다. 원본보다 키우지는 않는다.
        var boxHeight = boxWidth * 9 / 16;
        var scale = Math.Min(1.0, Math.Max(boxWidth / (double)original.Width, boxHeight / (double)original.Height));
        var width = Math.Max(1, (int)Math.Round(original.Width * scale));
        var height = Math.Max(1, (int)Math.Round(original.Height * scale));

        using var resized = scale >= 1.0
            ? original.Copy()
            : original.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKCubicResampler.CatmullRom));
        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Webp, quality);

        // 다 쓴 뒤에 이름을 바꿔, 사이트가 반쯤 쓴 파일을 읽지 않게 한다.
        var temp = path + ".tmp";
        using (var file = File.Create(temp))
            data.SaveTo(file);
        File.Move(temp, path, overwrite: true);
    }

    private async Task<byte[]?> DownloadAsync(string url, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxDownloadBytes)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxDownloadBytes)
                    return null;
            }
            return buffer.ToArray();
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
