namespace G8Scope;

/// <summary>
/// SDK configuration. Framework adapters build this from their own config.
/// </summary>
public sealed class Config
{
    /// <summary>Ingest key. Without it, the SDK does nothing (no capture).</summary>
    public string? Key { get; set; }

    /// <summary>Ingest API base URL. The SDK POSTs to <c>{Endpoint}/v1/ingest</c>.</summary>
    public string Endpoint { get; set; } = "https://ingest.scope.g8suite.com";

    /// <summary>Deployment environment, e.g. production, staging, local.</summary>
    public string Environment { get; set; } = "production";

    /// <summary>Optional release marker, e.g. my-app@2026.06.21.</summary>
    public string? Release { get; set; }

    /// <summary>HTTP timeout in milliseconds.</summary>
    public int TimeoutMs { get; set; } = 3000;

    /// <summary>Maximum number of transport retries after the first attempt.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Base backoff delay in milliseconds for the exponential schedule.</summary>
    public int RetryBaseMs { get; set; } = 100;

    /// <summary>Hard cap on any single backoff delay, in milliseconds.</summary>
    public int RetryCapMs { get; set; } = 2000;

    /// <summary>Maximum number of stack frames to capture.</summary>
    public int MaxFrames { get; set; } = 30;

    /// <summary>Maximum exception message length before truncation.</summary>
    public int MaxMessage { get; set; } = 2000;

    /// <summary>Full type names of exceptions to skip capturing.</summary>
    public IList<string> IgnoreExceptions { get; set; } = new List<string>();

    /// <summary>Context keys (case-insensitive) to redact before sending.</summary>
    public IList<string> ScrubKeys { get; set; } = new List<string>();

    /// <summary>Application base path used to decide whether a frame is in-app.</summary>
    public string BasePath { get; set; } = "";

    /// <summary>Master switch. When false, the SDK does nothing.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>SDK name reported in the envelope.</summary>
    public string SdkName { get; set; } = "g8scope-dotnet";

    /// <summary>SDK version reported in the envelope.</summary>
    public string SdkVersion { get; set; } = "1.0.0";
}
