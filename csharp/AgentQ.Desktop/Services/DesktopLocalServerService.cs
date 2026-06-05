using System.Diagnostics;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using AgentQ.Tools;

namespace AgentQ.Desktop.Services;

public sealed class DesktopLocalServerService
{
    private static readonly string[] PreferredScripts = ["dev", "start", "preview"];
    private static readonly ConcurrentDictionary<string, LocalServerSession> Sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly IHttpClientFactory _httpClientFactory;

    public DesktopLocalServerService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<LocalServerStartResult> StartAsync(
        string workspaceRoot,
        IPermissionEnforcer permissionEnforcer,
        DesktopToolCallbacks? callbacks,
        CancellationToken ct)
    {
        var workspaceKey = NormalizeWorkspaceRoot(workspaceRoot);
        if (await GetActiveSessionAsync(workspaceRoot, ct) is { } existing)
        {
            callbacks?.OnRunStep?.Invoke(
                AgentRunState.Done,
                "Local server reused",
                existing.Url);
            return new LocalServerStartResult
            {
                Succeeded = true,
                Url = existing.Url,
                Command = existing.Command,
                ProcessId = existing.ProcessId,
                ReusedExisting = true,
                Message = $"Local server is already running: {existing.Url}"
            };
        }

        var plan = ResolveStartPlan(workspaceRoot);
        if (!plan.CanStart)
        {
            return LocalServerStartResult.Failed(plan.Message);
        }

        var inputJson = JsonSerializer.Serialize(new
        {
            command = plan.DisplayCommand,
            workspaceRoot = Path.GetFullPath(workspaceRoot),
            url = plan.Url
        }, new JsonSerializerOptions { WriteIndented = true });
        var approved = await permissionEnforcer.RequestPermissionAsync(
            "run_local_server",
            $"Start local development server: {plan.DisplayCommand}",
            inputJson);
        if (!approved)
        {
            return LocalServerStartResult.Failed("Local server start was not approved.");
        }

        callbacks?.OnRunStep?.Invoke(
            AgentRunState.RunningTool,
            "Starting local server",
            $"{plan.DisplayCommand} -> {plan.Url}");

        var logDirectory = Path.Combine(Path.GetFullPath(workspaceRoot), ".agentq", "local-server");
        Directory.CreateDirectory(logDirectory);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        var stdoutPath = Path.Combine(logDirectory, $"{stamp}-stdout.log");
        var stderrPath = Path.Combine(logDirectory, $"{stamp}-stderr.log");

        var process = Process.Start(CreateStartInfo(plan, stdoutPath, stderrPath));
        if (process == null)
        {
            return LocalServerStartResult.Failed("Failed to start local server process.");
        }

        var reachable = await WaitForReachableAsync(plan.Url, process, ct);
        if (reachable)
        {
            callbacks?.OnRunStep?.Invoke(
                AgentRunState.Done,
                "Local server running",
                plan.Url);
            var session = new LocalServerSession(
                WorkspaceRoot: workspaceKey,
                Url: plan.Url,
                Command: plan.DisplayCommand,
                ProcessId: process.Id,
                StartedAtUtc: DateTimeOffset.UtcNow);
            Sessions[workspaceKey] = session;
            await SaveSessionAsync(session, ct);
            return new LocalServerStartResult
            {
                Succeeded = true,
                Url = plan.Url,
                Command = plan.DisplayCommand,
                ProcessId = process.Id,
                Message = $"Local server is running: {plan.Url}"
            };
        }

        var error = await ReadShortErrorAsync(stderrPath, stdoutPath, ct);
        if (!process.HasExited)
        {
            TryKill(process);
        }

        return LocalServerStartResult.Failed(
            string.IsNullOrWhiteSpace(error)
                ? $"Local server did not respond at {plan.Url}."
                : $"Local server did not respond at {plan.Url}. {error}");
    }

    public async Task<LocalServerStopResult> StopAsync(
        string workspaceRoot,
        IPermissionEnforcer permissionEnforcer,
        DesktopToolCallbacks? callbacks,
        CancellationToken ct)
    {
        var workspaceKey = NormalizeWorkspaceRoot(workspaceRoot);
        var session = await GetActiveSessionAsync(workspaceRoot, ct);
        if (session == null)
        {
            DeleteSessionFile(workspaceKey);
            return new LocalServerStopResult
            {
                Succeeded = true,
                Message = "No active local server session was found for this workspace."
            };
        }

        var inputJson = JsonSerializer.Serialize(new
        {
            workspaceRoot = workspaceKey,
            url = session.Url,
            processId = session.ProcessId
        }, new JsonSerializerOptions { WriteIndented = true });
        var approved = await permissionEnforcer.RequestPermissionAsync(
            "stop_local_server",
            $"Stop local development server: {session.Url}",
            inputJson);
        if (!approved)
        {
            return LocalServerStopResult.Failed("Local server stop was not approved.");
        }

        callbacks?.OnRunStep?.Invoke(
            AgentRunState.RunningTool,
            "Stopping local server",
            session.Url);

        Sessions.TryRemove(workspaceKey, out _);
        DeleteSessionFile(workspaceKey);
        if (IsProcessAlive(session.ProcessId))
        {
            try
            {
                using var process = Process.GetProcessById(session.ProcessId);
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(ct);
            }
            catch
            {
                return LocalServerStopResult.Failed($"Failed to stop local server process {session.ProcessId}.");
            }
        }

        callbacks?.OnRunStep?.Invoke(
            AgentRunState.Done,
            "Local server stopped",
            session.Url);
        return new LocalServerStopResult
        {
            Succeeded = true,
            Url = session.Url,
            ProcessId = session.ProcessId,
            Message = $"Local server stopped: {session.Url}"
        };
    }

    public async Task<LocalServerSession?> GetActiveSessionAsync(string workspaceRoot, CancellationToken ct)
    {
        var workspaceKey = NormalizeWorkspaceRoot(workspaceRoot);
        if (Sessions.TryGetValue(workspaceKey, out var inMemory) &&
            IsProcessAlive(inMemory.ProcessId) &&
            await IsReachableAsync(inMemory.Url, ct))
        {
            return inMemory;
        }

        Sessions.TryRemove(workspaceKey, out _);
        var persisted = await LoadSessionAsync(workspaceKey, ct);
        if (persisted != null &&
            ProjectScaffoldPlanRegistry.MatchesWorkspace(persisted.WorkspaceRoot, workspaceKey) &&
            IsProcessAlive(persisted.ProcessId) &&
            await IsReachableAsync(persisted.Url, ct))
        {
            Sessions[workspaceKey] = persisted;
            return persisted;
        }

        DeleteSessionFile(workspaceKey);
        return null;
    }

    public LocalServerStartPlan ResolveStartPlan(string workspaceRoot)
    {
        var root = NormalizeWorkspaceRoot(workspaceRoot);
        var packageJsonPath = Path.Combine(root, "package.json");
        if (!File.Exists(packageJsonPath))
        {
            return LocalServerStartPlan.Failed("No package.json was found in the selected workspace.");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
        if (!document.RootElement.TryGetProperty("scripts", out var scripts) ||
            scripts.ValueKind != JsonValueKind.Object)
        {
            return LocalServerStartPlan.Failed("package.json does not define scripts.");
        }

        string? selectedScript = null;
        foreach (var script in PreferredScripts)
        {
            if (scripts.TryGetProperty(script, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString()))
            {
                selectedScript = script;
                break;
            }
        }

        if (selectedScript == null)
        {
            return LocalServerStartPlan.Failed("No dev, start, or preview script was found in package.json.");
        }

        var port = FindFreePort();
        return new LocalServerStartPlan
        {
            CanStart = true,
            WorkspaceRoot = root,
            ScriptName = selectedScript,
            Port = port,
            Url = $"http://127.0.0.1:{port}/",
            DisplayCommand = $"npm run {selectedScript} -- --host 127.0.0.1 --port {port}"
        };
    }

    private static ProcessStartInfo CreateStartInfo(LocalServerStartPlan plan, string stdoutPath, string stderrPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "npm.cmd" : "npm",
            WorkingDirectory = plan.WorkspaceRoot,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add(plan.ScriptName);
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(plan.Port.ToString());
        startInfo.Environment["PORT"] = plan.Port.ToString();
        startInfo.Environment["HOST"] = "127.0.0.1";

        return RedirectToFiles(startInfo, stdoutPath, stderrPath);
    }

    private static ProcessStartInfo RedirectToFiles(ProcessStartInfo startInfo, string stdoutPath, string stderrPath)
    {
        var originalFileName = startInfo.FileName;
        var originalArguments = startInfo.ArgumentList.ToArray();
        var wrapper = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/sh",
            WorkingDirectory = startInfo.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (OperatingSystem.IsWindows())
        {
            wrapper.ArgumentList.Add("-NoProfile");
            wrapper.ArgumentList.Add("-ExecutionPolicy");
            wrapper.ArgumentList.Add("Bypass");
            wrapper.ArgumentList.Add("-Command");
            var args = string.Join(" ", originalArguments.Select(QuotePowerShellArg));
            wrapper.ArgumentList.Add($"& {QuotePowerShellArg(originalFileName)} {args} > {QuotePowerShellArg(stdoutPath)} 2> {QuotePowerShellArg(stderrPath)}");
        }
        else
        {
            wrapper.ArgumentList.Add("-c");
            var args = string.Join(" ", originalArguments.Select(QuoteShellArg));
            wrapper.ArgumentList.Add($"{QuoteShellArg(originalFileName)} {args} > {QuoteShellArg(stdoutPath)} 2> {QuoteShellArg(stderrPath)}");
        }

        wrapper.Environment["PORT"] = startInfo.Environment["PORT"];
        wrapper.Environment["HOST"] = startInfo.Environment["HOST"];
        return wrapper;
    }

    private async Task<bool> WaitForReachableAsync(string url, Process process, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("local-server");
        client.Timeout = TimeSpan.FromSeconds(2);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            if (process.HasExited)
            {
                return false;
            }

            try
            {
                using var response = await client.GetAsync(url, ct);
                if ((int)response.StatusCode < 500)
                {
                    return true;
                }
            }
            catch
            {
            }

            await Task.Delay(500, ct);
        }

        return false;
    }

    private async Task<bool> IsReachableAsync(string url, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("local-server");
            client.Timeout = TimeSpan.FromSeconds(2);
            using var response = await client.GetAsync(url, ct);
            return (int)response.StatusCode < 500;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> ReadShortErrorAsync(string stderrPath, string stdoutPath, CancellationToken ct)
    {
        foreach (var path in new[] { stderrPath, stdoutPath })
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var text = await File.ReadAllTextAsync(path, ct);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return DesktopPromptBuilder.Truncate(text.ReplaceLineEndings(" "), 500);
            }
        }

        return string.Empty;
    }

    private static int FindFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private static bool IsProcessAlive(int processId)
    {
        if (processId <= 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeWorkspaceRoot(string workspaceRoot) =>
        Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string GetSessionFilePath(string workspaceRoot) =>
        Path.Combine(workspaceRoot, ".agentq", "local-server", "session.json");

    private static async Task SaveSessionAsync(LocalServerSession session, CancellationToken ct)
    {
        var path = GetSessionFilePath(session.WorkspaceRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true }),
            ct);
    }

    private static async Task<LocalServerSession?> LoadSessionAsync(string workspaceRoot, CancellationToken ct)
    {
        var path = GetSessionFilePath(workspaceRoot);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<LocalServerSession>(stream, cancellationToken: ct);
        }
        catch
        {
            return null;
        }
    }

    private static void DeleteSessionFile(string workspaceRoot)
    {
        var path = GetSessionFilePath(workspaceRoot);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static string QuotePowerShellArg(string value) => "'" + value.Replace("'", "''") + "'";

    private static string QuoteShellArg(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";
}

public sealed class LocalServerStartPlan
{
    public bool CanStart { get; init; }

    public string WorkspaceRoot { get; init; } = string.Empty;

    public string ScriptName { get; init; } = string.Empty;

    public int Port { get; init; }

    public string Url { get; init; } = string.Empty;

    public string DisplayCommand { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public static LocalServerStartPlan Failed(string message) => new()
    {
        CanStart = false,
        Message = message
    };
}

public sealed class LocalServerStartResult
{
    public bool Succeeded { get; init; }

    public string Url { get; init; } = string.Empty;

    public string Command { get; init; } = string.Empty;

    public int ProcessId { get; init; }

    public bool ReusedExisting { get; init; }

    public string Message { get; init; } = string.Empty;

    public static LocalServerStartResult Failed(string message) => new()
    {
        Succeeded = false,
        Message = message
    };
}

public sealed class LocalServerStopResult
{
    public bool Succeeded { get; init; }

    public string Url { get; init; } = string.Empty;

    public int ProcessId { get; init; }

    public string Message { get; init; } = string.Empty;

    public static LocalServerStopResult Failed(string message) => new()
    {
        Succeeded = false,
        Message = message
    };
}

public sealed record LocalServerSession(
    string WorkspaceRoot,
    string Url,
    string Command,
    int ProcessId,
    DateTimeOffset StartedAtUtc);
