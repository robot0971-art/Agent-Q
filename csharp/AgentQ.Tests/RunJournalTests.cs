using AgentQ.Runtime.Journaling;
using AgentQ.Runtime.Runs;
using Xunit;

namespace AgentQ.Tests;

public sealed class RunJournalTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "agentq-run-journal-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAsync_ReplacesSnapshotAndReadsBackTheLatestValidJournal()
    {
        var store = new FileAgentRunJournalStore(_directory);
        await store.SaveAsync(CreateJournal("run_123", "received", DateTimeOffset.Parse("2026-07-13T00:00:00Z")));
        await store.SaveAsync(CreateJournal("run_123", "completed", DateTimeOffset.Parse("2026-07-13T00:01:00Z")));

        var result = await store.ReadAsync("run_123");

        Assert.Equal(RunJournalReadStatus.Loaded, result.Status);
        Assert.NotNull(result.Journal);
        Assert.Equal("completed", result.Journal.Transitions.Single().ReasonCode);
        Assert.Single(Directory.GetFiles(_directory, "*.json"));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task ReadAsync_ReturnsMissingWithoutCreatingAFile()
    {
        var store = new FileAgentRunJournalStore(_directory);

        var result = await store.ReadAsync("missing_run");

        Assert.Equal(RunJournalReadStatus.Missing, result.Status);
        Assert.Null(result.Journal);
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task ReadAsync_QuarantinesCorruptContentAsARecoverableResult()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "run_123.json"), "{ not json");
        var store = new FileAgentRunJournalStore(_directory);

        var result = await store.ReadAsync("run_123");

        Assert.Equal(RunJournalReadStatus.Corrupt, result.Status);
        Assert.Null(result.Journal);
        Assert.Equal("The run journal could not be read safely.", result.Error);
    }

    [Fact]
    public async Task SaveAsync_RejectsTraversalAndDoesNotWriteOutsideJournalDirectory()
    {
        var store = new FileAgentRunJournalStore(_directory);

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(CreateJournal("../escape", "received", DateTimeOffset.UtcNow)));

        Assert.False(Directory.Exists(_directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static AgentRunJournal CreateJournal(string runId, string reasonCode, DateTimeOffset occurredAt) =>
        new(
            runId,
            AgentRunJournal.CurrentSchemaVersion,
            [new AgentRunTransition(runId, null, AgentRunStatus.Received, AgentRunStatus.Received, reasonCode, "policy-1", null, occurredAt)],
            occurredAt);
}
