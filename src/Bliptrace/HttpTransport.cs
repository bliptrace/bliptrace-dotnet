using System.Net;
using System.Text;
using System.Text.Json;

namespace Bliptrace;

/// <summary>
/// Default transport: POSTs the envelope to <c>{endpoint}/v1/ingest</c> using
/// <see cref="HttpClient"/>. Swallows all errors so capture never breaks the host app.
///
/// Adds transport-level retry with exponential backoff + jitter (<see cref="RetryPolicy"/>).
/// The SAME serialized body — hence the SAME event_id — is replayed on every attempt,
/// so the ingest API's idempotency dedupe makes a retry safe: it never creates a
/// duplicate occurrence (spec §10).
/// </summary>
public class HttpTransport : ITransport
{
    private static readonly HttpClient SharedClient = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    private readonly HttpClient _client;
    private readonly RetryPolicy _policy;

    public HttpTransport(HttpClient? client = null, RetryPolicy? policy = null)
    {
        _client = client ?? SharedClient;
        _policy = policy ?? new RetryPolicy();
    }

    public void Send(string endpoint, string key, EnvelopeModel envelope, int timeoutMs)
    {
        try
        {
            var url = endpoint.TrimEnd('/') + "/v1/ingest";
            var json = JsonSerializer.Serialize(envelope, JsonOptions);

            // Retry loop. The same body (hence the same event_id) is replayed on each
            // attempt, so the ingest API's idempotency dedupe makes this safe — a retry
            // never creates a duplicate occurrence (spec §10).
            var attempt = 0;
            while (true)
            {
                var (status, retryAfterSec) = Attempt(url, json, key, timeoutMs);

                if (!_policy.ShouldRetry(status, attempt))
                {
                    return;
                }

                SleepMs(_policy.DelayMs(attempt, retryAfterSec));
                attempt++;
            }
        }
        catch
        {
            // Capturing must never break the monitored app.
        }
    }

    /// <summary>
    /// Perform one HTTP attempt. Returns the HTTP status and the numeric Retry-After
    /// (seconds, 0 if absent); status is 0 on a transport error/timeout. Virtual so
    /// tests can script status sequences without real network.
    /// </summary>
    protected virtual (int Status, int RetryAfterSec) Attempt(string url, string body, string key, int timeoutMs)
    {
        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = content,
            };
            request.Headers.TryAddWithoutValidation("X-Ingest-Key", key);

            using var cts = new System.Threading.CancellationTokenSource(Math.Max(1, timeoutMs));

            using var response = _client.SendAsync(request, cts.Token).GetAwaiter().GetResult();

            return ((int)response.StatusCode, ParseRetryAfter(response));
        }
        catch
        {
            // Transport error / timeout — represented as status 0 so the policy can retry.
            return (0, 0);
        }
    }

    /// <summary>Sleep between attempts. Virtual so tests can run without real delay.</summary>
    protected virtual void SleepMs(int ms)
    {
        if (ms > 0)
        {
            System.Threading.Thread.Sleep(ms);
        }
    }

    /// <summary>Parse a numeric Retry-After header (seconds); 0 when absent or non-numeric.</summary>
    private static int ParseRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            foreach (var value in values)
            {
                if (int.TryParse(value, out var seconds) && seconds > 0)
                {
                    return seconds;
                }
            }
        }

        return 0;
    }
}
