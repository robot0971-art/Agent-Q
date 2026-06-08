using System.IO;
using System.Text;
using System.Text.Json;

namespace AgentQ.Desktop.Services;

public sealed class DesktopDiagnosticsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private string _activeWorkspaceRoot = string.Empty;

    public void SetActiveWorkspace(string workspaceRoot)
    {
        _activeWorkspaceRoot = workspaceRoot;
    }

    public void Record(
        string eventType,
        string detail = "",
        string workspaceRoot = "",
        string provider = "",
        string model = "",
        Exception? exception = null)
    {
        var entry = DesktopDiagnosticEvent.Create(
            eventType,
            ResolveWorkspaceRoot(workspaceRoot),
            provider,
            model,
            detail,
            exception);

        WriteSync(entry);
    }

    public void RecordSync(
        string eventType,
        string detail = "",
        string workspaceRoot = "",
        string provider = "",
        string model = "",
        Exception? exception = null)
    {
        var entry = DesktopDiagnosticEvent.Create(
            eventType,
            ResolveWorkspaceRoot(workspaceRoot),
            provider,
            model,
            detail,
            exception);

        WriteSync(entry);
    }

    public static string GetWorkspaceDiagnosticsPath(string workspaceRoot)
    {
        return Path.Combine(Path.GetFullPath(workspaceRoot), ".agentq", "diagnostics", "events.jsonl");
    }

    public static string GetFallbackDiagnosticsPath()
    {
        var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = AppContext.BaseDirectory;
        }

        return Path.Combine(baseDirectory, "AgentQ", "diagnostics", "events.jsonl");
    }

    private string ResolveWorkspaceRoot(string workspaceRoot)
    {
        return string.IsNullOrWhiteSpace(workspaceRoot) ? _activeWorkspaceRoot : workspaceRoot;
    }

    private async Task WriteAsync(DesktopDiagnosticEvent entry, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            foreach (var path in GetTargetPaths(entry.WorkspaceRoot))
            {
                await AppendAsync(path, entry, ct);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static void WriteSync(DesktopDiagnosticEvent entry)
    {
        foreach (var path in GetTargetPaths(entry.WorkspaceRoot))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream, Encoding.UTF8);
                writer.WriteLine(JsonSerializer.Serialize(entry, Options));
            }
            catch
            {
                // Diagnostics must never become the app failure.
            }
        }
    }

    private static async Task AppendAsync(string path, DesktopDiagnosticEvent entry, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            await using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteLineAsync(JsonSerializer.Serialize(entry, Options).AsMemory(), ct);
        }
        catch
        {
            // Diagnostics must never become the app failure.
        }
    }

    private static IReadOnlyList<string> GetTargetPaths(string workspaceRoot)
    {
        var paths = new List<string> { GetFallbackDiagnosticsPath() };
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            paths.Add(GetWorkspaceDiagnosticsPath(workspaceRoot));
        }

        return paths;
    }

    private sealed class DesktopDiagnosticEvent
    {
        public DateTime Timestamp { get; init; } = DateTime.Now;

        public string EventType { get; init; } = string.Empty;

        public string WorkspaceRoot { get; init; } = string.Empty;

        public string Provider { get; init; } = string.Empty;

        public string Model { get; init; } = string.Empty;

        public string Detail { get; init; } = string.Empty;

        public string ExceptionType { get; init; } = string.Empty;

        public string ExceptionMessage { get; init; } = string.Empty;

        public string StackTrace { get; init; } = string.Empty;

        public static DesktopDiagnosticEvent Create(
            string eventType,
            string workspaceRoot,
            string provider,
            string model,
            string detail,
            Exception? exception)
        {
            return new DesktopDiagnosticEvent
            {
                EventType = eventType,
                WorkspaceRoot = workspaceRoot,
                Provider = provider,
                Model = model,
                Detail = SensitiveTextRedactor.Redact(DesktopPromptBuilder.Truncate(detail.ReplaceLineEndings(" "), 2000)),
                ExceptionType = exception?.GetType().FullName ?? string.Empty,
                ExceptionMessage = SensitiveTextRedactor.Redact(exception?.Message ?? string.Empty),
                StackTrace = SensitiveTextRedactor.Redact(DesktopPromptBuilder.Truncate(exception?.ToString() ?? string.Empty, 8000))
            };
        }
    }
}
