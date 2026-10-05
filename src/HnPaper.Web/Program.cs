using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using HnPaper.Web.Services;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.WebEncoders;

// CLI: `dotnet run -- collect`, `collect-items <yyyy-MM-dd>`, `collect-thumbs <yyyy-MM-dd>`, `collect-fill <yyyy-MM-dd>`,
// `reuse <yyyy-MM-dd>`, `merge-comments <yyyy-MM-dd> <id>`, `validate [yyyy-MM-dd] [id ...]`
if (args.Length > 0 && args[0] is "collect" or "collect-items" or "collect-thumbs" or "collect-fill" or "reuse" or "merge-comments" or "validate")
    return await Cli.RunAsync(args);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddSingleton(PaperOptions.Resolve(builder.Configuration["Paper:DataDirectory"]));
builder.Services.AddSingleton<EditionStore>();
builder.Services.AddSingleton<GradientLibrary>();
builder.Services.AddSingleton<OgImageMaker>();

// 한글을 &#xAC70; 같은 엔티티로 바꾸지 않고 그대로 출력한다(HTML이 크게 줄어든다). HTML 특수문자는 그대로 인코딩된다.
builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

// HTML·CSS를 Brotli/Gzip으로 압축해 보낸다. 페이지에 비밀 정보가 없어 압축해도 안전하다.
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.UseResponseCompression();

// 정적 파일을 라우팅보다 먼저 처리해야 /favicon.svg 같은 한 단계 경로가 1면의 /{date?}에 잡히지 않는다.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // 버전(?v=)이 붙은 파일(CSS, 파비콘, 공유 이미지)은 내용이 바뀌면 주소도 바뀌므로 1년 캐시한다. 나머지(그라데이션)는 7일.
        var versioned = ctx.Context.Request.Query.ContainsKey("v");
        ctx.Context.Response.Headers.CacheControl = versioned
            ? "public, max-age=31536000, immutable"
            : "public, max-age=604800";
    },
});
// 수집 때 WebP로 압축해 둔 대표 이미지(data/img)를 /thumbs/로 제공한다. 날짜·기사별로 바뀌지 않으므로 30일 캐시한다.
var paper = app.Services.GetRequiredService<PaperOptions>();
Directory.CreateDirectory(paper.ThumbDirectory);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(paper.ThumbDirectory),
    RequestPath = "/thumbs",
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "public, max-age=2592000",
});
app.UseRouting();
app.MapRazorPages();

// 기사 페이지의 공유 이미지(og:image). 처음 요청할 때 그려 저장해 두고 그 파일을 준다.
// 주소의 v가 지금 내용으로 만든 key와 같으면 그 주소의 내용은 바뀌지 않으므로 1년 캐시한다.
// 링크 미리보기를 만드는 서비스 가운데 HEAD로 먼저 확인하는 곳이 있어 HEAD도 받는다.
app.MapMethods("/{date}/{id:long}/og.jpg", [HttpMethods.Get, HttpMethods.Head], (string date, long id, string? v, HttpContext http, EditionStore store, OgImageMaker og) =>
{
    var story = store.Load(date)?.All.FirstOrDefault(s => s.Raw.Id == id);
    if (story is null)
        return Results.NotFound();

    var key = og.Key(story);
    http.Response.Headers.CacheControl = v == key ? "public, max-age=31536000, immutable" : "public, max-age=3600";
    return Results.File(og.GetOrCreate(story, key), "image/jpeg");
});

app.Run();
return 0;
