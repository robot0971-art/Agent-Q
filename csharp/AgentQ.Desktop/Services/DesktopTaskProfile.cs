namespace AgentQ.Desktop.Services;

public sealed class DesktopTaskProfile
{
    public DesktopTaskKind Kind { get; init; }

    public string Label { get; init; } = "general";

    public string SystemHint { get; init; } = string.Empty;

    public string ContextHint { get; init; } = string.Empty;
}
