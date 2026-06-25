using System.Text.Json.Serialization;

namespace G8Scope;

/// <summary>
/// Typed, serialisable representation of the g8scope error envelope.
/// Property names map to the wire contract via <see cref="JsonPropertyNameAttribute"/>.
/// NOTE: <c>project</c> is intentionally absent — it is resolved server-side from
/// the ingest key, so a client cannot spoof another tenant.
/// </summary>
public sealed class EnvelopeModel
{
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; set; } = "1.0";

    [JsonPropertyName("event_id")]
    public string EventId { get; set; } = "";

    [JsonPropertyName("occurred_at")]
    public string OccurredAt { get; set; } = "";

    [JsonPropertyName("runtime")]
    public RuntimeInfo Runtime { get; set; } = new();

    [JsonPropertyName("sdk")]
    public SdkInfo Sdk { get; set; } = new();

    [JsonPropertyName("environment")]
    public string Environment { get; set; } = "production";

    [JsonPropertyName("release")]
    public string? Release { get; set; }

    [JsonPropertyName("level")]
    public string Level { get; set; } = "error";

    [JsonPropertyName("exception")]
    public ExceptionInfo Exception { get; set; } = new();

    [JsonPropertyName("context")]
    public IDictionary<string, object?> Context { get; set; } = new Dictionary<string, object?>();
}

public sealed class RuntimeInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "dotnet";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";
}

public sealed class SdkInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";
}

public sealed class ExceptionInfo
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("frames")]
    public IList<Frame> Frames { get; set; } = new List<Frame>();
}

public sealed class Frame
{
    [JsonPropertyName("file")]
    public string File { get; set; } = "";

    [JsonPropertyName("line")]
    public int Line { get; set; }

    [JsonPropertyName("function")]
    public string Function { get; set; } = "";

    [JsonPropertyName("in_app")]
    public bool InApp { get; set; }
}
