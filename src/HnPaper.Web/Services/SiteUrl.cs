namespace HnPaper.Web.Services;

/// <summary>
/// 검색엔진·공유용 절대 주소의 앞부분. Paper:SiteUrl(https://news.uvucorp.com)이 있으면 그것을 쓴다.
/// 사이트가 Cloudflare 뒤에서 http로 요청을 받아 요청 주소로는 https를 알 수 없기 때문이다. 설정이 없으면 요청 주소를 쓴다.
/// </summary>
public sealed class SiteUrl(IConfiguration configuration)
{
    private readonly string? _configured = configuration["Paper:SiteUrl"]?.TrimEnd('/');

    public string For(HttpRequest request) =>
        string.IsNullOrEmpty(_configured) ? $"{request.Scheme}://{request.Host}{request.PathBase}" : _configured;
}
