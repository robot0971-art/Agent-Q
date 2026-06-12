using System.Text.Json;

namespace AgentQ.Tools;

/// <summary>
/// Echo tool used for plugin-style parity tests.
/// </summary>
public class PluginEchoTool : ITool
{
    /// <summary>
    /// Tool name.
    /// </summary>
    public string Name => "plugin_echo";

    /// <summary>
    /// Tool description.
    /// </summary>
    public string Description => "Echo plugin-style input for parity testing";

    /// <summary>
    /// This test helper does not mutate local state.
    /// </summary>
    public bool RequiresPermission => false;

    /// <summary>
    /// Tool input JSON schema.
    /// </summary>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            message = new { type = "string", description = "Message to echo back" }
        },
        required = new[] { "message" }
    };

    /// <summary>
    /// Echoes the provided message.
    /// </summary>
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
