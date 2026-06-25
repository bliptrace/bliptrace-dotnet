using System.Text;
using System.Text.Json;

namespace G8Scope;

/// <summary>
/// Default transport: POSTs the envelope to <c>{endpoint}/v1/ingest</c> using
/// <see cref="HttpClient"/>. Swallows all errors so capture never breaks the host app.
/// </summary>
public sealed class HttpTransport : ITransport
{
    private static readonly HttpClient SharedClient = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    private readonly HttpClient _client;

    public HttpTransport(HttpClient? client = null)
    {
        _client = client ?? SharedClient;
    }

    public void Send(string endpoint, string key, EnvelopeModel envelope, int timeoutMs)
    {
        try
        {
            var url = endpoint.TrimEnd('/') + "/v1/ingest";
            var json = JsonSerializer.Serialize(envelope, JsonOptions);

            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = content,
            };
            request.Headers.TryAddWithoutValidation("X-Ingest-Key", key);

            using var cts = new System.Threading.CancellationTokenSource(Math.Max(1, timeoutMs));

            // Fire synchronously but bounded by the timeout; ignore the outcome.
            _client.SendAsync(request, cts.Token).GetAwaiter().GetResult();
        }
        catch
        {
            // Capturing must never break the monitored app.
        }
    }
}
