namespace AgentQ.Desktop.Services;

public sealed class WorkspaceDependencyEdge
{
    public string FromPath { get; init; } = string.Empty;

    public string ToPath { get; init; } = string.Empty;

    public string Target { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public string Language { get; init; } = string.Empty;

    public int Line { get; init; }

    public bool IsExternal => string.IsNullOrWhiteSpace(ToPath);

    public string DisplayText
    {
        get
        {
            var target = string.IsNullOrWhiteSpace(ToPath) ? Target : ToPath;
            return Line > 0
                ? $"{FromPath}:{Line:0} -> {target} ({Kind})"
                : $"{FromPath} -> {target} ({Kind})";
        }
    }
}
