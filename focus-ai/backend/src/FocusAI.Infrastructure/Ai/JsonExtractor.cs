using System.Text.Json;

namespace FocusAI.Infrastructure.Ai;

/// <summary>
/// Pulls a JSON object out of a model response. Even with JSON mode requested,
/// models wrap output in prose or ```json fences often enough that parsing the
/// raw string directly is not safe.
/// </summary>
internal static class JsonExtractor
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    public static JsonElement? TryExtract(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var candidate = Unfence(content.Trim());

        var start = candidate.IndexOf('{');
        var end = candidate.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        candidate = candidate[start..(end + 1)];

        try
        {
            using var document = JsonDocument.Parse(candidate, DocumentOptions);
            // The document is disposed on return, so hand back a detached clone.
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static double GetDouble(JsonElement element, string property, double fallback)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(
                value.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => fallback
        };
    }

    public static int GetInt(JsonElement element, string property, int fallback) =>
        (int)Math.Round(GetDouble(element, property, fallback));

    public static bool GetBool(JsonElement element, string property, bool fallback)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
    }

    public static List<string> GetStringList(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }

    public static Dictionary<string, string> GetStringMap(JsonElement element, string property)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var item in value.EnumerateObject())
        {
            if (item.Value.ValueKind == JsonValueKind.String)
            {
                var text = item.Value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    result[item.Name] = text;
                }
            }
        }

        return result;
    }

    public static TEnum GetEnum<TEnum>(JsonElement element, string property, TEnum fallback)
        where TEnum : struct, Enum
    {
        var raw = GetString(element, property);
        return Enum.TryParse<TEnum>(raw, ignoreCase: true, out var parsed) ? parsed : fallback;
    }

    public static DateTimeOffset? GetDate(JsonElement element, string property)
    {
        var raw = GetString(element, property);
        if (string.IsNullOrWhiteSpace(raw) || raw.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            raw,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static string Unfence(string content)
    {
        if (!content.StartsWith("```", StringComparison.Ordinal))
        {
            return content;
        }

        var firstNewline = content.IndexOf('\n');
        if (firstNewline < 0)
        {
            return content;
        }

        var body = content[(firstNewline + 1)..];
        var closing = body.LastIndexOf("```", StringComparison.Ordinal);
        return closing >= 0 ? body[..closing] : body;
    }
}
