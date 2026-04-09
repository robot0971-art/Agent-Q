using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AgentQ.MockService;
using Xunit;

namespace AgentQ.Tests;

public sealed class AutomationCliIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task CliJsonMode_ReturnsConfigurationErrorEnvelope()
    {
        using var home = new TemporaryDirectory();

        var result = await RunCliAsync(
            ["--prompt", "hello", "--json"],
            environment:
            [
                new("USERPROFILE", home.Path),
                new("AGENTQ_MODEL", ""),
                new("AGENTQ_API_KEY", "")
            ]);

        Assert.Equal(2, result.ExitCode);
        using var json = JsonDocument.Parse(result.StdOut);
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("configuration_error", json.RootElement.GetProperty("terminationReason").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CliJsonMode_ReturnsInvalidArgumentsEnvelope()
    {
        using var home = new TemporaryDirectory();

        var result = await RunCliAsync(
            ["--prompt", "hello", "--stdin", "--json"],
            environment:
            [
                new("USERPROFILE", home.Path),
                new("AGENTQ_MODEL", "demo-model"),
                new("AGENTQ_API_KEY", "demo-key")
            ]);

        Assert.Equal(3, result.ExitCode);
        using var json = JsonDocument.Parse(result.StdOut);
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("invalid_arguments", json.RootElement.GetProperty("terminationReason").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CliJsonMode_CompletesSuccessfulAutomationRoundtrip()
    {
        await using var fixture = await MockServiceFixture.StartAsync();
        using var home = new TemporaryDirectory();

        var result = await RunCliAsync(
            ["--prompt", "PARITY_SCENARIO:plugin_tool_roundtrip", "--allow-tool", "plugin_echo", "--json"],
            environment:
            [
                new("USERPROFILE", home.Path),
                new("AGENTQ_PROVIDER", "anthropic"),
                new("AGENTQ_MODEL", "demo-model"),
                new("AGENTQ_API_KEY", "demo-key"),
                new("AGENTQ_BASE_URL", fixture.BaseUrl)
            ]);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.StdOut);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("completed", json.RootElement.GetProperty("terminationReason").GetString());
        Assert.Equal("plugin_echo", json.RootElement.GetProperty("allowedTools")[0].GetString());
        Assert.Equal("plugin_echo", json.RootElement.GetProperty("executedTools")[0].GetString());
        Assert.Equal("plugin_echo", json.RootElement.GetProperty("toolOutputs")[0].GetProperty("toolName").GetString());
        Assert.True(json.RootElement.GetProperty("toolOutputs")[0].GetProperty("isJson").GetBoolean());
        Assert.Equal("plugin tool completed: hello from plugin parity", json.RootElement.GetProperty("finalText").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CliJsonMode_ReportsPermissionDeniedWhenToolIsNotAllowed()
    {
        await using var fixture = await MockServiceFixture.StartAsync();
        using var home = new TemporaryDirectory();

        var result = await RunCliAsync(
            ["--prompt", "PARITY_SCENARIO:bash_permission_prompt_denied", "--json"],
            environment:
            [
                new("USERPROFILE", home.Path),
                new("AGENTQ_PROVIDER", "anthropic"),
                new("AGENTQ_MODEL", "demo-model"),
                new("AGENTQ_API_KEY", "demo-key"),
                new("AGENTQ_BASE_URL", fixture.BaseUrl)
            ]);

        Assert.Equal(4, result.ExitCode);
        using var json = JsonDocument.Parse(result.StdOut);
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("permission_denied", json.RootElement.GetProperty("terminationReason").GetString());
        Assert.Equal("bash", json.RootElement.GetProperty("deniedTools")[0].GetString());
    }

    private static async Task<CliProcessResult> RunCliAsync(
        IReadOnlyList<string> arguments,
        IEnumerable<KeyValuePair<string, string>> environment,
        string? standardInput = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(AppContext.BaseDirectory, "AgentQ.Cli.exe"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput != null,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var pair in environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start AgentQ.Cli.exe.");

        if (standardInput != null)
        {
            await process.StandardInput.WriteAsync(standardInput);
            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new CliProcessResult(
            process.ExitCode,
            (await stdoutTask).Trim(),
            (await stderrTask).Trim());
    }

    private sealed record CliProcessResult(int ExitCode, string StdOut, string StdErr);

    private sealed class MockServiceFixture : IAsyncDisposable
    {
        public required MockAnthropicService Service { get; init; }

        public required string BaseUrl { get; init; }

        public static async Task<MockServiceFixture> StartAsync()
        {
            var prefix = BuildListenerPrefix();
            var service = new MockAnthropicService();
            await service.StartAsync(prefix);

            return new MockServiceFixture
            {
                Service = service,
                BaseUrl = service.BaseUrl
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Service.StopAsync();
        }

        private static string BuildListenerPrefix()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            return $"http://127.0.0.1:{port}/";
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "agentq-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
