namespace HnPaper.Web.Services;

/// <summary>
/// wwwroot/img/gradients의 그라데이션 이미지 목록(scripts/generate-gradients.py로 만든다).
/// 대표 이미지가 없는 기사에 HN id로 하나를 골라 주므로 같은 기사는 늘 같은 패널이 나온다.
/// 폴더에 이미지를 더하면 다음 실행부터 후보에 들어간다.
/// </summary>
public sealed class GradientLibrary
{
    private const string Folder = "img/gradients";
    private static readonly HashSet<string> Extensions = [".webp", ".jpg", ".jpeg", ".png", ".avif"];

    private readonly string[] _urls;

    public GradientLibrary(IWebHostEnvironment env)
    {
        var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        var dir = Path.Combine(webRoot, Folder);
        _urls = Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir)
                .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Select(f => $"/{Folder}/{Path.GetFileName(f)}")
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];
    }

    public string? UrlFor(long id) =>
        _urls.Length == 0 ? null : _urls[(int)(Mix(id) % (ulong)_urls.Length)];

    // 연속된 id도 고르게 흩어지도록 섞는다.
    private static ulong Mix(long id) => unchecked((ulong)id * 0x9E3779B97F4A7C15UL) >> 17;
}
