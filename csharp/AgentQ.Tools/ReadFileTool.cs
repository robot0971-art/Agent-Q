using System.Text.Json;

namespace AgentQ.Tools;

public class ReadFileTool : ITool
{
    public string Name => "read_file";
    public string Description => "Read the contents of a file at the specified path";
    public bool RequiresPermission => false;
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Path to the file to read" },
            offset = new { type = "integer", description = "Line number to start reading from (1-indexed)" },
            limit = new { type = "integer", description = "Maximum number of lines to read" }
        },
        required = new[] { "path" }
    };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, object?> input, CancellationToken ct = default)
    {
        if (!input.TryGetValue("path", out var pathObj) || pathObj is not string path)
            return Task.FromResult(ToolResult.Error("Missing required parameter: path"));

        try
        {
            if (!ToolPathGuard.TryResolvePath(path, out var fullPath, out var errorMessage))
            {
                return Task.FromResult(ToolResult.Error(errorMessage!));
            }

            if (!File.Exists(fullPath))
                return Task.FromResult(ToolResult.Error($"File not found: {path}"));

            var lines = File.ReadAllLines(fullPath);
            int offset = 0;
            int limit = lines.Length;

            if (TryGetInt32(input, "offset", out var parsedOffset)) offset = Math.Max(0, parsedOffset - 1);
            if (TryGetInt32(input, "limit", out var parsedLimit)) limit = parsedLimit;

            offset = Math.Min(offset, lines.Length);
            limit = Math.Min(limit, lines.Length - offset);

            var selectedLines = lines.Skip(offset).Take(limit).ToArray();
            var content = string.Join("\n", selectedLines);

            var output = new Dictionary<string, object?>
            {
                ["path"] = path,
                ["content"] = content,
                ["totalLines"] = lines.Length,
                ["readLines"] = selectedLines.Length,
                ["offset"] = offset + 1,
                ["limit"] = limit
            };

            return Task.FromResult(ToolResult.Success(JsonSerializer.Serialize(output)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to read file: {ex.Message}"));
        }
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

