using AgentQ.Core.Models;

namespace AgentQ.Desktop.Services;

public sealed class DesktopToolCallbacks
{
    public Action<AgentRunState, string, string?>? OnRunStep { get; init; }

    public Action<string>? OnToolExecution { get; init; }

    public Action<string, string>? OnToolOutput { get; init; }

    public Action<string, string>? OnToolError { get; init; }

    public Action<string>? OnPermissionDenied { get; init; }

    public Action<FileChangeRecord>? OnFileChanged { get; init; }

    public Action<AgentVerificationPlan>? OnVerificationPlan { get; init; }

    public Action<VerificationResultCard>? OnVerificationResult { get; init; }

    public Action<UsageStats>? OnUsage { get; init; }

    public Action<DesktopLocalServerState>? OnLocalServerChanged { get; init; }

    public Func<int, bool>? OnRequestExtendSteps { get; init; }
}

public sealed record DesktopLocalServerState(
    bool IsRunning,
    string Url,
    string Command,
    int ProcessId,
    bool ReusedExisting,
    string Message);
