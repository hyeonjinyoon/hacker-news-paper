using System.Security.Cryptography;
using HnPaper.Web.Models;

namespace HnPaper.Web.Services;

/// <summary>
/// wwwroot/img/gradients의 패널 이미지 목록(scripts/generate-gradients.py로 만든다).
/// 대표 이미지가 없는 기사에 HN id로 하나를 골라 주되, 한 호 안에서는 겹치지 않게 나눠 준다(Assign).
/// 같은 호의 같은 기사는 1면, 기사 페이지, 기사 공유 이미지에서 늘 같은 패널이 나온다.
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

    /// <summary>
    /// 한 호의 기사들에 패널을 나눠 준다. 순위대로 돌며 각 기사는 id로 정한 패널을 받고,
    /// 앞 순위 기사가 이미 받은 패널이면 아직 안 쓴 다음 패널을 받는다. 그래서 기사가 패널 수보다 적으면 한 호 안에서 겹치지 않는다.
    /// 패널을 다 쓰면 처음부터 다시 나눈다. 기사 위치는 바꾸지 않는다.
    /// </summary>
    public void Assign(IEnumerable<StoryView> stories)
    {
        if (_files.Length == 0)
            return;

        var used = new bool[_files.Length];
        var left = _files.Length;
        foreach (var story in stories)
        {
            if (left == 0)
            {
                Array.Clear(used);
                left = _files.Length;
            }
            var i = HashIndex(story.Raw.Id);
            while (used[i])
                i = (i + 1) % _files.Length;
            used[i] = true;
            left--;
            story.Panel = i;
        }
    }

    public string? UrlFor(StoryView story) =>
        IndexFor(story) is { } i ? $"/{Folder}/{Path.GetFileName(_files[i])}?v={_versions[i]}" : null;

    /// <summary>고른 패널의 파일 경로. 기사 공유 이미지(OgImageMaker)를 그릴 때 쓴다.</summary>
    public string? PathFor(StoryView story) =>
        IndexFor(story) is { } i ? _files[i] : null;

    // Assign으로 나눠 준 패널. 나눠 받지 않은 기사는 id로만 고른다.
    private int? IndexFor(StoryView story) =>
        _files.Length == 0 ? null : story.Panel ?? HashIndex(story.Raw.Id);

    private int HashIndex(long id) => (int)(Mix(id) % (ulong)_files.Length);

    // 연속된 id도 고르게 흩어지도록 섞는다.
    private static ulong Mix(long id) => unchecked((ulong)id * 0x9E3779B97F4A7C15UL) >> 17;
}
