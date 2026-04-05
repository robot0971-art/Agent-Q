using System.Text.Json;

namespace AgentQ.Tools;

public class PluginEchoTool : ITool
{
    public string Name => "plugin_echo";
    public string Description => "Echo plugin-style input for parity testing";
    public bool RequiresPermission => false;
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            message = new { type = "string", description = "Message to echo back" }
        },
        required = new[] { "message" }
    };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, object?> input, CancellationToken ct = default)
    {
        if (!input.TryGetValue("message", out var messageObj) || messageObj is not string message)
        {
            return Task.FromResult(ToolResult.Error("Missing required parameter: message"));
        }

        var output = new Dictionary<string, object?>
        {
            ["input"] = new Dictionary<string, object?>
            {
                ["message"] = message
            },
            ["message"] = message
        };

        return Task.FromResult(ToolResult.Success(JsonSerializer.Serialize(output)));
    }
}

