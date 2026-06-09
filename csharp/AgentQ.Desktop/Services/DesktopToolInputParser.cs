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
            IReadOnlyDictionary<string, object?> values => new Dictionary<string, object?>(values, StringComparer.Ordinal),
            _ => []
        };
    }

    private static Dictionary<string, object?> ParseRawJson(string rawJson)
    {
        try
        {
            using var document = JsonDocument.Parse(rawJson);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? ParseObject(document.RootElement)
                : [];
        }
        catch
        {
            return [];
        }
    }

    private static Dictionary<string, object?> ParseObject(JsonElement element)
    {
        var values = new Dictionary<string, object?>();
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
