namespace AgentQ.Desktop.Services;

public sealed class DesktopTelemetryEvent
{
    public DateTime Timestamp { get; set; } = DateTime.Now;

    public string EventType { get; set; } = string.Empty;

    public string WorkspaceRoot { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string ToolName { get; set; } = string.Empty;

    public bool Succeeded { get; set; }

    public bool IsError { get; set; }

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public bool IsEstimate { get; set; }

    public int DurationMs { get; set; }

    public string Detail { get; set; } = string.Empty;
}
