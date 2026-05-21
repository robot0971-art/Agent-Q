using System.IO;
using System.Text.Json;
using AgentQ.Tools;

namespace AgentQ.Desktop.Services;

public static class DesktopSearchRetryService
{
    private const int MaximumRetryAttempts = 2;

    public static IReadOnlyList<Dictionary<string, object?>> BuildRetryInputs(
        string toolName,
        IReadOnlyDictionary<string, object?> input,
        string resultContent)
    {
        if (!IsInsufficientSearchResult(toolName, resultContent))
        {
            return [];
        }

        return toolName switch
        {
            "grep_search" => BuildGrepRetryInputs(input),
            "glob_search" => BuildGlobRetryInputs(input),
            _ => []
        };
    }

    public static async Task<ToolResult> ApplySearchRetriesAsync(
        ITool tool,
        Dictionary<string, object?> originalInput,
        ToolResult originalResult,
        Action<string>? onRetry,
        CancellationToken ct)
    {
        if (originalResult.IsError)
        {
            return originalResult;
        }

        var retryInputs = BuildRetryInputs(tool.Name, originalInput, originalResult.Content);
        if (retryInputs.Count == 0)
        {
            return originalResult;
        }

        var retryOutputs = new List<SearchRetryOutput>();
        foreach (var retryInput in retryInputs.Take(MaximumRetryAttempts))
        {
            ct.ThrowIfCancellationRequested();
            var label = DescribeRetry(tool.Name, retryInput);
            onRetry?.Invoke(label);
            var retryResult = await tool.ExecuteAsync(retryInput, ct);
            retryOutputs.Add(new SearchRetryOutput(label, retryInput, retryResult));
        }

        return ToolResult.Success(BuildCombinedResult(tool.Name, originalResult.Content, retryOutputs));
    }

    private static IReadOnlyList<Dictionary<string, object?>> BuildGrepRetryInputs(IReadOnlyDictionary<string, object?> input)
    {
        if (!TryGetString(input, "pattern", out var pattern))
        {
            return [];
        }

        var retries = new List<Dictionary<string, object?>>();
        if (!pattern.Contains("(?i)", StringComparison.OrdinalIgnoreCase))
        {
            retries.Add(CloneWith(input, "pattern", $"(?i){pattern}"));
        }

        var relaxed = RelaxPattern(pattern);
        if (!string.Equals(relaxed, pattern, StringComparison.Ordinal) &&
            !retries.Any(retry => retry.TryGetValue("pattern", out var value) && string.Equals(value as string, relaxed, StringComparison.Ordinal)))
        {
            retries.Add(CloneWith(input, "pattern", relaxed));
        }

        return retries;
    }

    private static IReadOnlyList<Dictionary<string, object?>> BuildGlobRetryInputs(IReadOnlyDictionary<string, object?> input)
    {
        if (!TryGetString(input, "pattern", out var pattern))
        {
            return [];
        }

        var normalized = pattern.Replace('\\', '/');
        var retries = new List<Dictionary<string, object?>>();

        if (!normalized.StartsWith("**/", StringComparison.Ordinal) && !normalized.Contains('/', StringComparison.Ordinal))
        {
            retries.Add(CloneWith(input, "pattern", $"**/{normalized}"));
        }

        var fileName = Path.GetFileName(normalized);
        if (!string.IsNullOrWhiteSpace(fileName) && !fileName.Contains('*', StringComparison.Ordinal))
        {
            retries.Add(CloneWith(input, "pattern", $"**/*{fileName}*"));
        }

        return retries;
    }

    private static bool IsInsufficientSearchResult(string toolName, string resultContent)
    {
        try
        {
            using var document = JsonDocument.Parse(resultContent);
            var root = document.RootElement;
            return toolName switch
            {
                "grep_search" => root.TryGetProperty("numMatches", out var matches) && matches.GetInt32() == 0,
                "glob_search" => root.TryGetProperty("numFiles", out var files) && files.GetInt32() == 0,
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    private static string BuildCombinedResult(string toolName, string originalContent, IReadOnlyList<SearchRetryOutput> retryOutputs)
    {
        object? original;
        try
        {
            original = JsonSerializer.Deserialize<object>(originalContent);
        }
        catch
        {
            original = originalContent;
        }

        var output = new Dictionary<string, object?>
        {
            ["original"] = original,
            ["searchRetries"] = retryOutputs.Select(retry => new Dictionary<string, object?>
            {
                ["reason"] = retry.Reason,
                ["input"] = retry.Input,
                ["isError"] = retry.Result.IsError,
                ["result"] = TryDeserializeJson(retry.Result.Content)
            }).ToList()
        };

        output["retrySummary"] = toolName switch
        {
            "grep_search" => "Original grep returned no matches; AgentQ retried with broader pattern variants.",
            "glob_search" => "Original glob returned no files; AgentQ retried with broader path pattern variants.",
            _ => "Original search was insufficient; AgentQ retried with broader variants."
        };

        return JsonSerializer.Serialize(output);
    }

    private static object? TryDeserializeJson(string content)
    {
        try
        {
            return JsonSerializer.Deserialize<object>(content);
        }
        catch
        {
            return content;
        }
    }

    private static string DescribeRetry(string toolName, IReadOnlyDictionary<string, object?> input)
    {
        var pattern = TryGetString(input, "pattern", out var value) ? value : "(unknown)";
        return toolName switch
        {
            "grep_search" => $"Retry grep with broader pattern: {pattern}",
            "glob_search" => $"Retry glob with broader pattern: {pattern}",
            _ => $"Retry search with broader input: {pattern}"
        };
    }

    private static string RelaxPattern(string pattern)
    {
        var relaxed = pattern
            .Replace(@"\b", string.Empty, StringComparison.Ordinal)
            .Trim('^', '$');

        return string.IsNullOrWhiteSpace(relaxed) ? pattern : relaxed;
    }

    private static Dictionary<string, object?> CloneWith(IReadOnlyDictionary<string, object?> input, string key, object? value)
    {
        var clone = input.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        clone[key] = value;
        return clone;
    }

    private static bool TryGetString(IReadOnlyDictionary<string, object?> input, string key, out string value)
    {
        if (input.TryGetValue(key, out var raw) && raw is string text && !string.IsNullOrWhiteSpace(text))
        {
            value = text.ReplaceLineEndings(" ").Trim();
            return true;
        }

        value = string.Empty;
        return false;
    }

    private sealed record SearchRetryOutput(
        string Reason,
        IReadOnlyDictionary<string, object?> Input,
        ToolResult Result);
}
