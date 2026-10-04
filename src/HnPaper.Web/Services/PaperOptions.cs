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
