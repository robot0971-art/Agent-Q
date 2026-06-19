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
        var path = ToolInputParser.GetString(input, "path");
        if (!string.IsNullOrWhiteSpace(path))
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

            var includeHidden = ToolInputParser.TryGetBoolean(input, "includeHidden", out var parsedIncludeHidden) && parsedIncludeHidden;
            var requestedLimit = ToolInputParser.TryGetInt32(input, "limit", out var parsedLimit) ? parsedLimit : DefaultLimit;
            if (requestedLimit <= 0)
            {
                return Task.FromResult(ToolResult.Error("limit must be greater than 0"));
            }

            var limit = Math.Min(requestedLimit, MaximumLimit);
            var entries = Directory.EnumerateFileSystemEntries(fullPath)
                .Where(entry => includeHidden || !IsHidden(entry))
                .OrderBy(entry => !IsDirectory(entry))
                .ThenBy(entry => Path.GetFileName(entry), StringComparer.OrdinalIgnoreCase)
                .Take(limit + 1)
                .ToList();

            var limitReached = entries.Count > limit;
            if (limitReached)
            {
                entries = entries.Take(limit).ToList();
            }

            var entryItems = entries
                .Select(entry => CreateEntryItem(fullPath, entry))
                .Where(entry => entry != null)
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
            var name = Path.GetFileName(path);
            if (!string.IsNullOrWhiteSpace(name) && name.StartsWith(".", StringComparison.Ordinal))
            {
                return true;
            }

            return File.GetAttributes(path).HasFlag(FileAttributes.Hidden);
        }
        catch
        {
            return false;
        }
    }

    private static Dictionary<string, object?>? CreateEntryItem(string rootPath, string entry)
    {
        if (!TryGetAttributes(entry, out var attributes))
        {
            return null;
        }

        var isDirectory = attributes.HasFlag(FileAttributes.Directory);
        var isReparsePoint = attributes.HasFlag(FileAttributes.ReparsePoint);

        return new Dictionary<string, object?>
        {
            ["name"] = Path.GetFileName(entry),
            ["path"] = entry,
            ["relativePath"] = Path.GetRelativePath(rootPath, entry).Replace('\\', '/'),
            ["type"] = isDirectory ? "directory" : "file",
            ["isReparsePoint"] = isReparsePoint,
            ["sizeBytes"] = isDirectory || isReparsePoint ? null : new FileInfo(entry).Length
        };
    }

    private static bool IsDirectory(string path)
    {
        return TryGetAttributes(path, out var attributes) && attributes.HasFlag(FileAttributes.Directory);
    }

    private static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch
        {
            attributes = default;
            return false;
        }
    }

}
