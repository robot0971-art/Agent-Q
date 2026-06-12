using System.Text.Json;

namespace AgentQ.Desktop.Services;

internal static class DesktopToolInputParser
{
    public static Dictionary<string, object?> Parse(object? input)
    {
        return TryParse(input, out var parsed, out _)
            ? parsed
            : [];
    }

    public static bool TryParse(
        object? input,
        out Dictionary<string, object?> parsed,
        out string error)
    {
        parsed = [];
        error = string.Empty;
        switch (input)
        {
            case null:
                return true;
            case JsonElement json when json.ValueKind == JsonValueKind.Object:
                parsed = ParseObject(json);
                return true;
            case JsonElement json when json.ValueKind == JsonValueKind.String:
                return TryParseRawJson(json.GetString() ?? string.Empty, out parsed, out error);
            case JsonElement json:
                error = $"Tool input JSON must be an object; received {json.ValueKind}.";
                return false;
            case string rawJson:
                return TryParseRawJson(rawJson, out parsed, out error);
            case IReadOnlyDictionary<string, object?> values:
                parsed = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
                return true;
            default:
                return true;
        }
    }

    private static bool TryParseRawJson(
        string rawJson,
        out Dictionary<string, object?> parsed,
        out string error)
    {
        parsed = [];
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                parsed = ParseObject(document.RootElement);
                return true;
            }

            if (document.RootElement.ValueKind == JsonValueKind.String)
            {
                var nestedJson = document.RootElement.GetString();
                if (!string.IsNullOrWhiteSpace(nestedJson))
                {
                    return TryParseRawJson(nestedJson, out parsed, out error);
                }

                return true;
            }

            error = $"Tool input JSON must be an object; received {document.RootElement.ValueKind}.";
            return false;
        }
        catch (JsonException ex)
        {
            error = $"Tool input JSON is malformed: {ex.Message}";
            return false;
        }
    }

    private static Dictionary<string, object?> ParseObject(JsonElement element)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            values[property.Name] = ParseValue(property.Value);
        }

        return values;
    }

    private static object? ParseValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => ParseObject(element),
            JsonValueKind.Array => element.EnumerateArray().Select(ParseValue).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt32(out var intValue) => intValue,
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }
}
