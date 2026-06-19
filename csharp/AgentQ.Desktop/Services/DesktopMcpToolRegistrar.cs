using AgentQ.Tools;

namespace AgentQ.Desktop.Services;

public sealed class DesktopMcpToolRegistrar(IMcpClient? client = null)
{
    private readonly IMcpClient _client = client ?? new StdioMcpClient();

    public async Task RegisterAsync(ToolRegistry registry, string workspaceRoot, CancellationToken ct = default)
    {
        var projectConfig = ProjectAgentConfigService.LoadLocal(workspaceRoot);
        var servers = McpServerRegistry.EnabledServers(projectConfig, workspaceRoot);
        if (servers.Count == 0)
        {
            return;
        }

        foreach (var server in servers.Take(4))
        {
            IReadOnlyList<McpToolInfo> tools;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(12));
                tools = await _client.ListToolsAsync(server, cts.Token);
            }
            catch
            {
                continue;
            }

            foreach (var tool in tools.Take(16))
            {
                registry.TryRegister(new McpBridgeTool(
                    McpToolName.Build(server.Name, tool.Name),
                    server,
                    tool,
                    _client));
            }
        }
    }
}
