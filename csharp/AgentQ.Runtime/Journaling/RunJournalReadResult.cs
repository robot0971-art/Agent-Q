namespace AgentQ.Runtime.Journaling;

public sealed record RunJournalReadResult(
    RunJournalReadStatus Status,
    AgentRunJournal? Journal,
    string? Error)
{
    public static RunJournalReadResult Missing() =>
        new(RunJournalReadStatus.Missing, null, null);

    public static RunJournalReadResult Loaded(AgentRunJournal journal) =>
        new(RunJournalReadStatus.Loaded, journal, null);

    public static RunJournalReadResult Corrupt(string error) =>
        new(RunJournalReadStatus.Corrupt, null, error);
}
