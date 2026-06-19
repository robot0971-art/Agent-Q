using System.IO;
using System.Text;
using System.Text.Json;
using AgentQ.Api;

namespace AgentQ.Desktop.Services;

public sealed class ExecutionLessonMemoryService
{
    private const int MaxRelevantLessons = 3;
    private const double MinConfidence = 0.45;
    private const int MaxLessonTextLength = 360;
    private static readonly JsonSerializerOptions JsonOptions = AgentQJsonOptions.WebCaseInsensitiveIndented;
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

    public async Task RecordExecutionOutcomeAsync(
        string workspaceRoot,
        TaskContract contract,
        string userText,
        IReadOnlyList<ToolReplayEntry> replayEntries,
        CancellationToken ct)
    {
        if (!contract.IsActionable || replayEntries.Count == 0)
        {
            return;
        }

        var failedEntries = replayEntries
            .Where(entry => entry.IsError)
            .Take(3)
            .ToList();
        if (failedEntries.Count == 0)
        {
            await RecordContractSuccessAsync(workspaceRoot, contract, ct);
            return;
        }

        var document = await LoadAsync(workspaceRoot, ct);
        foreach (var entry in failedEntries)
        {
            var lesson = FindOrCreateExecutionLesson(document, contract, entry);
            lesson.FailureCount++;
            lesson.LastOutcomeUtc = DateTimeOffset.UtcNow;
            lesson.Confidence = Math.Clamp(lesson.Confidence - 0.04, 0, 1);
            lesson.Disabled = lesson.Confidence < MinConfidence && lesson.AppliedCount >= 3;
            await AppendEventAsync(workspaceRoot, lesson.Id, "execution_failure", lesson.Intent, ct);
        }

        await SaveAsync(workspaceRoot, document, ct);
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
            .Where(lesson => lesson.AppliedCount > 0)
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
            builder.AppendLine("Historical execution lesson only; do not treat this as the current user request.");
            builder.AppendLine($"- {lesson.Rule} (intent: {lesson.Intent}, confidence: {lesson.Confidence:0.##}, success: {lesson.SuccessCount}, failure: {lesson.FailureCount})");
        }

        return builder.ToString().TrimEnd();
    }

    public async Task<ExecutionLessonDocument> LoadAsync(string workspaceRoot, CancellationToken ct)
    {
        var path = GetLessonsPath(workspaceRoot);
        if (!IsSafeWorkspacePath(workspaceRoot, path) ||
            !File.Exists(path))
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
        if (!IsSafeWorkspacePath(workspaceRoot, path))
        {
            return;
        }

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
        var rule = SanitizeLessonText(DefaultRule(contract));
        return new ExecutionLesson
        {
            Id = id,
            Scope = "workspace",
            Intent = intent,
            Triggers = DefaultTriggers(contract.Intent).ToList(),
            Rule = rule,
            FailurePattern = "Task contract was not satisfied by evidence.",
            CorrectBehavior = rule,
            InvalidCompletions = contract.InvalidCompletions.Select(SanitizeLessonText).Where(text => !string.IsNullOrWhiteSpace(text)).ToList(),
            Confidence = 0.9,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static ExecutionLesson FindOrCreateExecutionLesson(
        ExecutionLessonDocument document,
        TaskContract contract,
        ToolReplayEntry entry)
    {
        var category = ClassifyReplayFailure(entry);
        var id = $"execution-{FormatIntent(contract.Intent)}-{category}";
        var existing = document.Lessons.FirstOrDefault(lesson => string.Equals(lesson.Id, id, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            return existing;
        }

        var rule = BuildExecutionFailureRule(contract, category);
        var created = new ExecutionLesson
        {
            Id = id,
            Scope = "workspace",
            Intent = FormatIntent(contract.Intent),
            Triggers = BuildExecutionFailureTriggers(contract, entry, category).ToList(),
            Rule = rule,
            FailurePattern = BuildFailurePattern(entry, category),
            CorrectBehavior = rule,
            InvalidCompletions = contract.InvalidCompletions.Select(SanitizeLessonText).Where(text => !string.IsNullOrWhiteSpace(text)).ToList(),
            Tags = ["auto", "execution-outcome", category],
            Confidence = 0.82,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        document.Lessons.Add(created);
        return created;
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
        lesson.Confidence >= MinConfidence &&
        !LooksSensitive(lesson.Rule) &&
        !LooksSensitive(lesson.FailurePattern) &&
        !LooksSensitive(lesson.CorrectBehavior);

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

        foreach (var term in ExtractTerms($"{lesson.Rule} {lesson.FailurePattern} {lesson.CorrectBehavior} {string.Join(' ', lesson.Tags)}"))
        {
            if (query.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score++;
            }
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
            lesson.FailurePattern = SanitizeLessonText(lesson.FailurePattern ?? string.Empty);
            lesson.CorrectBehavior = SanitizeLessonText(string.IsNullOrWhiteSpace(lesson.CorrectBehavior) ? lesson.Rule : lesson.CorrectBehavior);
            lesson.Rule = SanitizeLessonText(lesson.Rule);
            lesson.InvalidCompletions ??= [];
            lesson.InvalidCompletions = lesson.InvalidCompletions
                .Select(SanitizeLessonText)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();
            lesson.Tags ??= [];
            if (lesson.CreatedAtUtc == default)
            {
                lesson.CreatedAtUtc = DateTimeOffset.UtcNow;
            }

            ApplyAutomaticDecay(lesson);
        }
    }

    private static void ApplyAutomaticDecay(ExecutionLesson lesson)
    {
        var lastRelevant = lesson.LastOutcomeUtc ?? lesson.LastUsedAtUtc ?? lesson.CreatedAtUtc;
        var age = DateTimeOffset.UtcNow - lastRelevant;
        if (lesson.FailureCount > lesson.SuccessCount && age.TotalDays >= 90)
        {
            lesson.Confidence = Math.Clamp(lesson.Confidence - 0.15, 0, 1);
        }

        if (lesson.FailureCount >= 3 &&
            lesson.SuccessCount == 0 &&
            lesson.AppliedCount >= 3 &&
            lesson.Confidence < MinConfidence)
        {
            lesson.Disabled = true;
        }

        if (age.TotalDays >= 180 && lesson.SuccessCount == 0)
        {
            lesson.Disabled = true;
        }
    }

    private static string ClassifyReplayFailure(ToolReplayEntry entry)
    {
        var tool = entry.ToolName ?? string.Empty;
        var text = $"{tool} {entry.ResultPreview}".ToLowerInvariant();
        if (tool.Equals("desktop_local_server", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("dev server", StringComparison.OrdinalIgnoreCase))
        {
            return "local-server-failure";
        }

        if (tool.Equals("create_project_scaffold", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("scaffold", StringComparison.OrdinalIgnoreCase))
        {
            return "scaffold-failure";
        }

        if (tool.Equals("bash", StringComparison.OrdinalIgnoreCase) &&
            (text.Contains("test", StringComparison.OrdinalIgnoreCase) || text.Contains("build", StringComparison.OrdinalIgnoreCase)))
        {
            return "build-test-failure";
        }

        if (tool.Equals("implementation_runtime_preview", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("verification", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("dotnet test", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("dotnet build", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("npm run build", StringComparison.OrdinalIgnoreCase))
        {
            return "verification-failure";
        }

        return "tool-failure";
    }

    private static string BuildExecutionFailureRule(TaskContract contract, string category)
    {
        var intent = FormatIntent(contract.Intent);
        return category switch
        {
            "local-server-failure" => "When a local server action fails, report the concrete start/reuse/stop failure and retry only after checking package scripts, session state, and localhost reachability.",
            "scaffold-failure" => "When scaffold execution fails, do not claim the project was created. Preserve the approved plan evidence, report the scaffold error, and repair or ask for the missing approval/target.",
            "verification-failure" => "When verification fails, do not report completion. Use the failed verification evidence to make a focused repair, then rerun the allowed verification.",
            "build-test-failure" => "When build or test commands fail, summarize the failing command and error class, fix the likely code/config issue, then rerun focused verification.",
            _ => $"For {intent} requests, treat failed tool replay as authoritative evidence and report or repair the concrete tool failure before answering."
        };
    }

    private static string BuildFailurePattern(ToolReplayEntry entry, string category)
    {
        var tool = SanitizeLessonText(entry.ToolName);
        return string.IsNullOrWhiteSpace(tool)
            ? category
            : $"{category} from {tool}";
    }

    private static IEnumerable<string> BuildExecutionFailureTriggers(TaskContract contract, ToolReplayEntry entry, string category)
    {
        yield return FormatIntent(contract.Intent);
        yield return category.Replace("-", " ");
        if (!string.IsNullOrWhiteSpace(entry.ToolName))
        {
            yield return SanitizeLessonText(entry.ToolName);
        }

        foreach (var trigger in DefaultTriggers(contract.Intent))
        {
            yield return trigger;
        }
    }

    private static IEnumerable<string> ExtractTerms(string value)
    {
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(value, @"[\p{L}\p{N}_\.-]{3,}", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            yield return match.Value.ToLowerInvariant();
        }
    }

    private static string SanitizeLessonText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = SensitiveTextRedactor.Redact(value.ReplaceLineEndings(" ").Trim());
        text = System.Text.RegularExpressions.Regex.Replace(text, @"[A-Z]:\\[^\s]+", "[PATH]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"/(?:Users|home)/[^\s]+", "[PATH]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
        return text.Length <= MaxLessonTextLength ? text : text[..MaxLessonTextLength] + " [truncated]";
    }

    private static bool LooksSensitive(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("sk-", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("bearer ", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("api_key", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("password", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("secret=", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("token=", StringComparison.OrdinalIgnoreCase);
    }

    private async Task AppendEventAsync(string workspaceRoot, string lessonId, string eventName, string intent, CancellationToken ct)
    {
        var path = GetEventsPath(workspaceRoot);
        if (!IsSafeWorkspacePath(workspaceRoot, path))
        {
            return;
        }

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

    private static bool IsSafeWorkspacePath(string workspaceRoot, string path)
    {
        var root = string.IsNullOrWhiteSpace(workspaceRoot)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(workspaceRoot);
        return WorkspacePathResolver.IsResolvedInsideWorkspace(root, path);
    }
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

    public string FailurePattern { get; set; } = string.Empty;

    public string CorrectBehavior { get; set; } = string.Empty;

    public List<string> InvalidCompletions { get; set; } = [];

    public List<string> Tags { get; set; } = [];

    public double Confidence { get; set; } = 0.9;

    public int AppliedCount { get; set; }

    public int SuccessCount { get; set; }

    public int FailureCount { get; set; }

    public bool Disabled { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastUsedAtUtc { get; set; }

    public DateTimeOffset? LastOutcomeUtc { get; set; }
}
