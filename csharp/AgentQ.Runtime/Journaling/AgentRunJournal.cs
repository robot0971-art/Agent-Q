using AgentQ.Runtime.Runs;

namespace AgentQ.Runtime.Journaling;

/// <summary>
/// Durable, replayable snapshot of a single agent run. The schema is deliberately
/// small so recovery data does not become a transcript or a tool-output archive.
/// </summary>
public sealed record AgentRunJournal(
    string RunId,
    int SchemaVersion,
    IReadOnlyList<AgentRunTransition> Transitions,
    DateTimeOffset UpdatedAt)
{
    public const int CurrentSchemaVersion = 1;
}
