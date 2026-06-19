using System.Diagnostics;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using AgentQ.Api;
using AgentQ.Tools;

namespace AgentQ.Desktop.Services;

public sealed class DesktopLocalServerService
{
    private static readonly string[] PreferredScripts = ["dev", "start", "preview"];
    private static readonly string[] BunLockFiles = ["bun.lockb", "bun.lock"];
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
        }, AgentQJsonOptions.Indented);
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
        if (!WorkspacePathResolver.IsResolvedInsideWorkspace(workspaceKey, logDirectory))
        {
            return LocalServerStartResult.Failed(
                "Local server log path resolves outside the workspace.",
                command: plan.DisplayCommand,
                url: plan.Url);
        }

        Directory.CreateDirectory(logDirectory);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        var stdoutPath = Path.Combine(logDirectory, $"{stamp}-stdout.log");
        var stderrPath = Path.Combine(logDirectory, $"{stamp}-stderr.log");

        Process? process;
        try
        {
            process = Process.Start(CreateStartInfo(plan, stdoutPath, stderrPath));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ObjectDisposedException)
        {
            return LocalServerStartResult.Failed(
                $"Failed to start local server process: {ex.Message}",
                command: plan.DisplayCommand,
                url: plan.Url);
        }

        if (process == null)
        {
            return LocalServerStartResult.Failed(
                "Failed to start local server process.",
                command: plan.DisplayCommand,
                url: plan.Url);
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
                StartedAtUtc: DateTimeOffset.UtcNow,
                ProcessStartedAtUtc: TryGetProcessStartTimeUtc(process.Id));
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
                : $"Local server did not respond at {plan.Url}. {error}",
            command: plan.DisplayCommand,
            url: plan.Url,
            processId: process.Id);
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
        }, AgentQJsonOptions.Indented);
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
            ProcessMatchesSession(inMemory) &&
            await IsReachableAsync(inMemory.Url, ct))
        {
            return inMemory;
        }

        Sessions.TryRemove(workspaceKey, out _);
        var persisted = await LoadSessionAsync(workspaceKey, ct);
        if (persisted != null &&
            ProjectScaffoldPlanRegistry.MatchesWorkspace(persisted.WorkspaceRoot, workspaceKey) &&
            ProcessMatchesSession(persisted) &&
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

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return LocalServerStartPlan.Failed($"package.json could not be read or parsed: {ex.Message}");
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("scripts", out var scripts) ||
                scripts.ValueKind != JsonValueKind.Object)
            {
                return LocalServerStartPlan.Failed("package.json does not define scripts.");
            }

            string? selectedScript = null;
            string selectedScriptCommand = string.Empty;
            foreach (var script in PreferredScripts)
            {
                if (scripts.TryGetProperty(script, out var value) &&
                    value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    selectedScript = script;
                    selectedScriptCommand = value.GetString() ?? string.Empty;
                    break;
                }
            }

            if (selectedScript == null)
            {
                return LocalServerStartPlan.Failed("No dev, start, or preview script was found in package.json.");
            }

            var port = FindFreePort();
            var packageManager = ResolvePackageManager(root);
            var arguments = ResolveServerArguments(selectedScriptCommand, port);
            return new LocalServerStartPlan
            {
                CanStart = true,
                WorkspaceRoot = root,
                ScriptName = selectedScript,
                ScriptCommand = selectedScriptCommand,
                PackageManager = packageManager,
                ServerArguments = arguments,
                Port = port,
                Url = $"http://127.0.0.1:{port}/",
                DisplayCommand = BuildDisplayCommand(packageManager, selectedScript, arguments)
            };
        }
    }

    private static ProcessStartInfo CreateStartInfo(LocalServerStartPlan plan, string stdoutPath, string stderrPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = GetPackageManagerExecutable(plan.PackageManager),
            WorkingDirectory = plan.WorkspaceRoot,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add(plan.ScriptName);
        if (plan.ServerArguments.Count > 0)
        {
            startInfo.ArgumentList.Add("--");
            foreach (var argument in plan.ServerArguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        startInfo.Environment["PORT"] = plan.Port.ToString();
        startInfo.Environment["HOST"] = "127.0.0.1";

        return RedirectToFiles(startInfo, stdoutPath, stderrPath);
    }

    private static string ResolvePackageManager(string workspaceRoot)
    {
        if (File.Exists(Path.Combine(workspaceRoot, "pnpm-lock.yaml")))
        {
            return "pnpm";
        }

        if (File.Exists(Path.Combine(workspaceRoot, "yarn.lock")))
        {
            return "yarn";
        }

        if (BunLockFiles.Any(file => File.Exists(Path.Combine(workspaceRoot, file))))
        {
            return "bun";
        }

        return "npm";
    }

    private static IReadOnlyList<string> ResolveServerArguments(string scriptCommand, int port)
    {
        var normalized = scriptCommand.ToLowerInvariant();
        if (normalized.Contains("vite", StringComparison.Ordinal) ||
            normalized.Contains("astro", StringComparison.Ordinal) ||
            normalized.Contains("svelte-kit", StringComparison.Ordinal))
        {
            return ["--host", "127.0.0.1", "--port", port.ToString()];
        }

        if (normalized.Contains("next", StringComparison.Ordinal))
        {
            return ["-H", "127.0.0.1", "-p", port.ToString()];
        }

        return [];
    }

    private static string BuildDisplayCommand(
        string packageManager,
        string scriptName,
        IReadOnlyList<string> arguments)
    {
        var command = $"{packageManager} run {scriptName}";
        return arguments.Count == 0
            ? command
            : $"{command} -- {string.Join(' ', arguments)}";
    }

    private static string GetPackageManagerExecutable(string packageManager)
    {
        if (!OperatingSystem.IsWindows())
        {
            return packageManager;
        }

        return packageManager.Equals("yarn", StringComparison.OrdinalIgnoreCase)
            ? "yarn.cmd"
            : $"{packageManager}.cmd";
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

            var text = await TryReadAllTextSharedAsync(path, ct);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return DesktopPromptBuilder.Truncate(text.ReplaceLineEndings(" "), 500);
            }
        }

        return string.Empty;
    }

    private static async Task<string> TryReadAllTextSharedAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(ct);
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
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

    private static bool ProcessMatchesSession(LocalServerSession session)
    {
        if (!IsProcessAlive(session.ProcessId))
        {
            return false;
        }

        if (session.ProcessStartedAtUtc == null)
        {
            return true;
        }

        var currentStartTime = TryGetProcessStartTimeUtc(session.ProcessId);
        if (currentStartTime == null)
        {
            return false;
        }

        return Math.Abs((currentStartTime.Value - session.ProcessStartedAtUtc.Value).TotalSeconds) <= 2;
    }

    private static DateTimeOffset? TryGetProcessStartTimeUtc(int processId)
    {
        if (processId <= 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return process.StartTime.ToUniversalTime();
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeWorkspaceRoot(string workspaceRoot) =>
        Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string GetSessionFilePath(string workspaceRoot) =>
        Path.Combine(workspaceRoot, ".agentq", "local-server", "session.json");

    private static async Task SaveSessionAsync(LocalServerSession session, CancellationToken ct)
    {
        var path = GetSessionFilePath(session.WorkspaceRoot);
        if (!WorkspacePathResolver.IsResolvedInsideWorkspace(session.WorkspaceRoot, path))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(session, AgentQJsonOptions.Indented),
            ct);
    }

    private static async Task<LocalServerSession?> LoadSessionAsync(string workspaceRoot, CancellationToken ct)
    {
        var path = GetSessionFilePath(workspaceRoot);
        if (!WorkspacePathResolver.IsResolvedInsideWorkspace(workspaceRoot, path) ||
            !File.Exists(path))
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
            if (WorkspacePathResolver.IsResolvedInsideWorkspace(workspaceRoot, path) &&
                File.Exists(path))
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

    public string ScriptCommand { get; init; } = string.Empty;

    public string PackageManager { get; init; } = "npm";

    public IReadOnlyList<string> ServerArguments { get; init; } = [];

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

    public static LocalServerStartResult Failed(
        string message,
        string command = "",
        string url = "",
        int processId = 0) => new()
    {
        Succeeded = false,
        Url = url,
        Command = command,
        ProcessId = processId,
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
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? ProcessStartedAtUtc = null);
