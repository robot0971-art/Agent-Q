using System.Text.Json;

namespace AgentQ.Tools;

internal static class ToolInputParser
{
    public static string? GetString(IReadOnlyDictionary<string, object?> input, string key, bool fallbackToString = false)
    {
        if (!input.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ when fallbackToString => value.ToString(),
            _ => null
        };
    }

    public static bool TryGetString(IReadOnlyDictionary<string, object?> input, string key, out string value)
    {
        value = GetString(input, key) ?? string.Empty;
        return value.Length > 0;
    }

    public static bool TryGetInt32(IReadOnlyDictionary<string, object?> input, string key, out int value)
    {
        value = 0;
        if (!input.TryGetValue(key, out var rawValue) || rawValue is null)
        {
            return false;
        }

        switch (rawValue)
        {
            case int intValue:
                value = intValue;
                return true;
            case long longValue when longValue is >= int.MinValue and <= int.MaxValue:
                value = (int)longValue;
                return true;
            case double doubleValue when doubleValue is >= int.MinValue and <= int.MaxValue:
                value = (int)doubleValue;
                return true;
            case string stringValue when int.TryParse(stringValue, out var parsed):
                value = parsed;
                return true;
            case JsonElement json when json.TryGetInt32(out var parsed):
                value = parsed;
                return true;
            default:
                return false;
        }
    }

    public static int? GetInt32(IReadOnlyDictionary<string, object?> input, string key)
    {
        return TryGetInt32(input, key, out var value) ? value : null;
    }

    public static bool TryGetBoolean(IReadOnlyDictionary<string, object?> input, string key, out bool value)
    {
        value = false;
        if (!input.TryGetValue(key, out var rawValue) || rawValue is null)
        {
            return false;
        }

        switch (rawValue)
        {
            case bool boolValue:
                value = boolValue;
                return true;
            case string stringValue when bool.TryParse(stringValue, out var parsed):
                value = parsed;
                return true;
            case JsonElement { ValueKind: JsonValueKind.True }:
                value = true;
                return true;
            case JsonElement { ValueKind: JsonValueKind.False }:
                value = false;
                return true;
            default:
                return false;
        }
    }

    public static bool GetBoolean(IReadOnlyDictionary<string, object?> input, string key, bool fallback = false)
    {
        return TryGetBoolean(input, key, out var value) ? value : fallback;
    }
}
