using System.Text.RegularExpressions;

namespace AgentQ.Desktop.Services;

public static partial class McpToolName
{
    public static string Build(string serverName, string toolName)
    {
        var server = Sanitize(serverName);
        var tool = Sanitize(toolName);
        return $"mcp_{server}_{tool}";
    }

    private static string Sanitize(string value)
    {
        value = InvalidNameCharsRegex().Replace(value.Trim().ToLowerInvariant(), "_");
        value = DuplicateUnderscoresRegex().Replace(value, "_").Trim('_');
        return string.IsNullOrWhiteSpace(value) ? "unnamed" : value;
    }

    [GeneratedRegex(@"[^a-z0-9_]+", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidNameCharsRegex();

    [GeneratedRegex(@"_+", RegexOptions.CultureInvariant)]
    private static partial Regex DuplicateUnderscoresRegex();
}
