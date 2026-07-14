namespace AgentQ.Runtime.Journaling;

public interface IAgentRunJournalStore
{
    Task SaveAsync(AgentRunJournal journal, CancellationToken cancellationToken = default);

    Task<RunJournalReadResult> ReadAsync(string runId, CancellationToken cancellationToken = default);
}
