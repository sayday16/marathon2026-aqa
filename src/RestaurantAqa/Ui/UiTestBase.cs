using Microsoft.Playwright;
using RestaurantAqa.Config;

namespace RestaurantAqa.Ui;

/// <summary>
/// Base of the UI fixtures: one browser per fixture, a fresh isolated context (and page) per test.
/// For a failed test the trace, a screenshot and the video are kept in the artifacts directory and attached to the result.
/// </summary>
public abstract class UiTestBase
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private string _videoDir = null!;

    private IBrowserContext? _context;

    protected IBrowserContext Context => _context ?? throw new InvalidOperationException("The browser context is created in SetUp.");

    protected IPage Page { get; private set; } = null!;

    protected static TestSettings Settings => TestSettings.Current;

    [OneTimeSetUp]
    public async Task LaunchBrowser()
    {
        // The backend runs on Lambda: a cold start or a busy moment can take longer than the 5 s default of the assertions.
        Assertions.SetDefaultExpectTimeout(20_000);
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = !Settings.Headed,
            Channel = Settings.BrowserChannel,
        });
    }

    [SetUp]
    public async Task OpenContext()
    {
        _videoDir = Path.Combine(Path.GetTempPath(), "aqa-video-" + Guid.NewGuid().ToString("N"));
        _context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = Settings.UiBaseUrl,
            Locale = "en-US",
            // UTC keeps the dates and times shown by the application identical on a laptop and in CI.
            TimezoneId = "UTC",
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
            RecordVideoDir = Settings.RecordVideo ? _videoDir : null,
        });
        Context.SetDefaultTimeout(15_000);
        await Context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true, Sources = false });
        Page = await Context.NewPageAsync();
    }

    [TearDown]
    public async Task CloseContext()
    {
        if (_context is null)
        {
            return;
        }

        bool failed = TestContext.CurrentContext.Result.Outcome.Status == NUnit.Framework.Interfaces.TestStatus.Failed;
        string folder = Path.Combine(Settings.ArtifactsDir, "ui", Sanitize(TestContext.CurrentContext.Test.Name));
        string? tracePath = null;
        string? screenshotPath = null;

        if (failed)
        {
            Directory.CreateDirectory(folder);
            if (Page is not null)
            {
                screenshotPath = Path.Combine(folder, "failure.png");
                await Page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
            }

            tracePath = Path.Combine(folder, "trace.zip");
            await _context.Tracing.StopAsync(new TracingStopOptions { Path = tracePath });
        }
        else
        {
            await _context.Tracing.StopAsync();
        }

        IVideo? video = Page?.Video;
        await _context.CloseAsync();

        string? videoPath = null;
        if (failed && video is not null)
        {
            videoPath = Path.Combine(folder, "video.webm");
            await video.SaveAsAsync(videoPath);
        }

        TryDelete(_videoDir);

        foreach (string? path in new[] { screenshotPath, tracePath, videoPath })
        {
            if (path is not null && File.Exists(path))
            {
                TestContext.AddTestAttachment(path);
            }
        }
    }

    [OneTimeTearDown]
    public async Task CloseBrowser()
    {
        await _browser.CloseAsync();
        _playwright.Dispose();
    }

    private static string Sanitize(string name) =>
        string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_'));

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
