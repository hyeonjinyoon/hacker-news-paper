using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HnPaper.Web.Services;

/// <summary>
/// 데이터 폴더 위치. 설정(Paper:DataDirectory, 환경 변수 Paper__DataDirectory)이 없으면
/// 저장소 루트의 data/를 쓴다.
/// </summary>
public sealed record PaperOptions(string DataDirectory)
{
    public string RawDirectory => Path.Combine(DataDirectory, "raw");
    public string KoDirectory => Path.Combine(DataDirectory, "ko");

    public string RawPath(string date) => Path.Combine(RawDirectory, date + ".json");
    public string KoPath(string date) => Path.Combine(KoDirectory, date + ".json");

    // 기사별 파일: 댓글 수집본, 중간 페이지 본문 번역본, 댓글 번역본
    public string RawItemPath(string date, long id) => Path.Combine(RawDirectory, date, id + ".json");
    public string KoItemPath(string date, long id) => Path.Combine(KoDirectory, date, id + ".json");
    public string KoCommentsPath(string date, long id) => Path.Combine(KoDirectory, date, id + ".comments.json");

    // 줄여 저장한 대표 이미지: data/img/{date}/{id}.webp(큰 것), {id}-s.webp(작은 것). 사이트는 /thumbs/로 제공한다.
    public string ThumbDirectory => Path.Combine(DataDirectory, "img");
    public string ThumbPath(string date, long id, bool small) => Path.Combine(ThumbDirectory, date, id + (small ? "-s.webp" : ".webp"));

    public static PaperOptions Resolve(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return new PaperOptions(Path.GetFullPath(configured));

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "src", "HnPaper.Web", "HnPaper.Web.csproj")))
                    return new PaperOptions(Path.Combine(dir.FullName, "data"));
            }
        }
        return new PaperOptions(Path.Combine(Directory.GetCurrentDirectory(), "data"));
    }
}

public static class Kst
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul");
    public static readonly Regex DatePattern = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

    public static DateTimeOffset Now => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone);
}

public static class PaperJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static T? Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);

    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, Options) + "\n");
    }
}
