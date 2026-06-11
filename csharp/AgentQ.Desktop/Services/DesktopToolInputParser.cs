using System.Text.Json;

namespace AgentQ.Desktop.Services;

internal static class DesktopToolInputParser
{
    public static Dictionary<string, object?> Parse(object? input)
    {
        return input switch
        {
            JsonElement json when json.ValueKind == JsonValueKind.Object => ParseObject(json),
            string rawJson => ParseRawJson(rawJson),
            IReadOnlyDictionary<string, object?> values => new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase),
            _ => []
        };
    }

    private static Dictionary<string, object?> ParseRawJson(string rawJson)
    {
        try
        {
            using var document = JsonDocument.Parse(rawJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return ParseObject(document.RootElement);
            }

            if (document.RootElement.ValueKind == JsonValueKind.String)
            {
                var nestedJson = document.RootElement.GetString();
                if (!string.IsNullOrWhiteSpace(nestedJson))
                {
                    return ParseRawJson(nestedJson);
                }
            }
        }
        catch
        {
        }

        return [];
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
