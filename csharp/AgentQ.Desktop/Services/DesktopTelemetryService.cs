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
        if (!WorkspacePathResolver.IsResolvedInsideWorkspace(root, directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "events.jsonl");
        var json = JsonSerializer.Serialize(Sanitize(telemetryEvent), Options);

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

    private static DesktopTelemetryEvent Sanitize(DesktopTelemetryEvent telemetryEvent)
    {
        return new DesktopTelemetryEvent
        {
            Timestamp = telemetryEvent.Timestamp,
            EventType = telemetryEvent.EventType,
            WorkspaceRoot = telemetryEvent.WorkspaceRoot,
            Provider = telemetryEvent.Provider,
            Model = telemetryEvent.Model,
            ToolName = telemetryEvent.ToolName,
            Succeeded = telemetryEvent.Succeeded,
            IsError = telemetryEvent.IsError,
            InputTokens = telemetryEvent.InputTokens,
            OutputTokens = telemetryEvent.OutputTokens,
            IsEstimate = telemetryEvent.IsEstimate,
            DurationMs = telemetryEvent.DurationMs,
            Detail = SensitiveTextRedactor.Redact(telemetryEvent.Detail)
        };
    }
}
