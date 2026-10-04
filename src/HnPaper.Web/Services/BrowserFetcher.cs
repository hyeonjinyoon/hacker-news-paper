using Microsoft.Playwright;

namespace HnPaper.Web.Services;

/// <summary>
/// 봇 차단에 막힌 원문을 화면에 창을 띄운 Chrome(헤드리스 아님)으로 다시 열어 HTML을 가져온다.
/// 설치된 Google Chrome을 쓰므로 브라우저를 따로 내려받지 않는다. 처음 필요할 때 띄우고, 한 번에 한 페이지씩 연다.
/// </summary>
public sealed class BrowserFetcher : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private bool _unavailable;

    /// <summary>브라우저로 다시 연 원문 수</summary>
    public int Opened { get; private set; }

    /// <returns>최종 주소(리다이렉트 뒤)와 HTML. 브라우저를 띄우지 못했거나 페이지를 열지 못하면 null.</returns>
    public async Task<(Uri Url, string Html)?> FetchHtmlAsync(Uri uri, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (await LaunchAsync() is not { } browser)
                return null;

            Opened++;
            // 글마다 새 컨텍스트(창)를 열어 쿠키가 다음 글로 넘어가지 않게 한다.
            await using var context = await browser.NewContextAsync();
            var page = await context.NewPageAsync();
            await page.GotoAsync(uri.ToString(), new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
            // 봇 확인 페이지가 스스로 넘어갈 시간을 준다.
            await page.WaitForTimeoutAsync(2_000);
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            return (new Uri(page.Url), await page.ContentAsync());
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            Console.Error.WriteLine($"브라우저로 열지 못함: {uri} ({e.GetType().Name}: {e.Message.Split('\n')[0]})");
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IBrowser?> LaunchAsync()
    {
        if (_browser is not null || _unavailable)
            return _browser;
        try
        {
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new() { Channel = "chrome", Headless = false });
        }
        catch (Exception e)
        {
            // Chrome이 없거나 띄우지 못하면 이번 수집에서는 브라우저를 다시 시도하지 않는다.
            _unavailable = true;
            Console.Error.WriteLine($"브라우저를 띄우지 못해 차단된 원문의 미리보기를 건너뜁니다: {e.Message.Split('\n')[0]}");
        }
        return _browser;
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
            await _browser.CloseAsync();
        _playwright?.Dispose();
        _lock.Dispose();
    }
}
