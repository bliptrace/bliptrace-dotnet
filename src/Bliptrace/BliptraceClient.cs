namespace Bliptrace;

/// <summary>
/// The runtime .NET client: decide whether to capture, build + scrub the envelope,
/// and ship it. Adapters either call <see cref="Capture"/> directly or split
/// <see cref="BuildEnvelope"/> / <see cref="SendEnvelope"/> to defer the send.
/// This client MUST never throw into the host application.
/// </summary>
public sealed class BliptraceClient
{
    private readonly Config _config;
    private readonly ITransport _transport;
    private readonly Scrubber _scrubber;

    public BliptraceClient(Config config, ITransport? transport = null, Scrubber? scrubber = null)
    {
        _config = config;
        _transport = transport ?? new HttpTransport();
        _scrubber = scrubber ?? new Scrubber(config.ScrubKeys);
    }

    /// <summary>Capture and ship immediately. Never throws.</summary>
    public void Capture(Exception ex, IDictionary<string, object?>? context = null)
    {
        try
        {
            var envelope = BuildEnvelope(ex, context);
            if (envelope != null)
            {
                SendEnvelope(envelope);
            }
        }
        catch
        {
            // never break the app
        }
    }

    /// <summary>
    /// Build the envelope (scrubbing context), or <c>null</c> when capture is disabled,
    /// unconfigured, or the exception type is ignored. Never throws.
    /// </summary>
    public EnvelopeModel? BuildEnvelope(Exception ex, IDictionary<string, object?>? context = null)
    {
        try
        {
            if (!_config.Enabled || string.IsNullOrEmpty(_config.Key))
            {
                return null;
            }

            if (ShouldIgnore(ex))
            {
                return null;
            }

            var scrubbed = _scrubber.Scrub(context);
            return Envelope.Build(ex, _config, scrubbed);
        }
        catch
        {
            return null; // never break the app
        }
    }

    /// <summary>Ship a previously built envelope. Never throws.</summary>
    public void SendEnvelope(EnvelopeModel envelope)
    {
        try
        {
            if (!string.IsNullOrEmpty(_config.Key))
            {
                _transport.Send(_config.Endpoint, _config.Key!, envelope, _config.TimeoutMs);
            }
        }
        catch
        {
            // never break the app
        }
    }

    private bool ShouldIgnore(Exception ex)
    {
        if (_config.IgnoreExceptions.Count == 0)
        {
            return false;
        }

        for (var t = ex.GetType(); t != null; t = t.BaseType)
        {
            var full = t.FullName;
            if (full != null && _config.IgnoreExceptions.Contains(full))
            {
                return true;
            }
        }

        return false;
    }
}
