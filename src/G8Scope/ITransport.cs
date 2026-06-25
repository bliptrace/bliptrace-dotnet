namespace G8Scope;

/// <summary>
/// Ships an envelope to the ingest API. Implementations MUST never throw.
/// </summary>
public interface ITransport
{
    void Send(string endpoint, string key, EnvelopeModel envelope, int timeoutMs);
}
