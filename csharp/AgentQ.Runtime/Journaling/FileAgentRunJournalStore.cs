using System.Text.Json;
using AgentQ.Runtime.Contracts;
using AgentQ.Runtime.Runs;

namespace AgentQ.Runtime.Journaling;

/// <summary>
/// Stores one complete JSON snapshot per run. A temporary file is fully flushed
/// before it replaces the previous snapshot, so a partially written file is never
/// presented as a successful recovery record.
/// </summary>
public sealed class FileAgentRunJournalStore : IAgentRunJournalStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly string _journalDirectory;

    public FileAgentRunJournalStore(string journalDirectory)
    {
        if (string.IsNullOrWhiteSpace(journalDirectory))
        {
            throw new ArgumentException("A journal directory is required.", nameof(journalDirectory));
        }

        _journalDirectory = Path.GetFullPath(journalDirectory);
    }

    public async Task SaveAsync(AgentRunJournal journal, CancellationToken cancellationToken = default)
    {
        ValidateJournal(journal);
        Directory.CreateDirectory(_journalDirectory);

        var destinationPath = GetJournalPath(journal.RunId);
        var temporaryPath = Path.Combine(
            _journalDirectory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, journal, SerializerOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<RunJournalReadResult> ReadAsync(string runId, CancellationToken cancellationToken = default)
    {
        var path = GetJournalPath(runId);
        if (!File.Exists(path))
        {
            return RunJournalReadResult.Missing();
        }

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var journal = await JsonSerializer.DeserializeAsync<AgentRunJournal>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            ValidateJournal(journal);
            return RunJournalReadResult.Loaded(journal!);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or JsonException or ArgumentException or NotSupportedException)
        {
            return RunJournalReadResult.Corrupt("The run journal could not be read safely.");
        }
    }

    private string GetJournalPath(string runId) =>
        Path.Combine(_journalDirectory, $"{ValidateRunId(runId)}.json");

    private static void ValidateJournal(AgentRunJournal? journal)
    {
        if (journal is null)
        {
            throw new ArgumentNullException(nameof(journal));
        }

        ValidateRunId(journal.RunId);
        if (journal.SchemaVersion != AgentRunJournal.CurrentSchemaVersion)
        {
            throw new ArgumentException("The journal schema version is not supported.", nameof(journal));
        }

        if (journal.Transitions is null || journal.Transitions.Count == 0)
        {
            throw new ArgumentException("A journal must contain at least one transition.", nameof(journal));
        }

        if (journal.Contract is { } contract &&
            (!string.Equals(contract.ContractId, journal.RunId, StringComparison.Ordinal) ||
             string.IsNullOrWhiteSpace(contract.Hash) ||
             string.IsNullOrWhiteSpace(contract.WorkspaceId)))
        {
            throw new ArgumentException("The journal contains an invalid runtime contract.", nameof(journal));
        }

        DateTimeOffset? previousOccurredAt = null;
        foreach (var transition in journal.Transitions)
        {
            if (transition is null || !string.Equals(transition.RunId, journal.RunId, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(transition.ReasonCode) || string.IsNullOrWhiteSpace(transition.PolicyVersion))
            {
                throw new ArgumentException("The journal contains an invalid transition.", nameof(journal));
            }

            if (previousOccurredAt is { } previous && transition.OccurredAt < previous)
            {
                throw new ArgumentException("Journal transitions must be ordered by occurrence time.", nameof(journal));
            }

            previousOccurredAt = transition.OccurredAt;
        }
    }

    private static string ValidateRunId(string? runId)
    {
        if (string.IsNullOrWhiteSpace(runId) || runId.Length > 128 ||
            runId.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_')))
        {
            throw new ArgumentException("The run id must use only letters, digits, hyphens, or underscores.", nameof(runId));
        }

        return runId;
    }
}
