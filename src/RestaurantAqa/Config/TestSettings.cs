namespace RestaurantAqa.Config;

/// <summary>
/// Settings of the system under test. Values come from the process environment first and from a
/// <c>.env</c> file (found by walking up from the test binary) second. Nothing secret is committed.
/// </summary>
public sealed class TestSettings
{
    private static readonly Lazy<TestSettings> Instance = new(() => new TestSettings());
    private readonly Dictionary<string, string> _fileValues;

    public static TestSettings Current => Instance.Value;

    private TestSettings()
    {
        _fileValues = LoadDotEnv();
    }

    /// <summary>Base URL of the API Gateway stage, e.g. https://abc.execute-api.eu-north-1.amazonaws.com/develop.</summary>
    public string ApiBaseUrl => Required("API_BASE_URL").TrimEnd('/');

    /// <summary>Base URL of the deployed frontend (CloudFront), used by the UI tests.</summary>
    public string UiBaseUrl => Required("UI_BASE_URL").TrimEnd('/');

    /// <summary>Optional: a table to book. When empty the first suitable table is discovered through the API.</summary>
    public string? TableId => Optional("TABLE_ID");

    /// <summary>Credentials of an existing waiter of the tested location. Waiter scenarios are skipped without them.</summary>
    public string? WaiterEmail => Optional("WAITER_EMAIL");

    public string? WaiterPassword => Optional("WAITER_PASSWORD");

    public bool HasWaiter => !string.IsNullOrWhiteSpace(WaiterEmail) && !string.IsNullOrWhiteSpace(WaiterPassword);

    /// <summary>Playwright browser channel: "chrome" or "msedge" to use an installed browser, empty for bundled Chromium.</summary>
    public string? BrowserChannel => Optional("BROWSER_CHANNEL");

    public bool Headed => string.Equals(Optional("HEADED"), "1", StringComparison.Ordinal);

    /// <summary>Video of UI tests (kept only for failed ones). Needs Playwright's ffmpeg, so it is off unless RECORD_VIDEO=1 (CI turns it on).</summary>
    public bool RecordVideo => string.Equals(Optional("RECORD_VIDEO"), "1", StringComparison.Ordinal);

    /// <summary>Directory for traces, screenshots and videos of failed UI tests.</summary>
    public string ArtifactsDir => Optional("ARTIFACTS_DIR") ?? Path.Combine(FindRepositoryRoot(), "artifacts");

    public string Get(string key) => Optional(key) ?? string.Empty;

    private string Required(string key) =>
        Optional(key) ?? throw new InvalidOperationException(
            $"Setting {key} is not configured. Copy .env.example to .env and fill it in, or set the {key} environment variable.");

    private string? Optional(string key)
    {
        string? value = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            _fileValues.TryGetValue(key, out value);
        }

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static Dictionary<string, string> LoadDotEnv()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string? path = FindUp(".env");
        if (path is null)
        {
            return values;
        }

        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            int separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim().Trim('"', '\'');
            values[key] = value;
        }

        return values;
    }

    private static string? FindUp(string fileName)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, "RestaurantAqa.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
