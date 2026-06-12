using System.Text.Json;

namespace AgentQ.Cli;

/// <summary>
/// Tool input JSON values into dictionaries tools can consume.
/// </summary>
internal static class JsonArgumentParser
{
    public static Dictionary<string, object?> ParseJsonArguments(string jsonArgs)
    {
        using var doc = JsonDocument.Parse(jsonArgs);
        return ParseJsonObject(doc.RootElement);
    }

    public static Dictionary<string, object?> ParseInput(object? input)
    {
        return TryParseInput(input, out var parsed, out _)
            ? parsed
            : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    public static bool TryParseInput(
        object? input,
        out Dictionary<string, object?> parsed,
        out string error)
    {
        parsed = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        error = string.Empty;
        switch (input)
        {
            case null:
                return true;
            case JsonElement json when json.ValueKind == JsonValueKind.Object:
                parsed = ParseJsonObject(json);
                return true;
            case JsonElement json when json.ValueKind == JsonValueKind.String:
                return TryParseJsonObject(json.GetString() ?? string.Empty, out parsed, out error);
            case JsonElement json:
                error = $"Tool input JSON must be an object; received {json.ValueKind}.";
                return false;
            case string rawJson:
                return TryParseJsonObject(rawJson, out parsed, out error);
            case IReadOnlyDictionary<string, object?> values:
                parsed = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
                return true;
            default:
                return true;
        }
    }

    private static bool TryParseJsonObject(
        string rawJson,
        out Dictionary<string, object?> parsed,
        out string error)
    {
        parsed = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return true;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                parsed = ParseJsonObject(doc.RootElement);
                return true;
            }

            if (doc.RootElement.ValueKind == JsonValueKind.String)
            {
                var nestedJson = doc.RootElement.GetString();
                if (!string.IsNullOrWhiteSpace(nestedJson))
                {
                    return TryParseJsonObject(nestedJson, out parsed, out error);
                }

                return true;
            }

            error = $"Tool input JSON must be an object; received {doc.RootElement.ValueKind}.";
            return false;
        }
        catch (JsonException ex)
        {
            error = $"Tool input JSON is malformed: {ex.Message}";
            return false;
        }
    }

    private static Dictionary<string, object?> ParseJsonObject(JsonElement element)
    {
        var inputDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in element.EnumerateObject())
        {
            inputDict[prop.Name] = ParseJsonValue(prop.Value);
        }

        return inputDict;
    }

    private static object? ParseJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => ParseJsonObject(element),
            JsonValueKind.Array => element.EnumerateArray().Select(ParseJsonValue).ToList(),
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
