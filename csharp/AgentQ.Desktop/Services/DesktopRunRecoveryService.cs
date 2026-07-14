using AgentQ.Runtime.Journaling;
using AgentQ.Runtime.Runs;
using System.IO;

namespace AgentQ.Desktop.Services;

public enum DesktopRunRecoveryKind
{
    ResumeRequiresApproval,
    InspectOnly,
    CorruptJournal
}

public sealed record DesktopRunRecoveryCandidate(
    string RunId,
    DesktopRunRecoveryKind Kind,
    AgentRunStatus? LastStatus,
    string Message,
    DateTimeOffset LastUpdatedAt);

/// <summary>
/// Finds interrupted Runtime journals for a selected workspace. This service is
/// deliberately discovery-only: finding a run never resumes, rolls back, or
/// mutates a workspace. The UI must obtain fresh user approval before any later
/// recovery action can execute.
/// </summary>
public sealed class DesktopRunRecoveryService
{
    public async Task<IReadOnlyList<DesktopRunRecoveryCandidate>> FindCandidatesAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            return [];
        }

        var root = Path.GetFullPath(workspaceRoot);
        var journalDirectory = Path.Combine(root, ".agentq", "runs");
        if (!WorkspacePathResolver.IsInsideWorkspace(root, journalDirectory) ||
            !WorkspacePathResolver.IsResolvedInsideWorkspace(root, journalDirectory) ||
            !Directory.Exists(journalDirectory))
        {
            return [];
        }

        var store = new FileAgentRunJournalStore(journalDirectory);
        var candidates = new List<DesktopRunRecoveryCandidate>();
        foreach (var path in Directory.EnumerateFiles(journalDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runId = Path.GetFileNameWithoutExtension(path);
            try
            {
                var read = await store.ReadAsync(runId, cancellationToken).ConfigureAwait(false);
                if (read.Status == RunJournalReadStatus.Corrupt)
                {
                    candidates.Add(new DesktopRunRecoveryCandidate(
                        runId,
                        DesktopRunRecoveryKind.CorruptJournal,
                        null,
                        "Run journal is corrupt and must be inspected before recovery.",
                        File.GetLastWriteTimeUtc(path)));
                    continue;
                }

                var journal = read.Journal;
                if (journal is null || IsTerminal(journal.Transitions[^1].NextStatus))
                {
                    continue;
                }

                var last = journal.Transitions[^1];
                candidates.Add(new DesktopRunRecoveryCandidate(
                    journal.RunId,
                    CanResume(last.NextStatus) ? DesktopRunRecoveryKind.ResumeRequiresApproval : DesktopRunRecoveryKind.InspectOnly,
                    last.NextStatus,
                    CanResume(last.NextStatus)
                        ? "This run was interrupted. Resume requires a new user approval."
                        : "This run requires inspection before a recovery action can be offered.",
                    journal.UpdatedAt));
            }
            catch (ArgumentException)
            {
                // A filename that is not a valid Runtime run id is not a journal.
            }
        }

        return candidates.OrderByDescending(candidate => candidate.LastUpdatedAt).ToArray();
    }

    private static bool IsTerminal(AgentRunStatus status) => status is
        AgentRunStatus.Completed or AgentRunStatus.Failed or AgentRunStatus.Cancelled or AgentRunStatus.RolledBack;

    private static bool CanResume(AgentRunStatus status) => status is
        AgentRunStatus.AwaitingClarification or AgentRunStatus.Planning or AgentRunStatus.AwaitingApproval or
        AgentRunStatus.ReadyToExecute or AgentRunStatus.Executing or AgentRunStatus.Verifying or AgentRunStatus.Repairing;
}
