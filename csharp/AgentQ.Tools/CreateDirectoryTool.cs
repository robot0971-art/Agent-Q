using System.Text.Json;

namespace AgentQ.Tools;

public class CreateDirectoryTool : ITool
{
    public string Name => "create_directory";

    public string Description =>
        "Create a new empty folder inside the workspace. Use this for requests like creating a folder; do not use shell commands for simple folder creation.";

    public bool RequiresPermission => true;

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Workspace-relative folder path to create" }
        },
        required = new[] { "path" }
    };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, object?> input, CancellationToken ct = default)
    {
        var path = ToolInputParser.GetString(input, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(ToolResult.Error("Missing required parameter: path"));
        }

        try
        {
            if (!ToolPathGuard.TryResolvePath(path, out var fullPath, out var errorMessage))
            {
                return Task.FromResult(ToolResult.Error(errorMessage!));
            }

            if (File.Exists(fullPath))
            {
                return Task.FromResult(ToolResult.Error($"Path points to an existing file, not a folder: {path}"));
            }

            var existedBefore = Directory.Exists(fullPath);
            if (!existedBefore)
            {
                Directory.CreateDirectory(fullPath);
            }

            var output = new Dictionary<string, object?>
            {
                ["path"] = path,
                ["directoryPath"] = fullPath,
                ["created"] = !existedBefore,
                ["status"] = existedBefore ? "already_exists" : "success"
            };

            return Task.FromResult(ToolResult.Success(JsonSerializer.Serialize(output)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to create folder: {ex.Message}"));
        }
    }

}
