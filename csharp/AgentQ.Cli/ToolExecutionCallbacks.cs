namespace AgentQ.Cli;

internal sealed class ToolExecutionCallbacks
{
    public Action<string>? OnToolExecution { get; init; }

    public Action<string, string>? OnToolOutput { get; init; }

    public Action<string, string>? OnToolError { get; init; }

    public Action<string>? OnPermissionDenied { get; init; }
}
