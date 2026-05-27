using System.Diagnostics;
using System.IO;
using System.Collections.Concurrent;
using System.Text.Json;

namespace AgentQ.Desktop.Services;

public sealed class StdioMcpClient : IMcpClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ConcurrentDictionary<string, Lazy<Task<StdioMcpSession>>> _sessions = new();

    public async Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(McpServerConfig server, CancellationToken ct = default)
    {
        var session = await GetSessionAsync(server, ct);
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
        var session = await GetSessionAsync(server, ct);
        return await session.SendRequestAsync("tools/call", new
        {
            name = toolName,
            arguments
        }, ct);
    }

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
        {
            if (!session.IsValueCreated || !session.Value.IsCompletedSuccessfully)
            {
                continue;
            }

            session.Value.Result.Dispose();
        }

        _sessions.Clear();
    }

    private async Task<StdioMcpSession> GetSessionAsync(McpServerConfig server, CancellationToken ct)
    {
        var key = BuildSessionKey(server);
        var lazySession = _sessions.GetOrAdd(
            key,
            _ => new Lazy<Task<StdioMcpSession>>(() => StartInitializedSessionAsync(server, ct)));

        try
        {
            return await lazySession.Value;
        }
        catch
        {
            _sessions.TryRemove(key, out _);
            throw;
        }
    }

    private static async Task<StdioMcpSession> StartInitializedSessionAsync(McpServerConfig server, CancellationToken ct)
    {
        var session = await StdioMcpSession.StartAsync(server, ct);
        try
        {
            await session.InitializeAsync(ct);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private static string BuildSessionKey(McpServerConfig server)
    {
        return string.Join(
            '\u001f',
            server.Name.Trim(),
            server.Transport.Trim(),
            server.Command.Trim(),
            string.Join('\u001e', server.Args),
            server.WorkingDirectory.Trim());
    }

    private sealed class StdioMcpSession : IDisposable
    {
        private readonly Process _process;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pendingRequests = new();
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly CancellationTokenSource _sessionCts = new();
        private readonly Task _readerTask;
        private int _nextId = 1;

        private StdioMcpSession(Process process)
        {
            _process = process;
            _process.EnableRaisingEvents = true;
            _process.Exited += (_, _) => FailPendingRequests(new InvalidOperationException("MCP server process exited."));
            _readerTask = Task.Run(ReadLoopAsync);
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

            _ = Task.Run(async () =>
            {
                try
                {
                    await process.StandardError.ReadToEndAsync(ct);
                }
                catch
                {
                    // Best-effort drain.
                }
            }, CancellationToken.None);
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
            var id = Interlocked.Increment(ref _nextId);
            var payload = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id,
                method,
                @params = parameters
            });

            var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pendingRequests.TryAdd(id, completion))
            {
                throw new InvalidOperationException($"Duplicate MCP request id: {id}");
            }

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token, _sessionCts.Token);
            using var cancellation = linkedCts.Token.Register(() =>
            {
                if (_pendingRequests.TryRemove(id, out var pending))
                {
                    Exception exception = timeoutCts.IsCancellationRequested
                        ? new TimeoutException($"MCP {method} did not respond within 10 seconds.")
                        : new TaskCanceledException($"MCP {method} was cancelled.");
                    pending.TrySetException(exception);
                }
            });

            await WriteLineAsync(payload, ct);
            return await completion.Task;
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

        private async Task ReadLoopAsync()
        {
            try
            {
                while (!_sessionCts.IsCancellationRequested)
                {
                    var line = await _process.StandardOutput.ReadLineAsync(_sessionCts.Token);
                    if (line is null)
                    {
                        throw new EndOfStreamException("MCP server closed stdout.");
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    DispatchResponse(line);
                }
            }
            catch (OperationCanceledException) when (_sessionCts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                FailPendingRequests(ex);
            }
        }

        private void DispatchResponse(string line)
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var responseId) ||
                responseId.ValueKind != JsonValueKind.Number ||
                !_pendingRequests.TryRemove(responseId.GetInt32(), out var pending))
            {
                return;
            }

            if (root.TryGetProperty("error", out var error))
            {
                pending.TrySetException(new InvalidOperationException($"MCP request failed: {error}"));
                return;
            }

            if (!root.TryGetProperty("result", out var result))
            {
                pending.TrySetResult(JsonSerializer.SerializeToElement(new { }));
                return;
            }

            pending.TrySetResult(result.Clone());
        }

        private void FailPendingRequests(Exception exception)
        {
            foreach (var pair in _pendingRequests.ToArray())
            {
                if (_pendingRequests.TryRemove(pair.Key, out var pending))
                {
                    pending.TrySetException(exception);
                }
            }
        }

        private async Task WriteLineAsync(string payload, CancellationToken ct)
        {
            await _writeLock.WaitAsync(ct);
            try
            {
                await _process.StandardInput.WriteLineAsync(payload.AsMemory(), ct);
                await _process.StandardInput.FlushAsync(ct);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public void Dispose()
        {
            try
            {
                _sessionCts.Cancel();
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
                _sessionCts.Dispose();
                _writeLock.Dispose();
                _process.Dispose();
            }
        }
    }
}
