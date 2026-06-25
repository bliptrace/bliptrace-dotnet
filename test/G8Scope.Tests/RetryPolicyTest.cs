using G8Scope;
using Xunit;

namespace G8Scope.Tests;

public class RetryPolicyTest
{
    [Theory]
    [InlineData(0, true)]    // transport error / timeout
    [InlineData(408, true)]  // request timeout
    [InlineData(429, true)]  // too many requests
    [InlineData(500, true)]  // internal server error
    [InlineData(502, true)]  // bad gateway
    [InlineData(503, true)]  // service unavailable
    [InlineData(200, false)] // ok
    [InlineData(202, false)] // accepted
    [InlineData(400, false)] // bad request
    [InlineData(403, false)] // forbidden
    [InlineData(404, false)] // not found
    public void ClassifiesRetryableStatuses(int status, bool expected)
    {
        var policy = new RetryPolicy(maxRetries: 5);
        Assert.Equal(expected, policy.IsRetryable(status));
    }

    [Fact]
    public void StopsRetryingOnceTheAttemptCapIsReached()
    {
        var policy = new RetryPolicy(maxRetries: 2);

        Assert.True(policy.ShouldRetry(503, 0));
        Assert.True(policy.ShouldRetry(503, 1));
        Assert.False(policy.ShouldRetry(503, 2)); // exhausted
    }

    [Fact]
    public void DoesNotRetryNonRetryableStatusEvenWithAttemptsLeft()
    {
        var policy = new RetryPolicy(maxRetries: 5);

        Assert.False(policy.ShouldRetry(400, 0));
        Assert.False(policy.ShouldRetry(202, 0));
    }

    [Fact]
    public void GrowsTheDelayExponentiallyAndNeverExceedsTheCap()
    {
        var policy = new RetryPolicy(baseMs: 100, capMs: 2000);
        Func<double> noJitter = () => 0.5; // 2*0.5-1 = 0 → no jitter

        Assert.Equal(100, policy.DelayMs(0, 0, noJitter));
        Assert.Equal(200, policy.DelayMs(1, 0, noJitter));
        Assert.Equal(400, policy.DelayMs(2, 0, noJitter));
        Assert.Equal(2000, policy.DelayMs(8, 0, noJitter)); // capped
    }

    [Fact]
    public void KeepsJitteredDelayWithinPlusOrMinus25Percent()
    {
        var policy = new RetryPolicy(baseMs: 100, capMs: 100000);

        foreach (var jitter in new Func<double>[] { () => 0.0, () => 0.999, () => 0.5 })
        {
            var d = policy.DelayMs(3, 0, jitter); // base 800ms
            Assert.InRange(d, 600, 1000);
        }
    }

    [Fact]
    public void JitterFromTheDefaultRandomSourceStaysWithinBounds()
    {
        var policy = new RetryPolicy(baseMs: 100, capMs: 100000);

        for (var i = 0; i < 1000; i++)
        {
            var d = policy.DelayMs(3); // base 800ms, real random jitter
            Assert.InRange(d, 600, 1000);
        }
    }

    [Fact]
    public void HonoursAServerRetryAfterInSecondsCappedToCapMs()
    {
        var policy = new RetryPolicy(capMs: 5000);

        Assert.Equal(2000, policy.DelayMs(0, 2));   // 2s honoured
        Assert.Equal(5000, policy.DelayMs(0, 999)); // hostile value capped
    }

    [Fact]
    public void IgnoresAZeroOrNegativeRetryAfterAndFallsBackToBackoff()
    {
        var policy = new RetryPolicy(baseMs: 100, capMs: 2000);
        Func<double> noJitter = () => 0.5;

        Assert.Equal(100, policy.DelayMs(0, 0, noJitter));
        Assert.Equal(100, policy.DelayMs(0, -5, noJitter));
    }

    [Fact]
    public void BuildsFromSdkConfig()
    {
        var policy = RetryPolicy.FromConfig(new Config
        {
            Key = "k",
            MaxRetries = 7,
            RetryBaseMs = 50,
            RetryCapMs = 9000,
        });

        Assert.Equal(7, policy.MaxRetries);
        Assert.Equal(50, policy.BaseMs);
        Assert.Equal(9000, policy.CapMs);
    }

    [Fact]
    public void HasTheDocumentedDefaults()
    {
        var policy = new RetryPolicy();

        Assert.Equal(2, policy.MaxRetries);
        Assert.Equal(100, policy.BaseMs);
        Assert.Equal(2000, policy.CapMs);
    }
}
