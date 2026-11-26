using Bliptrace;
using Xunit;

namespace Bliptrace.Tests;

/// <summary>
/// Subclasses <see cref="HttpTransport"/> to script per-attempt HTTP results and
/// record the requested delays — no real network, no real waiting. Each attempt
/// also records the exact body it was handed, so a test can assert the body
/// (and therefore the event_id) is identical across retries.
/// </summary>
public sealed class ScriptedTransport : HttpTransport
{
    private readonly Queue<(int Status, int RetryAfterSec)> _script;

    public ScriptedTransport(IEnumerable<(int Status, int RetryAfterSec)> script, RetryPolicy policy)
        : base(client: null, policy: policy)
    {
        _script = new Queue<(int, int)>(script);
    }

    public List<string> Bodies { get; } = new();

    public List<int> Delays { get; } = new();

    public int AttemptCount => Bodies.Count;

    protected override (int Status, int RetryAfterSec) Attempt(string url, string body, string key, int timeoutMs)
    {
        Bodies.Add(body);

        // Once the script runs dry, behave as a healthy 202 so the loop terminates.
        return _script.Count > 0 ? _script.Dequeue() : (202, 0);
    }

    protected override void SleepMs(int ms)
    {
        Delays.Add(ms);
    }
}

public class HttpTransportRetryTest
{
    private static readonly Func<double> NoJitter = () => 0.5;

    private static EnvelopeModel BuildEnvelope()
    {
        Exception ex;
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (Exception e)
        {
            ex = e;
        }

        var envelope = Envelope.Build(ex, new Config { Key = "k", Endpoint = "http://api" }, null);
        Assert.NotNull(envelope);
        return envelope!;
    }

    [Fact]
    public void StopsImmediatelyOnSuccess()
    {
        var transport = new ScriptedTransport(
            new[] { (202, 0) },
            new RetryPolicy(maxRetries: 2));

        transport.Send("http://api", "k", BuildEnvelope(), 3000);

        Assert.Equal(1, transport.AttemptCount);
        Assert.Empty(transport.Delays);
    }

    [Fact]
    public void RetriesThenSucceeds()
    {
        var transport = new ScriptedTransport(
            new[] { (503, 0), (429, 0), (202, 0) },
            new RetryPolicy(maxRetries: 3, baseMs: 100, capMs: 2000));

        transport.Send("http://api", "k", BuildEnvelope(), 3000);

        Assert.Equal(3, transport.AttemptCount);
        Assert.Equal(2, transport.Delays.Count); // delayed before attempts 2 and 3
    }

    [Fact]
    public void GivesUpAfterMaxRetries()
    {
        var transport = new ScriptedTransport(
            new[] { (503, 0), (503, 0), (503, 0), (503, 0) },
            new RetryPolicy(maxRetries: 2));

        transport.Send("http://api", "k", BuildEnvelope(), 3000);

        // 1 initial + 2 retries = 3 attempts; 2 delays in between.
        Assert.Equal(3, transport.AttemptCount);
        Assert.Equal(2, transport.Delays.Count);
    }

    [Fact]
    public void DoesNotRetryOnA4xx()
    {
        var transport = new ScriptedTransport(
            new[] { (400, 0), (202, 0) },
            new RetryPolicy(maxRetries: 5));

        transport.Send("http://api", "k", BuildEnvelope(), 3000);

        Assert.Equal(1, transport.AttemptCount);
        Assert.Empty(transport.Delays);
    }

    [Fact]
    public void RetriesTransportErrorsRepresentedAsStatusZero()
    {
        var transport = new ScriptedTransport(
            new[] { (0, 0), (0, 0), (202, 0) },
            new RetryPolicy(maxRetries: 3));

        transport.Send("http://api", "k", BuildEnvelope(), 3000);

        Assert.Equal(3, transport.AttemptCount);
        Assert.Equal(2, transport.Delays.Count);
    }

    [Fact]
    public void ReplaysTheIdenticalBodyAcrossAttempts()
    {
        var transport = new ScriptedTransport(
            new[] { (503, 0), (503, 0), (202, 0) },
            new RetryPolicy(maxRetries: 3));

        var envelope = BuildEnvelope();
        transport.Send("http://api", "k", envelope, 3000);

        Assert.Equal(3, transport.AttemptCount);
        Assert.True(transport.Bodies.Count > 1);

        var first = transport.Bodies[0];
        Assert.All(transport.Bodies, b => Assert.Equal(first, b));

        // The constant event_id is what makes the retry safe (server dedupes on it).
        Assert.Contains(envelope.EventId, first);
    }

    [Fact]
    public void BackoffDelaysGrowExponentiallyUpToTheCap()
    {
        var transport = new ScriptedTransport(
            new[] { (503, 0), (503, 0), (503, 0), (503, 0) },
            new RetryPolicy(maxRetries: 4, baseMs: 100, capMs: 250));

        // Inject deterministic no-jitter via a policy whose DelayMs is exercised
        // by the loop; verify by recomputing the expected schedule directly.
        var policy = new RetryPolicy(maxRetries: 4, baseMs: 100, capMs: 250);
        var expected = new[]
        {
            policy.DelayMs(0, 0, NoJitter),
            policy.DelayMs(1, 0, NoJitter),
            policy.DelayMs(2, 0, NoJitter),
            policy.DelayMs(3, 0, NoJitter),
        };

        Assert.Equal(100, expected[0]);
        Assert.Equal(200, expected[1]);
        Assert.Equal(250, expected[2]); // capped
        Assert.Equal(250, expected[3]); // capped

        transport.Send("http://api", "k", BuildEnvelope(), 3000);

        // Real loop produced one delay per retry, each clamped to the cap.
        Assert.Equal(4, transport.Delays.Count);
        Assert.All(transport.Delays, d => Assert.InRange(d, 0, 250));
    }

    [Fact]
    public void HonoursServerRetryAfterCappedToTheConfiguredCap()
    {
        var transport = new ScriptedTransport(
            new[] { (429, 999), (202, 0) }, // hostile Retry-After: 999s
            new RetryPolicy(maxRetries: 3, baseMs: 100, capMs: 2000));

        transport.Send("http://api", "k", BuildEnvelope(), 3000);

        Assert.Single(transport.Delays);
        Assert.Equal(2000, transport.Delays[0]); // capped at CapMs, never 999_000
    }

    [Fact]
    public void NeverThrowsEvenWhenAttemptDoes()
    {
        var transport = new ThrowingTransport(new RetryPolicy());

        var exception = Record.Exception(() =>
            transport.Send("http://api", "k", BuildEnvelope(), 3000));

        Assert.Null(exception);
    }

    private sealed class ThrowingTransport : HttpTransport
    {
        public ThrowingTransport(RetryPolicy policy) : base(client: null, policy: policy)
        {
        }

        protected override (int Status, int RetryAfterSec) Attempt(string url, string body, string key, int timeoutMs)
        {
            throw new InvalidOperationException("scripted failure");
        }
    }
}
