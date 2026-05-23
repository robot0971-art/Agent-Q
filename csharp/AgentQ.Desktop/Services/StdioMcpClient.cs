using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AgentQ.Desktop.Services;

public sealed class StdioMcpClient : IMcpClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(McpServerConfig server, CancellationToken ct = default)
    {
        using var session = await StdioMcpSession.StartAsync(server, ct);
        await session.InitializeAsync(ct);
        var response = await session.SendRequestAsync("tools/list", new { }, ct);
        if (!response.TryGetProperty("tools", out var tools) || tools.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<McpToolInfo>();
        foreach (var item in tools.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var description = item.TryGetProperty("description", out var descriptionElement)
                ? descriptionElement.GetString() ?? string.Empty
                : string.Empty;
            var schema = item.TryGetProperty("inputSchema", out var schemaElement)
                ? schemaElement.Clone()
                : JsonSerializer.SerializeToElement(new { type = "object", additionalProperties = true });

            result.Add(new McpToolInfo
            {
                Name = name,
                Description = description,
                InputSchema = schema
            });
        }

        return result;
    }

    public async Task<JsonElement> CallToolAsync(McpServerConfig server, string toolName, JsonElement arguments, CancellationToken ct = default)
    {
        using var session = await StdioMcpSession.StartAsync(server, ct);
        await session.InitializeAsync(ct);
        return await session.SendRequestAsync("tools/call", new
        {
            name = toolName,
            arguments
        }, ct);
    }

    private sealed class StdioMcpSession : IDisposable
    {
        private readonly Process _process;
        private int _nextId = 1;

        private StdioMcpSession(Process process)
        {
            _process = process;
        }

        public static async Task<StdioMcpSession> StartAsync(McpServerConfig server, CancellationToken ct)
        {
            if (!string.Equals(server.Transport, "stdio", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unsupported MCP transport: {server.Transport}");
            }

            if (string.IsNullOrWhiteSpace(server.Command))
            {
                throw new InvalidOperationException("MCP server command is required.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = server.Command,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var arg in server.Args.Where(arg => !string.IsNullOrWhiteSpace(arg)))
            {
                startInfo.ArgumentList.Add(arg);
            }

            if (!string.IsNullOrWhiteSpace(server.WorkingDirectory))
            {
                startInfo.WorkingDirectory = Path.GetFullPath(server.WorkingDirectory);
            }

            var process = Process.Start(startInfo) ??
                          throw new InvalidOperationException($"Unable to start MCP server {server.Name}.");

            _ = process.StandardError.ReadToEndAsync(ct);
            return await Task.FromResult(new StdioMcpSession(process));
        }

        public async Task InitializeAsync(CancellationToken ct)
        {
            await SendRequestAsync("initialize", new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new
                {
                    name = "AgentQ",
                    version = "0.1"
                }
            }, ct);

            await SendNotificationAsync("notifications/initialized", new { }, ct);
        }

        public async Task<JsonElement> SendRequestAsync(string method, object parameters, CancellationToken ct)
        {
            var id = _nextId++;
            var payload = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id,
                method,
                @params = parameters
            });

            await WriteLineAsync(payload, ct);

            while (!ct.IsCancellationRequested)
            {
                var line = await ReadLineWithTimeoutAsync(ct);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("id", out var responseId) ||
                    responseId.ValueKind != JsonValueKind.Number ||
                    responseId.GetInt32() != id)
                {
                    continue;
                }

                if (root.TryGetProperty("error", out var error))
                {
                    throw new InvalidOperationException($"MCP {method} failed: {error}");
                }

                if (!root.TryGetProperty("result", out var result))
                {
                    return JsonSerializer.SerializeToElement(new { });
                }

                return result.Clone();
            }

            throw new TaskCanceledException($"MCP {method} was cancelled.");
        }

        private async Task SendNotificationAsync(string method, object parameters, CancellationToken ct)
        {
            var payload = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method,
                @params = parameters
            });

            await WriteLineAsync(payload, ct);
        }

        private async Task WriteLineAsync(string payload, CancellationToken ct)
        {
            await _process.StandardInput.WriteLineAsync(payload.AsMemory(), ct);
            await _process.StandardInput.FlushAsync(ct);
        }

        private async Task<string?> ReadLineWithTimeoutAsync(CancellationToken ct)
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            try
            {
                return await _process.StandardOutput.ReadLineAsync(linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                throw new TimeoutException("MCP server did not respond within 10 seconds.");
            }
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
            finally
            {
                _process.Dispose();
            }
        }
    }
}
