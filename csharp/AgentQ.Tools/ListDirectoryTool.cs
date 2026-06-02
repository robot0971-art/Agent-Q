using System.Text.Json;

namespace AgentQ.Tools;

public sealed class ListDirectoryTool : ITool
{
    private const int DefaultLimit = 200;
    private const int MaximumLimit = 500;

    public string Name => "list_directory";

    public string Description =>
        "List files and folders in a workspace directory. Use this before shell commands when checking whether a folder is empty or discovering top-level project structure.";

    public bool RequiresPermission => false;

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Directory path to list. Defaults to the workspace root/current directory." },
            includeHidden = new { type = "boolean", description = "Whether to include hidden files and folders. Defaults to false." },
            limit = new { type = "integer", description = "Maximum number of entries to return. Defaults to 200 and clamps at 500." }
        }
    };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, object?> input, CancellationToken ct = default)
    {
        var requestedPath = ".";
        if (input.TryGetValue("path", out var pathObj) && pathObj is string path && !string.IsNullOrWhiteSpace(path))
        {
            requestedPath = path;
        }

        try
        {
            if (!ToolPathGuard.TryResolvePath(requestedPath, out var fullPath, out var errorMessage))
            {
                return Task.FromResult(ToolResult.Error(errorMessage!));
            }

            if (!Directory.Exists(fullPath))
            {
                return Task.FromResult(ToolResult.Error($"Directory not found: {requestedPath}"));
            }

            var includeHidden = TryGetBoolean(input, "includeHidden", out var parsedIncludeHidden) && parsedIncludeHidden;
            var requestedLimit = TryGetInt32(input, "limit", out var parsedLimit) ? parsedLimit : DefaultLimit;
            if (requestedLimit <= 0)
            {
                return Task.FromResult(ToolResult.Error("limit must be greater than 0"));
            }

            var limit = Math.Min(requestedLimit, MaximumLimit);
            var entries = Directory.EnumerateFileSystemEntries(fullPath)
                .Where(entry => includeHidden || !IsHidden(entry))
                .OrderBy(entry => !Directory.Exists(entry))
                .ThenBy(entry => Path.GetFileName(entry), StringComparer.OrdinalIgnoreCase)
                .Take(limit + 1)
                .ToList();

            var limitReached = entries.Count > limit;
            if (limitReached)
            {
                entries = entries.Take(limit).ToList();
            }

            var entryItems = entries
                .Select(entry => new Dictionary<string, object?>
                {
                    ["name"] = Path.GetFileName(entry),
                    ["path"] = entry,
                    ["relativePath"] = Path.GetRelativePath(fullPath, entry).Replace('\\', '/'),
                    ["type"] = Directory.Exists(entry) ? "directory" : "file",
                    ["sizeBytes"] = Directory.Exists(entry) ? null : new FileInfo(entry).Length
                })
                .ToList();

            var output = new Dictionary<string, object?>
            {
                ["path"] = requestedPath,
                ["fullPath"] = fullPath,
                ["entryCount"] = entryItems.Count,
                ["isEmpty"] = entryItems.Count == 0,
                ["limit"] = limit,
                ["requestedLimit"] = requestedLimit,
                ["limitReached"] = limitReached,
                ["includeHidden"] = includeHidden,
                ["entries"] = entryItems
            };

            return Task.FromResult(ToolResult.Success(JsonSerializer.Serialize(output)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to list directory: {ex.Message}"));
        }
    }

    private static bool IsHidden(string path)
    {
        try
        {
            return new FileInfo(path).Attributes.HasFlag(FileAttributes.Hidden);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetBoolean(Dictionary<string, object?> input, string key, out bool value)
    {
        value = false;
        if (!input.TryGetValue(key, out var rawValue) || rawValue == null)
        {
            return false;
        }

        if (rawValue is bool boolValue)
        {
            value = boolValue;
            return true;
        }

        if (rawValue is string stringValue && bool.TryParse(stringValue, out var parsed))
        {
            value = parsed;
            return true;
        }

        if (rawValue is JsonElement json &&
            (json.ValueKind == JsonValueKind.True || json.ValueKind == JsonValueKind.False))
        {
            value = json.GetBoolean();
            return true;
        }

        return false;
    }

    private static bool TryGetInt32(Dictionary<string, object?> input, string key, out int value)
    {
        value = 0;
        if (!input.TryGetValue(key, out var rawValue) || rawValue == null)
        {
            return false;
        }

        if (rawValue is int intValue)
        {
            value = intValue;
            return true;
        }

        if (rawValue is long longValue && longValue is >= int.MinValue and <= int.MaxValue)
        {
            value = (int)longValue;
            return true;
        }

        if (rawValue is string stringValue && int.TryParse(stringValue, out var parsed))
        {
            value = parsed;
            return true;
        }

        if (rawValue is JsonElement json && json.TryGetInt32(out parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }
}
