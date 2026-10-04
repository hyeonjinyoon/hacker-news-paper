using HnPaper.Web.Services;

// CLI: `dotnet run -- collect [--out <path>]`, `dotnet run -- validate [yyyy-MM-dd]`
if (args.Length > 0 && args[0] is "collect" or "validate")
    return await Cli.RunAsync(args);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddSingleton(PaperOptions.Resolve(builder.Configuration["Paper:DataDirectory"]));
builder.Services.AddSingleton<EditionStore>();
builder.Services.AddSingleton<GradientLibrary>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

// 정적 파일을 라우팅보다 먼저 처리해야 /favicon.svg 같은 한 단계 경로가 1면의 /{date?}에 잡히지 않는다.
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

app.Run();
return 0;
