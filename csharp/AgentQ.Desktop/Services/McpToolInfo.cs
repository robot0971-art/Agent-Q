using System.Text.Json;

namespace AgentQ.Desktop.Services;

public sealed class McpToolInfo
{
    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public JsonElement InputSchema { get; init; }
}
