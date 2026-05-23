using System.Text.Json;

namespace AgentQ.Desktop.Services;

public interface IMcpClient
{
    Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(McpServerConfig server, CancellationToken ct = default);

    Task<JsonElement> CallToolAsync(McpServerConfig server, string toolName, JsonElement arguments, CancellationToken ct = default);
}
