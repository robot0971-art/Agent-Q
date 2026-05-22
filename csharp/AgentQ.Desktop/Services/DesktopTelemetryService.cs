using System.IO;
using System.Text.Json;

namespace AgentQ.Desktop.Services;

public sealed class DesktopTelemetryService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task RecordAsync(DesktopTelemetryEvent telemetryEvent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(telemetryEvent.WorkspaceRoot))
        {
            return;
        }

        var root = Path.GetFullPath(telemetryEvent.WorkspaceRoot);
        var directory = Path.Combine(root, ".agentq", "telemetry");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "events.jsonl");
        var json = JsonSerializer.Serialize(telemetryEvent, Options);

        await _writeLock.WaitAsync(ct);
        try
        {
            await File.AppendAllTextAsync(path, json + Environment.NewLine, ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public static string GetTelemetryPath(string workspaceRoot)
    {
        return Path.Combine(Path.GetFullPath(workspaceRoot), ".agentq", "telemetry", "events.jsonl");
    }
}
