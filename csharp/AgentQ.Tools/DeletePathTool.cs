using System.Text.Json;

namespace AgentQ.Tools;

public class DeletePathTool : ITool
{
    public string Name => "delete_path";

    public string Description =>
        "Delete a workspace-relative file or empty folder. Use this for explicit delete requests instead of shell commands. Set recursive=true only when the user explicitly asked to delete a folder and approval is granted.";

    public bool RequiresPermission => true;

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Workspace-relative file or folder path to delete" },
            recursive = new { type = "boolean", description = "Delete a directory recursively. Default false." }
        },
        required = new[] { "path" }
    };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, object?> input, CancellationToken ct = default)
    {
        var path = TryGetString(input, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(ToolResult.Error("Missing required parameter: path"));
        }

        var recursive = TryGetBoolean(input, "recursive");

        try
        {
            if (!ToolPathGuard.TryResolvePath(path, out var fullPath, out var errorMessage))
            {
                return Task.FromResult(ToolResult.Error(errorMessage!));
            }

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                return Task.FromResult(Success(path, fullPath, "file", recursive));
            }

            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive);
                return Task.FromResult(Success(path, fullPath, "directory", recursive));
            }

            return Task.FromResult(ToolResult.Error($"Path not found: {path}"));
        }
        catch (IOException ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to delete path: {ex.Message}"));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to delete path: {ex.Message}"));
        }
    }

    private static ToolResult Success(string path, string fullPath, string kind, bool recursive)
    {
        var output = new Dictionary<string, object?>
        {
            ["path"] = path,
            ["deletedPath"] = fullPath,
            ["kind"] = kind,
            ["recursive"] = recursive,
            ["status"] = "success"
        };

        return ToolResult.Success(JsonSerializer.Serialize(output));
    }

    private static string? TryGetString(IReadOnlyDictionary<string, object?> input, string key)
    {
        if (!input.TryGetValue(key, out var value) || value == null)
        {
            return null;
        }

        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
            _ => null
        };
    }

    private static bool TryGetBoolean(IReadOnlyDictionary<string, object?> input, string key)
    {
        if (!input.TryGetValue(key, out var value) || value == null)
        {
            return false;
        }

        return value switch
        {
            bool flag => flag,
            string text when bool.TryParse(text, out var parsed) => parsed,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            _ => false
        };
    }
}
