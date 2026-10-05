using System.Security.Cryptography;

namespace HnPaper.Web.Services;

/// <summary>
/// wwwroot/img/gradients의 패널 이미지 목록(scripts/generate-gradients.py로 만든다).
/// 대표 이미지가 없는 기사에 HN id로 하나를 골라 주므로 같은 기사는 늘 같은 패널이 나온다(기사 공유 이미지도 같은 패널).
/// 폴더에 이미지를 더하면 다음 실행부터 후보에 들어간다.
/// 주소에는 파일 내용으로 만든 버전(?v=)을 붙여, 같은 이름으로 다시 만들어도 브라우저에 캐시된 옛 이미지가 쓰이지 않는다.
/// </summary>
public sealed class GradientLibrary
{
    private const string Folder = "img/gradients";
    private static readonly HashSet<string> Extensions = [".webp", ".jpg", ".jpeg", ".png", ".avif"];

    private readonly string[] _files;
    private readonly string[] _versions;

    public GradientLibrary(IWebHostEnvironment env)
    {
        var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        var dir = Path.Combine(webRoot, Folder);
        _files = Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir)
                .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                .ToArray()
            : [];
        _versions = _files.Select(f => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(f)))[..8]).ToArray();
    }

    public string? UrlFor(long id) =>
        IndexFor(id) is { } i ? $"/{Folder}/{Path.GetFileName(_files[i])}?v={_versions[i]}" : null;

    /// <summary>고른 패널의 파일 경로. 기사 공유 이미지(OgImageMaker)를 그릴 때 쓴다.</summary>
    public string? PathFor(long id) =>
        IndexFor(id) is { } i ? _files[i] : null;

    private int? IndexFor(long id) =>
        _files.Length == 0 ? null : (int)(Mix(id) % (ulong)_files.Length);

    // 연속된 id도 고르게 흩어지도록 섞는다.
    private static ulong Mix(long id) => unchecked((ulong)id * 0x9E3779B97F4A7C15UL) >> 17;
}
