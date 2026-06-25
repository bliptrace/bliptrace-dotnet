using System.Text.RegularExpressions;
using G8Scope;
using Xunit;

namespace G8Scope.Tests;

/// <summary>Records envelopes instead of shipping them over the network.</summary>
public sealed class FakeTransport : ITransport
{
    public readonly List<(string Endpoint, string Key, EnvelopeModel Envelope, int TimeoutMs)> Sent = new();

    public void Send(string endpoint, string key, EnvelopeModel envelope, int timeoutMs)
    {
        Sent.Add((endpoint, key, envelope, timeoutMs));
    }
}

public class ClientTest
{
    private static Exception ThrownException(string message = "boom")
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [Fact]
    public void BuildsAndShipsAValidEnvelope()
    {
        var transport = new FakeTransport();
        var client = new G8ScopeClient(
            new Config { Key = "k", Endpoint = "http://api" },
            transport);

        client.Capture(ThrownException());

        Assert.Single(transport.Sent);
        var env = transport.Sent[0].Envelope;

        Assert.Equal("1.0", env.SchemaVersion);
        Assert.Matches(new Regex("^[0-9a-f-]{36}$"), env.EventId);
        Assert.Equal("dotnet", env.Runtime.Name);
        Assert.Equal("g8scope-dotnet", env.Sdk.Name);
        Assert.Equal("1.0.0", env.Sdk.Version);
        Assert.Equal("error", env.Level);
        Assert.Equal("System.InvalidOperationException", env.Exception.Type);
        Assert.Equal("boom", env.Exception.Message);

        Assert.NotEmpty(env.Exception.Frames);
        var frame = env.Exception.Frames[0];
        Assert.False(string.IsNullOrEmpty(frame.File));
        Assert.False(string.IsNullOrEmpty(frame.Function));
        // line is an int (always present); in_app is a bool (always present)
        Assert.True(frame.Line >= 0);
    }

    [Fact]
    public void OccurredAtIsRfc3339WithMillisecondPrecision()
    {
        var transport = new FakeTransport();
        new G8ScopeClient(new Config { Key = "k" }, transport).Capture(ThrownException());

        var occurredAt = transport.Sent[0].Envelope.OccurredAt;
        Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$"), occurredAt);
    }

    [Fact]
    public void IgnoresConfiguredExceptionTypes()
    {
        var transport = new FakeTransport();
        var client = new G8ScopeClient(
            new Config
            {
                Key = "k",
                IgnoreExceptions = new List<string> { "System.InvalidOperationException" },
            },
            transport);

        client.Capture(ThrownException());

        Assert.Empty(transport.Sent);
    }

    [Fact]
    public void DoesNothingWithoutAKeyOrWhenDisabled()
    {
        var transport = new FakeTransport();

        new G8ScopeClient(new Config { Key = null }, transport).Capture(ThrownException());
        new G8ScopeClient(new Config { Key = "k", Enabled = false }, transport).Capture(ThrownException());

        Assert.Empty(transport.Sent);
    }

    [Fact]
    public void ScrubsSensitiveContextBeforeSending()
    {
        var transport = new FakeTransport();
        var client = new G8ScopeClient(
            new Config { Key = "k", ScrubKeys = new List<string> { "password" } },
            transport);

        client.Capture(ThrownException(), new Dictionary<string, object?>
        {
            ["user"] = new Dictionary<string, object?>
            {
                ["password"] = "hunter2",
                ["id"] = "7",
            },
        });

        var context = transport.Sent[0].Envelope.Context;
        var user = Assert.IsType<Dictionary<string, object?>>(context["user"]);
        Assert.Equal("[REDACTED]", user["password"]);
        Assert.Equal("7", user["id"]);
    }

    [Fact]
    public void ScrubIsCaseInsensitiveAndRecursesIntoEnumerables()
    {
        var scrubber = new Scrubber(new[] { "token" });
        var result = scrubber.Scrub(new Dictionary<string, object?>
        {
            ["Token"] = "secret",
            ["items"] = new List<object?>
            {
                new Dictionary<string, object?> { ["token"] = "nested" },
            },
        });

        Assert.Equal("[REDACTED]", result["Token"]);
        var items = Assert.IsType<List<object?>>(result["items"]);
        var first = Assert.IsType<Dictionary<string, object?>>(items[0]);
        Assert.Equal("[REDACTED]", first["token"]);
    }

    [Fact]
    public void NeverThrowsEvenIfContextIsWeird()
    {
        var transport = new FakeTransport();
        var client = new G8ScopeClient(new Config { Key = "k" }, transport);

        var exception = Record.Exception(() => client.Capture(
            ThrownException(),
            new Dictionary<string, object?>
            {
                ["fn"] = (Func<int>)(() => 1),
                ["self_ref"] = new object(),
            }));

        Assert.Null(exception);
        Assert.Single(transport.Sent);
    }

    [Fact]
    public void TruncatesLongMessages()
    {
        var transport = new FakeTransport();
        var longMessage = new string('x', 5000);
        Exception ex;
        try
        {
            throw new Exception(longMessage);
        }
        catch (Exception e)
        {
            ex = e;
        }

        new G8ScopeClient(new Config { Key = "k", MaxMessage = 100 }, transport).Capture(ex);

        Assert.Equal(100, transport.Sent[0].Envelope.Exception.Message.Length);
    }

    [Fact]
    public void NeverPutsProjectInThePayload()
    {
        // The EnvelopeModel has no project member; assert no such JSON property serialises.
        var transport = new FakeTransport();
        new G8ScopeClient(new Config { Key = "k" }, transport).Capture(ThrownException());

        var json = System.Text.Json.JsonSerializer.Serialize(transport.Sent[0].Envelope);
        Assert.DoesNotContain("\"project\"", json);
    }
}
