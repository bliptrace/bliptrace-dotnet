using System.Diagnostics;
using System.Runtime.InteropServices;

namespace G8Scope;

/// <summary>
/// Builds the language-neutral g8scope error envelope from an <see cref="Exception"/>.
/// </summary>
public static class Envelope
{
    private static readonly string[] FrameworkNamespaces = { "System.", "Microsoft." };

    /// <param name="ex">The exception to capture.</param>
    /// <param name="config">SDK configuration.</param>
    /// <param name="context">Already-scrubbed context (request/user/custom).</param>
    public static EnvelopeModel Build(Exception ex, Config config, IDictionary<string, object?>? context = null)
    {
        return new EnvelopeModel
        {
            SchemaVersion = "1.0",
            EventId = Guid.NewGuid().ToString(),
            OccurredAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            Runtime = new RuntimeInfo
            {
                Name = "dotnet",
                Version = RuntimeVersion(),
            },
            Sdk = new SdkInfo
            {
                Name = config.SdkName,
                Version = config.SdkVersion,
            },
            Environment = config.Environment,
            Release = config.Release,
            Level = "error",
            Exception = new ExceptionInfo
            {
                Type = ex.GetType().FullName ?? ex.GetType().Name,
                Message = Limit(ex.Message ?? "", config.MaxMessage),
                Frames = BuildFrames(ex, config),
            },
            Context = NormaliseContext(context),
        };
    }

    private static string RuntimeVersion()
    {
        try
        {
            var desc = RuntimeInformation.FrameworkDescription;
            return string.IsNullOrWhiteSpace(desc)
                ? System.Environment.Version.ToString()
                : desc;
        }
        catch
        {
            return System.Environment.Version.ToString();
        }
    }

    private static IList<Frame> BuildFrames(Exception ex, Config config)
    {
        var frames = new List<Frame>();

        try
        {
            var trace = new StackTrace(ex, true);
            foreach (var sf in trace.GetFrames())
            {
                if (sf == null)
                {
                    continue;
                }

                var method = sf.GetMethod();
                var declaringType = method?.DeclaringType;
                var fileName = sf.GetFileName();
                var file = string.IsNullOrEmpty(fileName)
                    ? (declaringType?.FullName ?? "[internal]")
                    : fileName!;

                var function = declaringType != null && method != null
                    ? declaringType.FullName + "." + method.Name
                    : (method?.Name ?? "[unknown]");

                frames.Add(new Frame
                {
                    File = file,
                    Line = sf.GetFileLineNumber(),
                    Function = function,
                    InApp = IsInApp(file, declaringType?.Namespace, config.BasePath),
                });

                if (frames.Count >= config.MaxFrames)
                {
                    break;
                }
            }
        }
        catch
        {
            // Frame extraction must never break capture.
        }

        return frames;
    }

    private static bool IsInApp(string file, string? ns, string basePath)
    {
        if (!string.IsNullOrEmpty(basePath))
        {
            return file.StartsWith(basePath, StringComparison.Ordinal) && !IsFrameworkNamespace(ns);
        }

        // No base path configured: fall back to the namespace heuristic.
        return !IsFrameworkNamespace(ns);
    }

    private static bool IsFrameworkNamespace(string? ns)
    {
        if (string.IsNullOrEmpty(ns))
        {
            return false;
        }

        foreach (var prefix in FrameworkNamespaces)
        {
            if (ns!.StartsWith(prefix, StringComparison.Ordinal) ||
                ns == prefix.TrimEnd('.'))
            {
                return true;
            }
        }

        return false;
    }

    private static IDictionary<string, object?> NormaliseContext(IDictionary<string, object?>? context)
    {
        return context == null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(context);
    }

    private static string Limit(string value, int max)
    {
        if (max < 0)
        {
            max = 0;
        }

        return value.Length > max ? value.Substring(0, max) : value;
    }
}
