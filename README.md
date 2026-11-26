# Bliptrace .NET SDK

The runtime **.NET / C#** SDK for [Bliptrace](https://bliptrace.com) — a central,
multi-tenant error-capture and observability platform. The SDK builds a
language-neutral *error envelope* from an `Exception` and ships it to the bliptrace
ingest API.

It is designed to be invisible to the host application: **capture never throws**,
and a slow or unreachable ingest endpoint will never slow your app down.

## Installation

```bash
dotnet add package Bliptrace
```

## Usage

```csharp
using Bliptrace;

var client = new BliptraceClient(new Config
{
    Key = "your-ingest-key",                 // required; without it the SDK does nothing
    Endpoint = "https://ingest.scope.g8suite.com",
    Environment = "production",
    Release = "my-app@2026.06.21",
    BasePath = "/srv/app",                    // used to mark in-app stack frames
    ScrubKeys = new[] { "password", "token", "authorization" },
    IgnoreExceptions = new[] { "System.OperationCanceledException" },
});

try
{
    DoSomethingRisky();
}
catch (Exception ex)
{
    client.Capture(ex, new Dictionary<string, object?>
    {
        ["user"] = new Dictionary<string, object?> { ["id"] = "u_123" },
        ["request"] = new Dictionary<string, object?>
        {
            ["method"] = "POST",
            ["url"] = "/password/reset",
        },
    });
}
```

### Deferring the send

Adapters can split the build and the send — for example to ship the envelope
*after* the HTTP response has been returned to the user:

```csharp
var envelope = client.BuildEnvelope(ex, context); // null if disabled / no key / ignored
if (envelope != null)
{
    client.SendEnvelope(envelope);
}
```

## Configuration

| Property            | Default                                  | Description                                            |
|---------------------|------------------------------------------|--------------------------------------------------------|
| `Key`               | `null`                                   | Ingest key. Without it the SDK does nothing.           |
| `Endpoint`          | `https://ingest.scope.g8suite.com`       | Base URL; the SDK POSTs to `{Endpoint}/v1/ingest`.     |
| `Environment`       | `production`                             | Deployment environment.                                |
| `Release`           | `null`                                   | Release marker.                                        |
| `TimeoutMs`         | `3000`                                   | HTTP timeout in milliseconds.                          |
| `MaxFrames`         | `30`                                     | Maximum stack frames captured.                         |
| `MaxMessage`        | `2000`                                   | Maximum exception message length before truncation.    |
| `IgnoreExceptions`  | `[]`                                     | Full type names to skip (matches subclasses too).      |
| `ScrubKeys`         | `[]`                                     | Context keys (case-insensitive) to redact.             |
| `BasePath`          | `""`                                     | Application base path for in-app frame detection.      |
| `Enabled`           | `true`                                   | Master switch.                                         |
| `SdkName`           | `bliptrace-dotnet`                         | SDK name reported in the envelope.                     |
| `SdkVersion`        | `1.0.0`                                  | SDK version reported in the envelope.                  |

## PII scrubbing

Configured keys are redacted to `[REDACTED]` (case-insensitive) before the
envelope leaves your process. Scrubbing recurses into nested dictionaries and
enumerables. Note that Bliptrace also scrubs server-side, but client-side scrubbing
keeps sensitive values off the wire entirely.

## Anti-spoofing

The envelope **never** contains a `project` field. The project (tenant) is
resolved **server-side** from your ingest key and stamped by the ingest API, so a
client cannot spoof another tenant's project.

## Licence

MIT © Developers Hub Sdn Bhd
