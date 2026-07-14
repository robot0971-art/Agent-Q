using System.Diagnostics;
using AgentQ.Desktop.Services;
using AgentQ.Tools;
using Xunit;

namespace AgentQ.Tests;

[Collection("Environment variable tests")]
public sealed class DesktopProcessLifecycleTests
{
    [Fact]
    public async Task DesktopLocalServerService_CancellationTerminatesStartedProcessTree()
    {
        var root = Path.Combine(Path.GetTempPath(), "agentq-process-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pidPath = Path.Combine(root, "server.pid");
        await File.WriteAllTextAsync(
            Path.Combine(root, "package.json"),
            """{"scripts":{"dev":"node server.js"}}""");
        await File.WriteAllTextAsync(
            Path.Combine(root, "server.js"),
            """
            const fs = require('fs');
            fs.writeFileSync('server.pid', String(process.pid));
            setInterval(() => {}, 1000);
            """);

        using var cancellation = new CancellationTokenSource();
        var service = new DesktopLocalServerService(new TestHttpClientFactory());
        var startTask = service.StartAsync(
            root,
            new AlwaysAllowPermissionEnforcer(),
            callbacks: null,
            cancellation.Token);

        var processId = await WaitForProcessIdAsync(pidPath);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startTask);
        await WaitForExitAsync(processId);
    }

    private static async Task<int> WaitForProcessIdAsync(string pidPath)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(pidPath) && int.TryParse(await File.ReadAllTextAsync(pidPath), out var processId) && processId > 0)
            {
                return processId;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("The test local-server process did not publish its PID.");
    }

    private static async Task WaitForExitAsync(int processId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return;
                }
            }
            catch (ArgumentException)
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Test-owned local-server process {processId} remained alive after cancellation.");
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
