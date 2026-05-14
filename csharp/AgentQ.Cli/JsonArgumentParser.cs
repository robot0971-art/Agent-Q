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
        return input switch
        {
            JsonElement json when json.ValueKind == JsonValueKind.Object => ParseJsonObject(json),
            string rawJson => TryParseJsonObject(rawJson),
            _ => new Dictionary<string, object?>()
        };
    }

    private static Dictionary<string, object?> TryParseJsonObject(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                return ParseJsonObject(doc.RootElement);
            }
        }
        catch
        {
        }

        return new Dictionary<string, object?>();
    }

    private static Dictionary<string, object?> ParseJsonObject(JsonElement element)
    {
        var inputDict = new Dictionary<string, object?>();
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
