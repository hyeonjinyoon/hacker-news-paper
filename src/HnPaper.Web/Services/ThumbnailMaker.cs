using HnPaper.Web.Models;
using SkiaSharp;

namespace HnPaper.Web.Services;

/// <summary>
/// 원문 사이트의 og 이미지를 내려받아 두 크기의 WebP로 줄여 data/img/{date}/에 저장한다.
/// 사이트는 이 파일이 있으면 원문 서버 대신 직접 제공한다(크기·대기 시간이 크게 준다).
/// 줄이지 못한 이미지(SVG, AVIF 등 디코딩할 수 없는 형식)는 건너뛰고, 사이트는 원본 주소를 그대로 쓴다.
/// </summary>
public sealed class ThumbnailMaker(HttpClient http, PaperOptions options)
{
    public const int LargeWidth = 960;
    public const int SmallWidth = 480;
    private const int Quality = 76;
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
            using var original = SKBitmap.Decode(bytes);
            if (original is null || original.Width <= 0 || original.Height <= 0)
                return false;

            Directory.CreateDirectory(Path.GetDirectoryName(options.ThumbPath(date, story.Id, small: false))!);
            Save(original, LargeWidth, options.ThumbPath(date, story.Id, small: false));
            Save(original, SmallWidth, options.ThumbPath(date, story.Id, small: true));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void Save(SKBitmap original, int maxWidth, string path)
    {
        // 원본보다 키우지는 않는다.
        var width = Math.Min(maxWidth, original.Width);
        var height = Math.Max(1, (int)Math.Round(original.Height * (width / (double)original.Width)));

        using var resized = width == original.Width
            ? original.Copy()
            : original.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Webp, Quality);

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
