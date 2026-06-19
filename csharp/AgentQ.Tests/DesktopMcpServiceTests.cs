using AgentQ.Desktop.Services;
using AgentQ.Tools;
using System.Text.Json;
using Xunit;

namespace AgentQ.Tests;

public sealed class DesktopMcpServiceTests
{
    [Fact]
    public void McpServerRegistry_BuildsContextForEnabledServers()
    {
        var config = new ProjectAgentConfig
        {
            McpServers =
            [
                new McpServerConfig
                {
                    Name = "unity",
                    Command = "node",
                    Args = ["unity-mcp.js"],
                    Tags = ["trusted"]
                },
                new McpServerConfig
                {
                    Name = "disabled",
                    Command = "node",
                    Enabled = false
                }
            ]
        };

        var context = McpServerRegistry.BuildContext(config);

        Assert.Contains("Configured MCP servers", context);
        Assert.Contains("unity", context);
        Assert.DoesNotContain("disabled", context);
        Assert.Empty(McpServerRegistry.Validate(config));
    }

    [Fact]
    public void McpServerRegistry_DisablesUntrustedServers()
    {
        var config = new ProjectAgentConfig
        {
            McpServers =
            [
                new McpServerConfig
                {
                    Name = "untrusted",
                    Command = "node",
                    Args = ["server.js"]
                }
            ]
        };

        Assert.Empty(McpServerRegistry.EnabledServers(config));
        Assert.Contains(McpServerRegistry.Validate(config), warning => warning.Contains("trusted tag", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void McpServerRegistry_BlocksWorkspaceLocalExecutables()
    {
        var root = CreateTempDirectory();
        var command = Path.Combine(root, "tools", "server.exe");
        var config = new ProjectAgentConfig
        {
            McpServers =
            [
                new McpServerConfig
                {
                    Name = "local",
                    Command = command,
                    Tags = ["trusted"]
                }
            ]
        };

        Assert.Empty(McpServerRegistry.EnabledServers(config, root));
        Assert.Contains(McpServerRegistry.Validate(config, root), warning => warning.Contains("workspace-local executable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void McpServerRegistry_BuildContextUsesWorkspaceSafetyFilter()
    {
        var root = CreateTempDirectory();
        var command = Path.Combine(root, "node.exe");
        var config = new ProjectAgentConfig
        {
            McpServers =
            [
                new McpServerConfig
                {
                    Name = "local",
                    Command = command,
                    Tags = ["trusted"]
                }
            ]
        };

        var context = McpServerRegistry.BuildContext(config, root);

        Assert.Equal(string.Empty, context);
    }

    [Fact]
    public void McpServerRegistry_NormalizesRelativeWorkingDirectoryForExecution()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "mcp"));
        var config = new ProjectAgentConfig
        {
            McpServers =
            [
                new McpServerConfig
                {
                    Name = "local",
                    Command = "node",
                    Args = ["server.js"],
                    WorkingDirectory = "mcp",
                    Tags = ["trusted"]
                }
            ]
        };

        var server = Assert.Single(McpServerRegistry.EnabledServers(config, root));

        Assert.Equal(Path.GetFullPath(Path.Combine(root, "mcp")), server.WorkingDirectory);
        Assert.Equal("mcp", config.McpServers[0].WorkingDirectory);
    }

    [Fact]
    public void McpServerRegistry_BlocksSymlinkedWorkingDirectoryOutsideWorkspace()
    {
        var root = CreateTempDirectory();
        var outside = CreateTempDirectory();
        var linkPath = Path.Combine(root, "mcp-link");
        try
        {
            Directory.CreateSymbolicLink(linkPath, outside);
        }
        catch
        {
            return;
        }

        try
        {
            var config = new ProjectAgentConfig
            {
                McpServers =
                [
                    new McpServerConfig
                    {
                        Name = "local",
                        Command = "node",
                        Args = ["server.js"],
                        WorkingDirectory = "mcp-link",
                        Tags = ["trusted"]
                    }
                ]
            };

            Assert.Empty(McpServerRegistry.EnabledServers(config, root));
            Assert.Contains(McpServerRegistry.Validate(config, root), warning => warning.Contains("working directory blocked", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try
            {
                Directory.Delete(linkPath);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void McpToolName_BuildsSafeAgentQToolName()
    {
        var name = McpToolName.Build("Unity Server", "scene/read-object");

        Assert.Equal("mcp_unity_server_scene_read_object", name);
    }

    [Fact]
    public async Task McpBridgeTool_CallsClientWithOriginalToolName()
    {
        var server = new McpServerConfig
        {
            Name = "unity",
            Command = "node"
        };
        using var schema = JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"}}}""");
        var tool = new McpToolInfo
        {
            Name = "scene/read-object",
            Description = "Read a scene object.",
            InputSchema = schema.RootElement.Clone()
        };
        var client = new FakeMcpClient(JsonSerializer.SerializeToElement(new
        {
            content = new[] { new { type = "text", text = "ok" } }
        }));
        var bridge = new McpBridgeTool("mcp_unity_scene_read_object", server, tool, client);

        var result = await bridge.ExecuteAsync(
            new Dictionary<string, object?> { ["path"] = "Assets/Scene.unity" },
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("scene/read-object", client.LastToolName);
        Assert.Equal("Assets/Scene.unity", client.LastArguments.GetProperty("path").GetString());
        Assert.Contains("ok", result.Content);
        Assert.True(bridge.RequiresPermission);
    }

    [Fact]
    public async Task DesktopMcpToolRegistrar_RegistersEnabledServerTools()
    {
        var root = CreateTempDirectory();
        await new ProjectAgentConfigService().SaveAsync(
            root,
            new ProjectAgentConfig
            {
                McpServers =
                [
                    new McpServerConfig
                    {
                        Name = "unity",
                        Command = "node",
                        Args = ["server.js"],
                        Tags = ["trusted"]
                    }
                ]
            },
            CancellationToken.None);
        var registry = new ToolRegistry();
        var registrar = new DesktopMcpToolRegistrar(new FakeMcpClient(JsonSerializer.SerializeToElement(new
        {
            content = new[] { new { type = "text", text = "ok" } }
        })));

        await registrar.RegisterAsync(registry, root, CancellationToken.None);

        var registered = Assert.IsType<McpBridgeTool>(registry.Get("mcp_unity_scene_read_object"));
        Assert.Equal("mcp_unity_scene_read_object", registered.Name);
    }

    [Fact]
    public async Task DesktopMcpToolRegistrar_SkipsFailedDiscoveryWithoutBlockingRegistry()
    {
        var root = CreateTempDirectory();
        await new ProjectAgentConfigService().SaveAsync(
            root,
            new ProjectAgentConfig
            {
                McpServers =
                [
                    new McpServerConfig
                    {
                        Name = "unity",
                        Command = "node",
                        Args = ["server.js"],
                        Tags = ["trusted"]
                    }
                ]
            },
            CancellationToken.None);
        var registry = new ToolRegistry();
        var registrar = new DesktopMcpToolRegistrar(new ThrowingMcpClient());

        await registrar.RegisterAsync(registry, root, CancellationToken.None);

        Assert.Empty(registry.All);
    }

    [Fact]
    public async Task StdioMcpClient_ReusesInitializedSessionForToolCalls()
    {
        var powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(powershell))
        {
            return;
        }

        var root = CreateTempDirectory();
        var scriptPath = Path.Combine(root, "mcp-server.ps1");
        await File.WriteAllTextAsync(scriptPath, """
$pidValue = $PID
$callCount = 0
while (($line = [Console]::In.ReadLine()) -ne $null) {
    if ([string]::IsNullOrWhiteSpace($line)) {
        continue
    }

    $request = $line | ConvertFrom-Json
    if ($null -eq $request.id) {
        continue
    }

    if ($request.method -eq "initialize") {
        $response = @{
            jsonrpc = "2.0"
            id = $request.id
            result = @{
                protocolVersion = "2024-11-05"
                capabilities = @{}
                serverInfo = @{
                    name = "test-mcp"
                    version = "1.0"
                }
            }
        }
    }
    elseif ($request.method -eq "tools/list") {
        $response = @{
            jsonrpc = "2.0"
            id = $request.id
            result = @{
                tools = @(
                    @{
                        name = "echo"
                        description = "Echo with session state."
                        inputSchema = @{
                            type = "object"
                            additionalProperties = $true
                        }
                    }
                )
            }
        }
    }
    elseif ($request.method -eq "tools/call") {
        $callCount += 1
        $response = @{
            jsonrpc = "2.0"
            id = $request.id
            result = @{
                content = @(
                    @{
                        type = "text"
                        text = "pid=$pidValue;count=$callCount;tool=$($request.params.name)"
                    }
                )
            }
        }
    }
    else {
        $response = @{
            jsonrpc = "2.0"
            id = $request.id
            error = @{
                code = -32601
                message = "unknown method"
            }
        }
    }

    [Console]::Out.WriteLine(($response | ConvertTo-Json -Depth 20 -Compress))
    [Console]::Out.Flush()
}
""");

        var server = new McpServerConfig
        {
            Name = "stateful",
            Command = powershell,
            Args = ["-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath],
            WorkingDirectory = root
        };

        using var client = new StdioMcpClient();

        var tools = await client.ListToolsAsync(server, CancellationToken.None);
        var first = await client.CallToolAsync(server, "echo", JsonSerializer.SerializeToElement(new { value = 1 }), CancellationToken.None);
        var second = await client.CallToolAsync(server, "echo", JsonSerializer.SerializeToElement(new { value = 2 }), CancellationToken.None);

        Assert.Equal("echo", Assert.Single(tools).Name);
        Assert.Contains("count=1", ExtractMcpText(first), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("count=2", ExtractMcpText(second), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesktopEvidenceFormatter_ExplainsMcpToolCalls()
    {
        var evidence = DesktopEvidenceFormatter.DescribeToolEvidence(
            "mcp_unity_scene_read_object",
            new Dictionary<string, object?> { ["path"] = "Assets/Scene.unity" },
            "C:\\repo");

        Assert.Contains("MCP tool called", evidence);
        Assert.Contains("permission", evidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesktopProjectConfigBuilder_PreservesExistingMcpServers()
    {
        var existing =
            new McpServerConfig
            {
                Name = "unreal",
                Command = "python",
                Args = ["unreal_mcp.py"]
            };

        var config = DesktopProjectConfigBuilder.Build(
            AgentWorkMode.Coding,
            ["cmd /c test.cmd"],
            ["hint"],
            [existing]);

        Assert.Equal("unreal", Assert.Single(config.McpServers).Name);
        Assert.Contains("MCP servers", DesktopProjectConfigBuilder.BuildDisplay(config));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentq-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ExtractMcpText(JsonElement result)
    {
        return result.GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;
    }

    private sealed class FakeMcpClient(JsonElement callResult) : IMcpClient
    {
        public string LastToolName { get; private set; } = string.Empty;

        public JsonElement LastArguments { get; private set; }

        public Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(McpServerConfig server, CancellationToken ct = default)
        {
            IReadOnlyList<McpToolInfo> tools =
            [
                new McpToolInfo
                {
                    Name = "scene/read-object",
                    Description = "Read a scene object.",
                    InputSchema = JsonSerializer.SerializeToElement(new { type = "object" })
                }
            ];
            return Task.FromResult(tools);
        }

        public Task<JsonElement> CallToolAsync(McpServerConfig server, string toolName, JsonElement arguments, CancellationToken ct = default)
        {
            LastToolName = toolName;
            LastArguments = arguments.Clone();
            return Task.FromResult(callResult.Clone());
        }
    }

    private sealed class ThrowingMcpClient : IMcpClient
    {
        public Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(McpServerConfig server, CancellationToken ct = default)
        {
            throw new InvalidOperationException("discovery failed");
        }

        public Task<JsonElement> CallToolAsync(McpServerConfig server, string toolName, JsonElement arguments, CancellationToken ct = default)
        {
            throw new InvalidOperationException("call failed");
        }
    }
}
