using AgentQ.Desktop.Services;
using AgentQ.Runtime.Journaling;
using AgentQ.Runtime.Runs;
using Xunit;

namespace AgentQ.Tests;

public sealed class DesktopRuntimeRunLifecycleTests
{
    [Fact]
    public async Task TerminalRun_IsPersistedAndRecoverableFromTheJournal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "agentq-lifecycle-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var journalStore = new FileAgentRunJournalStore(directory);
            var run = new DesktopRuntimeRunLifecycle(journalStore: journalStore).Start("desktop-journaled");

            run.RecordDesktopState(AgentRunState.GatheringContext);
            run.RecordDesktopState(AgentRunState.RunningTool);
            run.RecordDesktopState(AgentRunState.Done);
            await run.FlushJournalAsync();

            var recovered = await journalStore.ReadAsync("desktop-journaled");

            Assert.Equal(RunJournalReadStatus.Loaded, recovered.Status);
            Assert.NotNull(recovered.Journal);
            Assert.Equal(AgentRunStatus.Completed, recovered.Journal.Transitions[^1].NextStatus);
            Assert.Contains(recovered.Journal.Transitions, transition => transition.NextStatus == AgentRunStatus.Executing);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task JournalFailure_DoesNotChangeTheObservedDesktopLifecycle()
    {
        var run = new DesktopRuntimeRunLifecycle(journalStore: new ThrowingJournalStore()).Start("desktop-journal-failure");

        run.RecordDesktopState(AgentRunState.GatheringContext);
        run.RecordDesktopState(AgentRunState.Cancelled);
        await run.FlushJournalAsync();

        Assert.Equal(AgentRunStatus.Cancelled, run.History[^1].NextStatus);
    }

    [Fact]
    public void ToolAndVerificationTimeline_ProjectsToCompletedRuntimeRun()
    {
        var run = new DesktopRuntimeRunLifecycle().Start("desktop-run");

        run.RecordDesktopState(AgentRunState.GatheringContext);
        run.RecordDesktopState(AgentRunState.Planning);
        run.RecordDesktopState(AgentRunState.WaitingForApproval);
        run.RecordDesktopState(AgentRunState.RunningTool);
        run.RecordDesktopState(AgentRunState.Verifying);
        run.RecordDesktopState(AgentRunState.Done);

        Assert.Equal(AgentRunStatus.Completed, run.History[^1].NextStatus);
        Assert.Contains(run.History, transition => transition.NextStatus == AgentRunStatus.Executing);
        Assert.Contains(run.History, transition => transition.NextStatus == AgentRunStatus.Verifying);
        Assert.Equal("desktop-completion", run.History[^1].EvidenceId);
    }

    [Fact]
    public void ConversationTimeline_CompletesWithoutCreatingExecutionContract()
    {
        var run = new DesktopRuntimeRunLifecycle().Start("desktop-conversation");

        run.RecordDesktopState(AgentRunState.GatheringContext);
        run.RecordDesktopState(AgentRunState.Done);

        Assert.Equal(AgentRunStatus.Completed, run.History[^1].NextStatus);
        Assert.DoesNotContain(run.History, transition => !string.IsNullOrWhiteSpace(transition.ContractId));
    }

    [Fact]
    public void Cancellation_EndsRuntimeRunWithoutChangingDesktopFlow()
    {
        var run = new DesktopRuntimeRunLifecycle().Start("desktop-cancelled");

        run.RecordDesktopState(AgentRunState.GatheringContext);
        run.RecordDesktopState(AgentRunState.Cancelled);

        Assert.Equal(AgentRunStatus.Cancelled, run.History[^1].NextStatus);
    }

    private sealed class ThrowingJournalStore : IAgentRunJournalStore
    {
        public Task SaveAsync(AgentRunJournal journal, CancellationToken cancellationToken = default) =>
            Task.FromException(new IOException("Journal disk unavailable."));

        public Task<RunJournalReadResult> ReadAsync(string runId, CancellationToken cancellationToken = default) =>
            Task.FromException<RunJournalReadResult>(new IOException("Journal disk unavailable."));
    }
}
