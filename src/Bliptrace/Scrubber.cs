using System.Collections;

namespace Bliptrace;

/// <summary>
/// Client-side PII scrub: redact configured keys (case-insensitive) before sending.
/// Recurses into nested dictionaries and enumerables.
/// </summary>
public sealed class Scrubber
{
    private const string Redacted = "[REDACTED]";

    private readonly HashSet<string> _keys;

    public Scrubber(IEnumerable<string>? keys = null)
    {
        _keys = new HashSet<string>(
            keys ?? Enumerable.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns a scrubbed copy of <paramref name="data"/>. The input is not mutated.
    /// </summary>
    public IDictionary<string, object?> Scrub(IDictionary<string, object?>? data)
    {
        var result = new Dictionary<string, object?>();
        if (data == null)
        {
            return result;
        }

        foreach (var pair in data)
        {
            if (_keys.Contains(pair.Key))
            {
                result[pair.Key] = Redacted;
                continue;
            }

            result[pair.Key] = ScrubValue(pair.Value);
        }

        return result;
    }

    private object? ScrubValue(object? value)
    {
        switch (value)
        {
            case null:
                return null;

            case string:
                return value;

            case IDictionary<string, object?> typed:
                return Scrub(typed);

            case IDictionary dict:
            {
                var copy = new Dictionary<string, object?>();
                foreach (DictionaryEntry entry in dict)
                {
                    var key = entry.Key?.ToString() ?? "";
                    copy[key] = _keys.Contains(key) ? Redacted : ScrubValue(entry.Value);
                }

                return copy;
            }

            case IEnumerable enumerable:
            {
                var list = new List<object?>();
                foreach (var item in enumerable)
                {
                    list.Add(ScrubValue(item));
                }

                return list;
            }

            default:
                return value;
        }
    }
}
