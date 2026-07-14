using AgentQ.Runtime.Runs;
using AgentQ.Runtime.Journaling;
using AgentQ.Runtime.Contracts;
using System.IO;

namespace AgentQ.Desktop.Services;

/// <summary>
/// Projects the already-authoritative Desktop run steps onto the portable Runtime state
/// machine. This is observability-only during the migration: a projection failure must
/// never change Desktop execution, permission, or completion behaviour.
/// </summary>
public sealed class DesktopRuntimeRunLifecycle(
    IAgentRunCoordinator? coordinator = null,
    IAgentRunJournalStore? journalStore = null)
{
    private readonly IAgentRunCoordinator _coordinator = coordinator ?? new AgentRunCoordinator(new AgentRunStateMachine());
    private readonly IAgentRunJournalStore? _journalStore = journalStore;

    public DesktopRuntimeRunSession Start(string runId, string? workspaceRoot = null)
    {
        var session = _coordinator.Start(runId, "desktop-runtime-bridge-v1");
        return new DesktopRuntimeRunSession(session, _journalStore ?? CreateWorkspaceJournalStore(workspaceRoot));
    }

    private static IAgentRunJournalStore CreateWorkspaceJournalStore(string? workspaceRoot)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(workspaceRoot) && Directory.Exists(workspaceRoot))
            {
                var root = Path.GetFullPath(workspaceRoot);
                var directory = Path.Combine(root, ".agentq", "runs");
                if (WorkspacePathResolver.IsInsideWorkspace(root, directory) &&
                    WorkspacePathResolver.IsResolvedInsideWorkspace(root, directory))
                {
                    return new FileAgentRunJournalStore(directory);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Journal placement is observability-only during migration. Fall back to a
            // process-local location if a selected workspace cannot be used safely.
        }

        return new FileAgentRunJournalStore(Path.Combine(Path.GetTempPath(), "AgentQ", "run-journals"));
    }
}

public sealed class DesktopRuntimeRunSession
{
    private readonly AgentRunSession _session;
    private readonly IAgentRunJournalStore _journalStore;
    private readonly object _journalWriteGate = new();
    private Task _journalWrites = Task.CompletedTask;
    private RuntimeTaskContract? _contract;

    internal DesktopRuntimeRunSession(AgentRunSession session, IAgentRunJournalStore journalStore)
    {
        _session = session;
        _journalStore = journalStore;
        QueueJournalSnapshot();
    }

    public IReadOnlyList<AgentRunTransition> History => _session.History;

    public string RunId => _session.RunId;

    public RuntimeTaskContract? Contract => _contract;

    public void RecordContract(RuntimeTaskContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (!string.Equals(contract.ContractId, RunId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The runtime contract must be bound to this run.", nameof(contract));
        }

        _contract = contract;
        QueueJournalSnapshot();
    }

    public void RecordDesktopState(AgentRunState state)
    {
        try
        {
            var historyCount = _session.History.Count;
            switch (state)
            {
                case AgentRunState.GatheringContext:
                case AgentRunState.Planning:
                case AgentRunState.Generating:
                    EnsureUnderstanding();
                    if (state == AgentRunState.Planning)
                    {
                        AdvanceIfPossible(AgentRunStatus.Planning, "desktop-planning");
                    }
                    break;
                case AgentRunState.Clarifying:
                    EnsureUnderstanding();
                    AdvanceIfPossible(AgentRunStatus.AwaitingClarification, "desktop-clarifying");
                    break;
                case AgentRunState.WaitingForApproval:
                    EnsurePlanning();
                    AdvanceIfPossible(AgentRunStatus.AwaitingApproval, "desktop-awaiting-approval", ContractId);
                    break;
                case AgentRunState.RunningTool:
                case AgentRunState.RecordingChanges:
                    EnsureExecuting();
                    break;
                case AgentRunState.Verifying:
                    EnsureExecuting();
                    AdvanceIfPossible(AgentRunStatus.Verifying, "desktop-verifying", ContractId);
                    break;
                case AgentRunState.Done:
                    Complete();
                    break;
                case AgentRunState.Failed:
                    AdvanceIfPossible(AgentRunStatus.Failed, "desktop-failed", ContractId);
                    break;
                case AgentRunState.Cancelled:
                    AdvanceIfPossible(AgentRunStatus.Cancelled, "desktop-cancelled", ContractId);
                    break;
            }

            if (_session.History.Count != historyCount)
            {
                QueueJournalSnapshot();
            }
        }
        catch (InvalidOperationException)
        {
            // The Desktop timeline remains the source of truth until the Runtime owns the
            // execution loop. Ignore a projection gap rather than changing product flow.
        }
    }

    /// <summary>
    /// Waits for best-effort observability writes already queued by this session. A
    /// journal failure is intentionally swallowed: Desktop execution remains
    /// authoritative during this migration.
    /// </summary>
    public async Task FlushJournalAsync()
    {
        Task pending;
        lock (_journalWriteGate)
        {
            pending = _journalWrites;
        }

        try
        {
            await pending.ConfigureAwait(false);
        }
        catch
        {
            // QueueJournalSnapshot converts store errors to successful best-effort
            // completion, but retain this guard for a future store implementation.
        }
    }

    private string ContractId => $"desktop-contract-{_session.RunId}";

    private void Complete()
    {
        if (_session.Status == AgentRunStatus.Received)
        {
            EnsureUnderstanding();
        }

        if (_session.Status == AgentRunStatus.Understanding)
        {
            AdvanceIfPossible(AgentRunStatus.Conversation, "desktop-conversation");
        }

        if (_session.Status == AgentRunStatus.Conversation)
        {
            AdvanceIfPossible(AgentRunStatus.Completed, "desktop-completed");
            return;
        }

        if (_session.Status == AgentRunStatus.Planning || _session.Status == AgentRunStatus.AwaitingApproval ||
            _session.Status == AgentRunStatus.ReadyToExecute || _session.Status == AgentRunStatus.Executing)
        {
            EnsureExecuting();
            AdvanceIfPossible(AgentRunStatus.Verifying, "desktop-verifying", ContractId);
        }

        if (_session.Status == AgentRunStatus.Verifying)
        {
            AdvanceIfPossible(AgentRunStatus.Completed, "desktop-completed", ContractId, "desktop-completion");
        }
    }

    private void EnsureUnderstanding()
    {
        AdvanceIfPossible(AgentRunStatus.Understanding, "desktop-understanding");
    }

    private void EnsurePlanning()
    {
        EnsureUnderstanding();
        AdvanceIfPossible(AgentRunStatus.Planning, "desktop-planning");
    }

    private void EnsureExecuting()
    {
        EnsurePlanning();
        AdvanceIfPossible(AgentRunStatus.AwaitingApproval, "desktop-awaiting-approval", ContractId);
        AdvanceIfPossible(AgentRunStatus.ReadyToExecute, "desktop-ready-to-execute", ContractId);
        AdvanceIfPossible(AgentRunStatus.Executing, "desktop-executing", ContractId);
    }

    private void AdvanceIfPossible(AgentRunStatus next, string reason, string? contractId = null, string? evidenceId = null)
    {
        if (_session.Status != next && new AgentRunStateMachine().CanTransition(_session.Status, next))
        {
            _session.Advance(next, reason, contractId, evidenceId);
        }
    }

    private void QueueJournalSnapshot()
    {
        var journal = new AgentRunJournal(
            _session.RunId,
            AgentRunJournal.CurrentSchemaVersion,
            _session.History.ToArray(),
            DateTimeOffset.UtcNow,
            _contract);

        lock (_journalWriteGate)
        {
            _journalWrites = _journalWrites
                .ContinueWith(
                    _ => PersistSnapshotBestEffortAsync(journal),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default)
                .Unwrap();
        }
    }

    private async Task PersistSnapshotBestEffortAsync(AgentRunJournal journal)
    {
        try
        {
            await _journalStore.SaveAsync(journal).ConfigureAwait(false);
        }
        catch
        {
            // Observability must never make a model/tool run fail or change its result.
        }
    }
}
