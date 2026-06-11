using System.IO;
using System.Text;
using System.Text.Json;

namespace AgentQ.Desktop.Services;

public sealed class ExecutionLessonMemoryService
{
    private const int MaxRelevantLessons = 3;
    private const double MinConfidence = 0.45;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ExecutionLesson>> SelectRelevantAsync(
        string workspaceRoot,
        string userText,
        TaskContract contract,
        CancellationToken ct)
    {
        if (!contract.IsActionable)
        {
            return [];
        }

        var document = await LoadAsync(workspaceRoot, ct);
        var query = userText.ToLowerInvariant();
        return document.Lessons
            .Where(lesson => IsUseful(lesson))
            .Where(lesson => IntentMatches(lesson, contract))
            .Select(lesson => new
            {
                Lesson = lesson,
                Score = Score(lesson, query)
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Lesson.Confidence)
            .ThenByDescending(item => item.Lesson.LastUsedAtUtc ?? item.Lesson.CreatedAtUtc)
            .Take(MaxRelevantLessons)
            .Select(item => item.Lesson)
            .ToList();
    }

    public async Task<IReadOnlyList<ExecutionLesson>> TouchRelevantAsync(
        string workspaceRoot,
        string userText,
        TaskContract contract,
        CancellationToken ct)
    {
        var relevant = (await SelectRelevantAsync(workspaceRoot, userText, contract, ct)).ToList();
        if (relevant.Count == 0)
        {
            return [];
        }

        var document = await LoadAsync(workspaceRoot, ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var selected in relevant)
        {
            var lesson = document.Lessons.FirstOrDefault(item => string.Equals(item.Id, selected.Id, StringComparison.OrdinalIgnoreCase));
            if (lesson == null)
            {
                continue;
            }

            lesson.LastUsedAtUtc = now;
            lesson.AppliedCount++;
            await AppendEventAsync(workspaceRoot, lesson.Id, "applied", lesson.Intent, ct);
        }

        await SaveAsync(workspaceRoot, document, ct);
        return relevant;
    }

    public async Task RecordContractFailureAsync(
        string workspaceRoot,
        TaskContract contract,
        string userText,
        string assistantText,
        CancellationToken ct)
    {
        if (!contract.IsActionable)
        {
            return;
        }

        var document = await LoadAsync(workspaceRoot, ct);
        var lesson = FindOrCreateLesson(document, contract);
        lesson.FailureCount++;
        lesson.LastOutcomeUtc = DateTimeOffset.UtcNow;
        lesson.Confidence = Math.Clamp(lesson.Confidence - 0.12, 0, 1);
        lesson.Disabled = lesson.Confidence < MinConfidence && lesson.AppliedCount >= 3;
        await SaveAsync(workspaceRoot, document, ct);
        await AppendEventAsync(workspaceRoot, lesson.Id, "failure", lesson.Intent, ct);
    }

    public async Task RecordContractSuccessAsync(
        string workspaceRoot,
        TaskContract contract,
        CancellationToken ct)
    {
        if (!contract.IsActionable)
        {
            return;
        }

        var document = await LoadAsync(workspaceRoot, ct);
        var matching = document.Lessons
            .Where(lesson => IsUseful(lesson))
            .Where(lesson => IntentMatches(lesson, contract))
            .OrderByDescending(lesson => lesson.LastUsedAtUtc ?? lesson.CreatedAtUtc)
            .FirstOrDefault();
        if (matching == null)
        {
            return;
        }

        matching.SuccessCount++;
        matching.LastOutcomeUtc = DateTimeOffset.UtcNow;
        matching.Confidence = Math.Clamp(matching.Confidence + 0.03, 0, 1);
        await SaveAsync(workspaceRoot, document, ct);
        await AppendEventAsync(workspaceRoot, matching.Id, "success", matching.Intent, ct);
    }

    public string BuildContext(IReadOnlyList<ExecutionLesson> lessons)
    {
        if (lessons.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Relevant execution lessons:");
        foreach (var lesson in lessons.Take(MaxRelevantLessons))
        {
            builder.AppendLine($"- {lesson.Rule} (intent: {lesson.Intent}, confidence: {lesson.Confidence:0.##}, success: {lesson.SuccessCount}, failure: {lesson.FailureCount})");
        }

        return builder.ToString().TrimEnd();
    }

    public async Task<ExecutionLessonDocument> LoadAsync(string workspaceRoot, CancellationToken ct)
    {
        var path = GetLessonsPath(workspaceRoot);
        if (!File.Exists(path))
        {
            return new ExecutionLessonDocument();
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            var document = JsonSerializer.Deserialize<ExecutionLessonDocument>(json, JsonOptions) ?? new ExecutionLessonDocument();
            Normalize(document);
            return document;
        }
        catch
        {
            return new ExecutionLessonDocument();
        }
    }

    private async Task SaveAsync(string workspaceRoot, ExecutionLessonDocument document, CancellationToken ct)
    {
        document.Version = 1;
        var path = GetLessonsPath(workspaceRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(document, JsonOptions), ct);
        if (File.Exists(path))
        {
            File.Replace(tempPath, path, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }

    private static ExecutionLesson FindOrCreateLesson(ExecutionLessonDocument document, TaskContract contract)
    {
        var id = CreateLessonId(contract);
        var existing = document.Lessons.FirstOrDefault(lesson => string.Equals(lesson.Id, id, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            return existing;
        }

        var created = CreateLesson(contract, id);
        document.Lessons.Add(created);
        return created;
    }

    private static ExecutionLesson CreateLesson(TaskContract contract, string id)
    {
        var intent = FormatIntent(contract.Intent);
        return new ExecutionLesson
        {
            Id = id,
            Scope = "workspace",
            Intent = intent,
            Triggers = DefaultTriggers(contract.Intent).ToList(),
            Rule = DefaultRule(contract),
            InvalidCompletions = contract.InvalidCompletions.ToList(),
            Confidence = 0.9,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static string CreateLessonId(TaskContract contract) => contract.Intent switch
    {
        TaskContractIntent.RunLocalServer => "run-local-server-no-structure-summary",
        _ => "task-contract-" + FormatIntent(contract.Intent)
    };

    private static IReadOnlyList<string> DefaultTriggers(TaskContractIntent intent) => intent switch
    {
        TaskContractIntent.RunLocalServer => ["\uB85C\uCEEC\uC11C\uBC84", "\uC11C\uBC84 \uB744\uC6CC", "npm run dev", "dev server", "localhost"],
        _ => []
    };

    private static string DefaultRule(TaskContract contract) => contract.Intent switch
    {
        TaskContractIntent.RunLocalServer => "For run_local_server requests, do not stop after describing project structure. Start the dev server, verify a localhost URL, then report it.",
        _ => contract.Goal
    };

    private static bool IsUseful(ExecutionLesson lesson) =>
        !lesson.Disabled &&
        !string.IsNullOrWhiteSpace(lesson.Rule) &&
        lesson.Confidence >= MinConfidence;

    private static bool IntentMatches(ExecutionLesson lesson, TaskContract contract) =>
        string.Equals(lesson.Intent, FormatIntent(contract.Intent), StringComparison.OrdinalIgnoreCase);

    private static int Score(ExecutionLesson lesson, string query)
    {
        var score = 0;
        foreach (var trigger in lesson.Triggers ?? [])
        {
            if (!string.IsNullOrWhiteSpace(trigger) && query.Contains(trigger.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
            {
                score += 3;
            }
        }

        if (query.Contains(lesson.Intent.Replace("_", string.Empty), StringComparison.OrdinalIgnoreCase))
        {
            score += 2;
        }

        return score;
    }

    private static void Normalize(ExecutionLessonDocument document)
    {
        document.Lessons = document.Lessons?
            .Where(lesson => lesson != null)
            .ToList() ?? [];

        foreach (var lesson in document.Lessons)
        {
            lesson.Id ??= string.Empty;
            lesson.Scope ??= "workspace";
            lesson.Intent ??= string.Empty;
            lesson.Triggers ??= [];
            lesson.Rule ??= string.Empty;
            lesson.InvalidCompletions ??= [];
            if (lesson.CreatedAtUtc == default)
            {
                lesson.CreatedAtUtc = DateTimeOffset.UtcNow;
            }
        }
    }

    private async Task AppendEventAsync(string workspaceRoot, string lessonId, string eventName, string intent, CancellationToken ct)
    {
        var path = GetEventsPath(workspaceRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(new
        {
            ts = DateTimeOffset.UtcNow,
            lessonId,
            @event = eventName,
            intent
        }, EventJsonOptions);
        await File.AppendAllTextAsync(path, json + Environment.NewLine, ct);
    }

    private static string FormatIntent(TaskContractIntent intent) => intent switch
    {
        TaskContractIntent.RunLocalServer => "run_local_server",
        TaskContractIntent.StopLocalServer => "stop_local_server",
        TaskContractIntent.DeletePath => "delete_path",
        TaskContractIntent.CreateDirectory => "create_directory",
        TaskContractIntent.CreateFile => "create_file",
        TaskContractIntent.CreateProject => "create_project",
        TaskContractIntent.ModifyCode => "modify_code",
        TaskContractIntent.RunVerification => "run_verification",
        TaskContractIntent.SearchAndSummarize => "search_and_summarize",
        TaskContractIntent.InspectProject => "inspect_project",
        TaskContractIntent.ExplainOrChat => "explain_or_chat",
        _ => "none"
    };

    private static string GetLessonsPath(string workspaceRoot) =>
        Path.Combine(Path.GetFullPath(workspaceRoot), ".agentq", "lessons", "execution-lessons.json");

    private static string GetEventsPath(string workspaceRoot) =>
        Path.Combine(Path.GetFullPath(workspaceRoot), ".agentq", "lessons", "execution-lesson-events.jsonl");
}

public sealed class ExecutionLessonDocument
{
    public int Version { get; set; } = 1;

    public List<ExecutionLesson> Lessons { get; set; } = [];
}

public sealed class ExecutionLesson
{
    public string Id { get; set; } = string.Empty;

    public string Scope { get; set; } = "workspace";

    public string Intent { get; set; } = string.Empty;

    public List<string> Triggers { get; set; } = [];

    public string Rule { get; set; } = string.Empty;

    public List<string> InvalidCompletions { get; set; } = [];

    public double Confidence { get; set; } = 0.9;

    public int AppliedCount { get; set; }

    public int SuccessCount { get; set; }

    public int FailureCount { get; set; }

    public bool Disabled { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastUsedAtUtc { get; set; }

    public DateTimeOffset? LastOutcomeUtc { get; set; }
}
