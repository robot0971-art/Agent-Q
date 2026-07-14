using AgentQ.Runtime.Completion;
using AgentQ.Runtime.Intent;

namespace AgentQ.Desktop.Services;

public interface IDesktopTaskContractCompletionAdapter
{
    bool ShouldRetry(TaskContract contract, string assistantText, IReadOnlyList<string> executedCommands, AgentWorkMode workMode, IReadOnlyList<ToolReplayEntry> replayEntries);

    bool ShouldReject(TaskContract contract, string assistantText, IReadOnlyList<string> executedCommands, AgentWorkMode workMode, IReadOnlyList<ToolReplayEntry> replayEntries);
}

/// <summary>
/// Migration seam for legacy completion policy. Runtime supplies an additional evidence
/// floor; it can only request another attempt/reject, never make a legacy rejection pass.
/// </summary>
public sealed class DesktopTaskContractCompletionAdapter(ICompletionSafetyPolicy? runtimeSafetyPolicy = null) : IDesktopTaskContractCompletionAdapter
{
    private readonly ICompletionSafetyPolicy _runtimeSafetyPolicy = runtimeSafetyPolicy ?? new CompletionSafetyPolicy();

    public bool ShouldRetry(TaskContract contract, string assistantText, IReadOnlyList<string> executedCommands, AgentWorkMode workMode, IReadOnlyList<ToolReplayEntry> replayEntries) =>
        TaskContractCompletionChecker.ShouldRetry(contract, assistantText, executedCommands, workMode, replayEntries) ||
        _runtimeSafetyPolicy.RequiresRetryOrReject(ToRuntimeRequest(contract, executedCommands, workMode, replayEntries));

    public bool ShouldReject(TaskContract contract, string assistantText, IReadOnlyList<string> executedCommands, AgentWorkMode workMode, IReadOnlyList<ToolReplayEntry> replayEntries) =>
        TaskContractCompletionChecker.ShouldReject(contract, assistantText, executedCommands, workMode, replayEntries) ||
        _runtimeSafetyPolicy.RequiresRetryOrReject(ToRuntimeRequest(contract, executedCommands, workMode, replayEntries));

    private static CompletionSafetyRequest ToRuntimeRequest(
        TaskContract contract,
        IReadOnlyList<string> executedCommands,
        AgentWorkMode workMode,
        IReadOnlyList<ToolReplayEntry> replayEntries)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(executedCommands);
        ArgumentNullException.ThrowIfNull(replayEntries);

        var hasCommand = executedCommands.Count > 0 || HasSuccessfulTool(replayEntries, "bash");
        var hasMutation = HasSuccessfulTool(replayEntries, "delete_path", "create_directory", "write_file", "edit_file", "create_project_scaffold");
        var hasSearch = HasSuccessfulTool(replayEntries, "web_search", "fetch_url", "read_file", "grep_search", "hybrid_search", "semantic_search");
        return new CompletionSafetyRequest(
            contract.IsActionable,
            workMode == AgentWorkMode.Readonly,
            contract.Intent == TaskContractIntent.SearchAndSummarize ? AgentTurnIntent.Hybrid : AgentTurnIntent.Action,
            RequiresExecutionEvidence(contract.Intent),
            hasCommand,
            hasMutation,
            hasSearch);
    }

    private static bool RequiresExecutionEvidence(TaskContractIntent intent) => intent is
        TaskContractIntent.RunLocalServer or
        TaskContractIntent.DeletePath or
        TaskContractIntent.CreateDirectory or
        TaskContractIntent.CreateFile or
        TaskContractIntent.CreateProject or
        TaskContractIntent.ModifyCode or
        TaskContractIntent.RunVerification or
        TaskContractIntent.SearchAndSummarize;

    private static bool HasSuccessfulTool(IReadOnlyList<ToolReplayEntry> entries, params string[] names) =>
        entries.Any(entry => entry.IsError != true && names.Any(name => string.Equals(name, entry.ToolName, StringComparison.OrdinalIgnoreCase)));
}
