using System.Text.RegularExpressions;

namespace AgentQ.Desktop.Services;

public static partial class ModelReasoningTagFilter
{
    public static string Strip(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var withoutBlocks = ThinkBlockRegex().Replace(text, string.Empty);
        return ThinkTagRegex().Replace(withoutBlocks, string.Empty);
    }

    [GeneratedRegex(@"<think\b[^>]*>.*?</think>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ThinkBlockRegex();

    [GeneratedRegex(@"</?think\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ThinkTagRegex();
}
