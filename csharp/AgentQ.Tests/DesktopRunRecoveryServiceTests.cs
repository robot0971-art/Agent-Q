using AgentQ.Desktop.Services;
using AgentQ.Runtime.Journaling;
using AgentQ.Runtime.Runs;
using Xunit;

namespace AgentQ.Tests;

public sealed class DesktopRunRecoveryServiceTests
{
    [Fact]
    public async Task FindCandidatesAsync_ReturnsOnlyInterruptedRunsAndNeverResumesThem()
    {
        var workspace = CreateWorkspace();
        try
        {
            var directory = Path.Combine(workspace, ".agentq", "runs");
            var store = new FileAgentRunJournalStore(directory);
            await store.SaveAsync(CreateJournal("completed", AgentRunStatus.Completed));
            await store.SaveAsync(CreateJournal("interrupted", AgentRunStatus.Executing));

            var candidates = await new DesktopRunRecoveryService().FindCandidatesAsync(workspace);

            var candidate = Assert.Single(candidates);
            Assert.Equal("interrupted", candidate.RunId);
            Assert.Equal(DesktopRunRecoveryKind.ResumeRequiresApproval, candidate.Kind);
            Assert.Equal(AgentRunStatus.Executing, candidate.LastStatus);
            Assert.Contains("approval", candidate.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task FindCandidatesAsync_ReportsCorruptJournalInsteadOfIgnoringIt()
    {
        var workspace = CreateWorkspace();
        try
        {
            var directory = Path.Combine(workspace, ".agentq", "runs");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "corrupt.json"), "{ invalid json");

            var candidates = await new DesktopRunRecoveryService().FindCandidatesAsync(workspace);

            var candidate = Assert.Single(candidates);
            Assert.Equal(DesktopRunRecoveryKind.CorruptJournal, candidate.Kind);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static AgentRunJournal CreateJournal(string runId, AgentRunStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        return new AgentRunJournal(runId, AgentRunJournal.CurrentSchemaVersion,
        [new AgentRunTransition(runId, null, AgentRunStatus.Received, status, "test", "test-policy", null, now)], now);
    }

    private static string CreateWorkspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "agentq-recovery-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        return workspace;
    }
}
