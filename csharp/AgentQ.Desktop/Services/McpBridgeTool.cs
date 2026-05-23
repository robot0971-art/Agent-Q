using System.IO;
using System.Text.Json;
using AgentQ.Tools;

namespace AgentQ.Desktop.Services;

public sealed class McpBridgeTool(
    string name,
    McpServerConfig server,
    McpToolInfo tool,
    IMcpClient client) : ITool
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public string Name { get; } = name;

    public string Description => string.IsNullOrWhiteSpace(tool.Description)
        ? $"Call MCP tool {tool.Name} on server {server.Name}."
        : $"Call MCP tool {tool.Name} on server {server.Name}. {tool.Description}";

    public object InputSchema => tool.InputSchema.ValueKind == JsonValueKind.Undefined
        ? new
        {
            type = "object",
            additionalProperties = true
        }
        : tool.InputSchema;

    public bool RequiresPermission => true;

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object?> input, CancellationToken ct = default)
    {
        try
        {
            var arguments = JsonSerializer.SerializeToElement(input, JsonOptions);
            var result = await client.CallToolAsync(server, tool.Name, arguments, ct);
            return ToolResult.Success(JsonSerializer.Serialize(result, JsonOptions));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException or TaskCanceledException)
        {
            return ToolResult.Error($"MCP tool call failed: {ex.Message}");
        }
    }
}
