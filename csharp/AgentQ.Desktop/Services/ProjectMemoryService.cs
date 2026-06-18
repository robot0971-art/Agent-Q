using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentQ.Desktop.Services;

public sealed class ProjectMemoryService
{
    private const string WorkspaceMemoryDirectoryName = ".agentq";
    private const string LocalMemoryFileName = "memory.local.json";
    private const string SharedMemoryFileName = "memory.shared.json";
    private const int MaxMemoryTextLength = 1000;
    private const int MaxMemoryCommandLength = 300;
    private const int MaxContextBankFactLength = 500;
    private const double MinUsefulLessonConfidence = 0.2;
    private const int StaleUnusedLessonDays = 180;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _memoryDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".agentq",
        "project-memory");

    private readonly WorkspaceAnalysisService _workspaceAnalysisService;
    private readonly ProjectMemoryGcService _gcService = new();

    public ProjectMemoryService(WorkspaceAnalysisService? workspaceAnalysisService = null)
    {
        _workspaceAnalysisService = workspaceAnalysisService ?? new WorkspaceAnalysisService();
    }

    public async Task<ProjectMemory> LoadOrDiscoverAsync(string workspaceRoot, CancellationToken ct)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var path = GetMemoryPath(root);

        if (File.Exists(path))
        {
            try
            {
                var json = await File.ReadAllTextAsync(path, ct);
                var memory = JsonSerializer.Deserialize<ProjectMemory>(json, Options);
                if (memory != null)
                {
                    ApplyLocalConfig(root, memory);
                    await ApplyWorkspaceMemoryAsync(root, memory, ct);
                    await EnrichContextBankAsync(root, memory, ct);
                    return memory;
                }
            }
            catch
            {
                // Corrupt memory should not block a run; rediscover below.
            }
        }

        var discovered = Discover(root);
        await EnrichContextBankAsync(root, discovered, ct);
        await SaveAsync(discovered, ct);
        await ApplyWorkspaceMemoryAsync(root, discovered, ct);
        return discovered;
    }

    public async Task SaveAsync(ProjectMemory memory, CancellationToken ct)
    {
        Directory.CreateDirectory(_memoryDirectory);
        memory.UpdatedAt = DateTime.Now;

        var path = GetMemoryPath(memory.WorkspaceRoot);
        var tempPath = Path.Combine(_memoryDirectory, $"{Guid.NewGuid():N}.tmp");
        var json = JsonSerializer.Serialize(memory, Options);

        try
        {
            await File.WriteAllTextAsync(tempPath, json, ct);
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public string BuildContext(ProjectMemory memory) => BuildContext(memory, query: string.Empty);

    public string BuildContext(ProjectMemory memory, string query)
    {
        var hasQuery = ExtractTerms(query).Any();
        var lessons = SelectRelevantLessons(memory.Lessons, query, 12);
        var errorHistoryLessons = lessons
            .Where(IsErrorHistoryLesson)
            .Take(5)
            .ToList();
        var generalLessons = lessons
            .Where(lesson => !IsErrorHistoryLesson(lesson))
            .ToList();
        var preferences = memory.Preferences.Where(IsUsefulPreference).ToList();
        var checks = memory.Checks.Where(IsUsefulCheck).ToList();
        var workspaceRules = memory.WorkspaceRules
            .Where(IsUsefulWorkspaceRule)
            .Where(rule => !hasQuery || TextMatchesQuery(rule, query) || IsGlobalSafetyRule(rule))
            .ToList();
        var contextFacts = SelectRelevantContextFacts(memory.ContextBank, query, 16);
        var verificationCommands = hasQuery
            ? memory.VerificationCommands
                .Where(command => TextMatchesQuery(command, query))
                .ToList()
            : memory.VerificationCommands;
        var projectHints = hasQuery
            ? memory.ProjectHints
                .Where(hint => TextMatchesQuery(hint, query))
                .ToList()
            : memory.ProjectHints;
        if (hasQuery)
        {
            preferences = preferences
                .Where(preference => TextMatchesQuery($"{preference.Key} {preference.Value}", query))
                .ToList();
            checks = checks
                .Where(check => TextMatchesQuery($"{check.Name} {check.Command} {check.When}", query))
                .ToList();
        }

        if (verificationCommands.Count == 0 &&
            projectHints.Count == 0 &&
            workspaceRules.Count == 0 &&
            generalLessons.Count == 0 &&
            errorHistoryLessons.Count == 0 &&
            preferences.Count == 0 &&
            checks.Count == 0 &&
            contextFacts.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Project memory:");
        builder.AppendLine($"Workspace: {memory.WorkspaceRoot}");

        if (verificationCommands.Count > 0)
        {
            builder.AppendLine("Known verification commands:");
            foreach (var command in verificationCommands)
            {
                builder.AppendLine($"- {command}");
            }
        }

        if (projectHints.Count > 0)
        {
            builder.AppendLine("Project hints:");
            foreach (var hint in projectHints)
            {
                builder.AppendLine($"- {hint}");
            }
        }

        if (workspaceRules.Count > 0)
        {
            builder.AppendLine("Workspace rules:");
            foreach (var rule in workspaceRules)
            {
                builder.AppendLine($"- {rule}");
            }
        }

        if (contextFacts.Count > 0)
        {
            builder.AppendLine("Context bank:");
            foreach (var entry in contextFacts)
            {
                builder.AppendLine($"- {entry.Category}: {entry.Fact.Key} = {entry.Fact.Value}");
            }
        }

        if (errorHistoryLessons.Count > 0)
        {
            builder.AppendLine("Previously seen failures:");
            foreach (var lesson in errorHistoryLessons)
            {
                var title = string.IsNullOrWhiteSpace(lesson.Title) ? lesson.Id : lesson.Title;
                var source = string.IsNullOrWhiteSpace(lesson.Source) ? "unknown source" : lesson.Source;
                builder.AppendLine($"- {title}: {lesson.Content} (source: {source}, confidence: {lesson.Confidence:0.##})");
            }
        }

        if (generalLessons.Count > 0)
        {
            builder.AppendLine("Learned lessons:");
            foreach (var lesson in generalLessons)
            {
                var title = string.IsNullOrWhiteSpace(lesson.Title) ? lesson.Id : lesson.Title;
                var source = string.IsNullOrWhiteSpace(lesson.Source) ? "unknown source" : lesson.Source;
                builder.AppendLine($"- {title}: {lesson.Content} (source: {source}, confidence: {lesson.Confidence:0.##})");
            }
        }

        if (preferences.Count > 0)
        {
            builder.AppendLine("User/project preferences:");
            foreach (var preference in preferences)
            {
                builder.AppendLine($"- {preference.Key}: {preference.Value}");
            }
        }

        if (checks.Count > 0)
        {
            builder.AppendLine("Remembered checks:");
            foreach (var check in checks)
            {
                var when = string.IsNullOrWhiteSpace(check.When) ? "manual" : check.When;
                builder.AppendLine($"- {check.Name} ({when}): {check.Command}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    public async Task AddLocalLessonAsync(string workspaceRoot, ProjectMemoryLesson lesson, CancellationToken ct)
    {
        if (!IsUsefulLesson(lesson))
        {
            return;
        }

        var root = Path.GetFullPath(workspaceRoot);
        var document = await LoadWorkspaceMemoryFileAsync(root, GetLocalMemoryPath(root), ct) ?? new ProjectMemoryFile();
        if (string.IsNullOrWhiteSpace(lesson.Id))
        {
            lesson.Id = CreateMemoryId(lesson.Title, lesson.Content);
        }

        lesson.CreatedAt = lesson.CreatedAt == default ? DateTime.Now : lesson.CreatedAt;
        lesson.Confidence = Math.Clamp(lesson.Confidence, 0, 1);
        lesson.Enabled = true;

        var duplicate = document.Lessons.FirstOrDefault(existing => LessonsMatch(existing, lesson));
        if (duplicate != null)
        {
            lesson.Id = string.IsNullOrWhiteSpace(duplicate.Id) ? lesson.Id : duplicate.Id;
            lesson.CreatedAt = duplicate.CreatedAt == default ? lesson.CreatedAt : duplicate.CreatedAt;
            lesson.LastUsedAt = duplicate.LastUsedAt ?? lesson.LastUsedAt;
            lesson.Confidence = Math.Max(duplicate.Confidence, lesson.Confidence);
            lesson.FailureFingerprint = string.IsNullOrWhiteSpace(lesson.FailureFingerprint)
                ? duplicate.FailureFingerprint
                : lesson.FailureFingerprint;
            lesson.Tags = (duplicate.Tags ?? []).Concat(lesson.Tags ?? [])
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (string.IsNullOrWhiteSpace(lesson.Source))
            {
                lesson.Source = duplicate.Source;
            }
        }

        document.Lessons.RemoveAll(existing =>
            string.Equals(existing.Id, lesson.Id, StringComparison.OrdinalIgnoreCase) ||
            LessonsMatch(existing, lesson));
        document.Lessons.Add(lesson);
        await SaveWorkspaceMemoryFileAsync(root, GetLocalMemoryPath(root), document, ct);
    }

    public async Task<IReadOnlyList<ProjectMemoryLesson>> LoadLocalLessonsAsync(string workspaceRoot, CancellationToken ct)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var document = await LoadWorkspaceMemoryFileAsync(root, GetLocalMemoryPath(root), ct);
        return document?.Lessons
            .OrderByDescending(lesson => lesson.LastUsedAt ?? lesson.CreatedAt)
            .ToList() ?? [];
    }

    public async Task<ProjectMemoryGcReport> PreviewLocalLessonGcAsync(
        string workspaceRoot,
        ProjectMemoryGcOptions? options,
        CancellationToken ct)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var document = await LoadWorkspaceMemoryFileAsync(root, GetLocalMemoryPath(root), ct);
        return _gcService.Preview(document?.Lessons ?? [], options);
    }

    public async Task<ProjectMemoryGcReport> CompactLocalLessonsAsync(
        string workspaceRoot,
        ProjectMemoryGcOptions? options,
        CancellationToken ct)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var path = GetLocalMemoryPath(root);
        var document = await LoadWorkspaceMemoryFileAsync(root, path, ct) ?? new ProjectMemoryFile();
        var report = _gcService.Apply(document.Lessons, options);
        if (report.RemovedCount > 0)
        {
            await SaveWorkspaceMemoryFileAsync(root, path, document, ct);
        }

        return report;
    }

    public async Task<bool> DisableLocalLessonAsync(string workspaceRoot, string lessonId, CancellationToken ct)
    {
        return await UpdateLocalLessonAsync(workspaceRoot, lessonId, lesson => lesson.Enabled = false, ct);
    }

    public async Task<bool> DeleteLocalLessonAsync(string workspaceRoot, string lessonId, CancellationToken ct)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var path = GetLocalMemoryPath(root);
        var document = await LoadWorkspaceMemoryFileAsync(root, path, ct);
        if (document == null)
        {
            return false;
        }

        var removed = document.Lessons.RemoveAll(lesson => string.Equals(lesson.Id, lessonId, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            return false;
        }

        await SaveWorkspaceMemoryFileAsync(root, path, document, ct);
        return true;
    }

    public async Task<IReadOnlyList<ProjectMemoryLesson>> TouchRelevantLocalLessonsAsync(
        string workspaceRoot,
        string query,
        CancellationToken ct,
        int maxCount = 12)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var path = GetLocalMemoryPath(root);
        var document = await LoadWorkspaceMemoryFileAsync(root, path, ct);
        if (document == null)
        {
            return [];
        }

        var queryTerms = ExtractTerms(query).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (queryTerms.Count == 0)
        {
            return [];
        }

        var touched = document.Lessons
            .Where(IsUsefulLesson)
            .Where(lesson => LessonMatchesQuery(lesson, queryTerms))
            .OrderByDescending(lesson => ScoreLesson(lesson, queryTerms))
            .ThenByDescending(lesson => lesson.Confidence)
            .Take(Math.Clamp(maxCount, 1, 50))
            .ToList();

        if (touched.Count == 0)
        {
            return [];
        }

        var now = DateTime.Now;
        foreach (var lesson in touched)
        {
            lesson.LastUsedAt = now;
        }

        await SaveWorkspaceMemoryFileAsync(root, path, document, ct);
        return touched;
    }

    private async Task<bool> UpdateLocalLessonAsync(
        string workspaceRoot,
        string lessonId,
        Action<ProjectMemoryLesson> update,
        CancellationToken ct)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var path = GetLocalMemoryPath(root);
        var document = await LoadWorkspaceMemoryFileAsync(root, path, ct);
        if (document == null)
        {
            return false;
        }

        var lesson = document.Lessons.FirstOrDefault(lesson => string.Equals(lesson.Id, lessonId, StringComparison.OrdinalIgnoreCase));
        if (lesson == null)
        {
            return false;
        }

        update(lesson);
        await SaveWorkspaceMemoryFileAsync(root, path, document, ct);
        return true;
    }

    public IReadOnlyList<ProjectMemoryLesson> SelectRelevantLessons(
        IEnumerable<ProjectMemoryLesson> lessons,
        string query,
        int maxCount = 12)
    {
        var queryTerms = ExtractTerms(query).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return lessons
            .Where(IsUsefulLesson)
            .Where(lesson => queryTerms.Count == 0 || LessonMatchesQuery(lesson, queryTerms))
            .Select(lesson => new
            {
                Lesson = lesson,
                Score = ScoreLesson(lesson, queryTerms)
            })
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Lesson.Confidence)
            .ThenByDescending(item => item.Lesson.LastUsedAt ?? item.Lesson.CreatedAt)
            .Take(Math.Clamp(maxCount, 1, 50))
            .Select(item => item.Lesson)
            .ToList();
    }

    private ProjectMemory Discover(string root)
    {
        var memory = new ProjectMemory { WorkspaceRoot = root };

        AddIfExists(memory.VerificationCommands, root, "build.desktop.cmd", "cmd /c build.desktop.cmd");
        AddIfExists(memory.VerificationCommands, root, "build.cmd", "cmd /c build.cmd");
        AddIfExists(memory.VerificationCommands, root, "test.cmd", "cmd /c test.cmd");

        ApplyLocalConfig(root, memory);

        if (Directory.Exists(Path.Combine(root, ".git")))
        {
            memory.ProjectHints.Add("Workspace is a Git repository.");
        }

        var sln = Directory.EnumerateFiles(root, "*.sln", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(root, "*.slnx", SearchOption.TopDirectoryOnly))
            .Select(Path.GetFileName)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(sln))
        {
            memory.ProjectHints.Add($"Solution file: {sln}");
        }

        return memory;
    }

    private static void ApplyLocalConfig(string root, ProjectMemory memory)
    {
        var config = ProjectAgentConfigService.LoadLocal(root);
        if (config != null)
        {
            AddUnique(memory.ProjectHints, "Project .agentq/config.json loaded.");
            AddUniqueRange(memory.VerificationCommands, config.VerificationCommands);
            AddUniqueRange(memory.WorkspaceRules, config.WorkspaceRules);
            if (!string.IsNullOrWhiteSpace(config.WorkMode))
            {
                AddUnique(memory.ProjectHints, $"Preferred work mode: {config.WorkMode}");
            }
        }
    }

    private static async Task ApplyWorkspaceMemoryAsync(string root, ProjectMemory memory, CancellationToken ct)
    {
        var shared = await LoadWorkspaceMemoryFileAsync(root, GetSharedMemoryPath(root), ct);
        if (shared != null)
        {
            ApplyWorkspaceMemoryFile(memory, shared, replaceExisting: false);
            AddUnique(memory.ProjectHints, "Project .agentq/memory.shared.json loaded.");
        }

        var local = await LoadWorkspaceMemoryFileAsync(root, GetLocalMemoryPath(root), ct);
        if (local != null)
        {
            ApplyWorkspaceMemoryFile(memory, local, replaceExisting: true);
            AddUnique(memory.ProjectHints, "Project .agentq/memory.local.json loaded.");
        }
    }

    private static void ApplyWorkspaceMemoryFile(ProjectMemory memory, ProjectMemoryFile file, bool replaceExisting)
    {
        AddUniqueRange(memory.ProjectHints, file.ProjectHints);
        AddUniqueRange(memory.WorkspaceRules, file.WorkspaceRules);
        AddUniqueRange(memory.VerificationCommands, file.VerificationCommands);

        foreach (var lesson in (file.Lessons ?? []).Where(IsUsefulLesson))
        {
            AddOrReplace(
                memory.Lessons,
                lesson,
                existing => string.Equals(existing.Id, lesson.Id, StringComparison.OrdinalIgnoreCase),
                replaceExisting);
        }

        foreach (var preference in (file.Preferences ?? []).Where(IsUsefulPreference))
        {
            AddOrReplace(
                memory.Preferences,
                preference,
                existing => string.Equals(existing.Key, preference.Key, StringComparison.OrdinalIgnoreCase),
                replaceExisting);
        }

        foreach (var check in (file.Checks ?? []).Where(IsUsefulCheck))
        {
            AddOrReplace(
                memory.Checks,
                check,
                existing => string.Equals(existing.Name, check.Name, StringComparison.OrdinalIgnoreCase),
                replaceExisting);
        }

        if (file.ContextBank != null)
        {
            ApplyContextBank(memory.ContextBank, file.ContextBank, replaceExisting);
        }
    }

    private static void ApplyContextBank(ProjectContextBank target, ProjectContextBank source, bool replaceExisting)
    {
        AddUniqueFacts(target.Stack, source.Stack, replaceExisting);
        AddUniqueFacts(target.Rules, source.Rules, replaceExisting);
        AddUniqueFacts(target.Preferences, source.Preferences, replaceExisting);
        AddUniqueFacts(target.ForbiddenPatterns, source.ForbiddenPatterns, replaceExisting);
        AddUniqueFacts(target.KeyCommands, source.KeyCommands, replaceExisting);
        AddUniqueFacts(target.KeyFiles, source.KeyFiles, replaceExisting);
        AddUniqueFacts(target.KeySymbols, source.KeySymbols, replaceExisting);
        AddUniqueFacts(target.RecurringErrors, source.RecurringErrors, replaceExisting);
    }

    private static void AddUniqueFacts(
        List<ProjectMemoryFact> target,
        IEnumerable<ProjectMemoryFact> additions,
        bool replaceExisting)
    {
        foreach (var fact in (additions ?? []).Where(IsUsefulFact))
        {
            AddOrReplace(target, fact, existing =>
                string.Equals(existing.Key, fact.Key, StringComparison.OrdinalIgnoreCase) &&
                (replaceExisting ||
                 string.Equals(existing.Value, fact.Value, StringComparison.OrdinalIgnoreCase)),
                replaceExisting);
        }
    }

    private async Task EnrichContextBankAsync(string root, ProjectMemory memory, CancellationToken ct)
    {
        WorkspaceAnalysis analysis;
        try
        {
            analysis = await _workspaceAnalysisService.AnalyzeAsync(root, ct);
        }
        catch
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(analysis.ProjectType) && analysis.ProjectType != "Unknown")
        {
            AddFact(memory.ContextBank.Stack, "project-type", analysis.ProjectType, "workspace-analysis");
        }

        if (!string.IsNullOrWhiteSpace(analysis.Framework) && analysis.Framework != "Unknown")
        {
            AddFact(memory.ContextBank.Stack, "framework", analysis.Framework, "workspace-analysis");
        }

        foreach (var command in analysis.VerificationCommands.Take(8))
        {
            AddFact(memory.ContextBank.KeyCommands, command, command, "workspace-analysis", ["verification"]);
        }

        foreach (var file in analysis.KeyFiles.Take(12))
        {
            AddFact(memory.ContextBank.KeyFiles, file, file, "workspace-analysis");
        }

        foreach (var symbol in analysis.KeySymbols.Take(12))
        {
            AddFact(memory.ContextBank.KeySymbols, symbol, symbol, "workspace-analysis");
        }

        foreach (var rule in memory.WorkspaceRules.Take(12))
        {
            AddFact(memory.ContextBank.Rules, "workspace-rule", rule, "workspace-memory");
        }

        foreach (var preference in memory.Preferences.Where(IsUsefulPreference).Take(12))
        {
            AddFact(memory.ContextBank.Preferences, preference.Key, preference.Value, "workspace-memory");
        }

        foreach (var lesson in memory.Lessons.Where(IsErrorHistoryLesson).Take(12))
        {
            AddFact(memory.ContextBank.RecurringErrors, string.IsNullOrWhiteSpace(lesson.Title) ? lesson.Id : lesson.Title, lesson.Content, "workspace-memory", lesson.Tags);
        }
    }

    private static void AddFact(
        List<ProjectMemoryFact> facts,
        string key,
        string value,
        string source,
        IEnumerable<string>? tags = null)
    {
        var fact = new ProjectMemoryFact
        {
            Key = key,
            Value = value,
            Source = source,
            Tags = tags?.Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? []
        };

        if (!IsUsefulFact(fact))
        {
            return;
        }

        AddUnique(facts, fact, existing =>
            string.Equals(existing.Key, fact.Key, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.Value, fact.Value, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<ProjectMemoryFile?> LoadWorkspaceMemoryFileAsync(string workspaceRoot, string path, CancellationToken ct)
    {
        if (!WorkspacePathResolver.IsResolvedInsideWorkspace(workspaceRoot, path) ||
            !File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            return JsonSerializer.Deserialize<ProjectMemoryFile>(json, Options);
        }
        catch
        {
            return null;
        }
    }

    private static async Task SaveWorkspaceMemoryFileAsync(string workspaceRoot, string path, ProjectMemoryFile document, CancellationToken ct)
    {
        if (!WorkspacePathResolver.IsResolvedInsideWorkspace(workspaceRoot, path))
        {
            throw new InvalidOperationException("Project memory path resolves outside the workspace.");
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        document.UpdatedAt = DateTime.Now;
        var tempPath = Path.Combine(directory ?? Environment.CurrentDirectory, $"{Guid.NewGuid():N}.tmp");
        var json = JsonSerializer.Serialize(document, Options);

        try
        {
            await File.WriteAllTextAsync(tempPath, json, ct);
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static void AddIfExists(List<string> commands, string root, string fileName, string command)
    {
        if (File.Exists(Path.Combine(root, fileName)))
        {
            AddUnique(commands, command);
        }
    }

    private static void AddUniqueRange(List<string> values, IEnumerable<string>? additions)
    {
        if (additions == null)
        {
            return;
        }

        foreach (var addition in additions)
        {
            AddUnique(values, addition);
        }
    }

    private static void AddUnique(List<string> values, string value)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            !LooksSensitive(value) &&
            !values.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(value);
        }
    }

    private static void AddUnique<T>(List<T> values, T value, Func<T, bool> exists)
    {
        if (!values.Any(exists))
        {
            values.Add(value);
        }
    }

    private static void AddOrReplace<T>(
        List<T> values,
        T value,
        Func<T, bool> matches,
        bool replaceExisting)
    {
        var index = values.FindIndex(item => matches(item));
        if (index < 0)
        {
            values.Add(value);
            return;
        }

        if (replaceExisting)
        {
            values[index] = value;
        }
    }

    private static bool IsUsefulLesson(ProjectMemoryLesson lesson)
    {
        var title = lesson.Title ?? string.Empty;
        var content = lesson.Content ?? string.Empty;
        var source = lesson.Source ?? string.Empty;
        IEnumerable<string> tags = lesson.Tags ?? [];

        return lesson.Enabled &&
               !IsExpired(lesson.ExpiresAt) &&
               !string.IsNullOrWhiteSpace(content) &&
               lesson.Confidence >= MinUsefulLessonConfidence &&
               !IsStaleUnusedLesson(lesson) &&
               content.Length <= MaxMemoryTextLength &&
               title.Length <= 180 &&
               !LooksLikeOffTargetAssistantAdvice($"{title} {content} {string.Join(' ', tags)}") &&
               !LooksSensitive(content) &&
               !LooksSensitive(title) &&
               !LooksSensitive(source);
    }

    private static bool IsUsefulPreference(ProjectMemoryPreference preference)
    {
        var key = preference.Key ?? string.Empty;
        var value = preference.Value ?? string.Empty;

        return preference.Enabled &&
               !string.IsNullOrWhiteSpace(key) &&
               !string.IsNullOrWhiteSpace(value) &&
               key.Length <= 120 &&
               value.Length <= MaxMemoryTextLength &&
               !LooksSensitive(key) &&
               !LooksSensitive(value);
    }

    private static bool IsUsefulCheck(ProjectMemoryCheck check)
    {
        var name = check.Name ?? string.Empty;
        var command = check.Command ?? string.Empty;

        return check.Enabled &&
               !string.IsNullOrWhiteSpace(name) &&
               !string.IsNullOrWhiteSpace(command) &&
               name.Length <= 160 &&
               command.Length <= MaxMemoryCommandLength &&
               !LooksSensitive(command) &&
               !LooksDangerousCommand(command);
    }

    private static bool IsUsefulFact(ProjectMemoryFact fact)
    {
        var key = fact.Key ?? string.Empty;
        var value = fact.Value ?? string.Empty;
        var source = fact.Source ?? string.Empty;
        IEnumerable<string> tags = fact.Tags ?? [];

        return fact.Enabled &&
               !string.IsNullOrWhiteSpace(key) &&
               !string.IsNullOrWhiteSpace(value) &&
               key.Length <= 180 &&
               value.Length <= MaxContextBankFactLength &&
               fact.Confidence >= MinUsefulLessonConfidence &&
               !LooksLikeOffTargetAssistantAdvice($"{key} {value} {string.Join(' ', tags)}") &&
               !LooksSensitive(key) &&
               !LooksSensitive(value) &&
               !LooksSensitive(source);
    }

    private static bool IsUsefulWorkspaceRule(string rule)
    {
        rule = rule?.Trim() ?? string.Empty;
        return rule.Length is > 0 and <= MaxMemoryTextLength &&
               (!LooksSensitive(rule) || IsGlobalSafetyRule(rule)) &&
               !ContainsSensitiveCredentialValue(rule) &&
               !LooksLikeOffTargetAssistantAdvice(rule);
    }

    private static bool ContainsSensitiveCredentialValue(string value)
    {
        return Regex.IsMatch(value, @"sk-[A-Za-z0-9_-]{12,}", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(value, @"bearer\s+[A-Za-z0-9._-]{12,}", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(value, @"(?i)(api[-_\s]?key|access[-_\s]?token|refresh[-_\s]?token|private[-_\s]?key|password|secret)\s*[:=]\s*\S+") ||
               Regex.IsMatch(value, @"postgres(?:ql)?://[^@\s]+:[^@\s]+@", RegexOptions.IgnoreCase);
    }

    private static bool IsGlobalSafetyRule(string rule)
    {
        var lower = rule.ToLowerInvariant();
        return new[]
            {
                "secret",
                "password",
                "token",
                "credential",
                "api key",
                "api-key",
                "do not store",
                "do not commit",
                "workspace path",
                "permission",
                "approval",
                "destructive",
                "symlink",
                "reparse",
                "\uBE44\uBC00",
                "\uD1A0\uD070",
                "\uC2B9\uC778",
                "\uAD8C\uD55C"
            }
            .Any(term => lower.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsExpired(DateTime? expiresAt) =>
        expiresAt.HasValue && expiresAt.Value <= DateTime.Now;

    private static bool IsStaleUnusedLesson(ProjectMemoryLesson lesson)
    {
        var lastRelevantAt = lesson.LastUsedAt ?? lesson.CreatedAt;
        return lastRelevantAt != default && lastRelevantAt <= DateTime.Now.AddDays(-StaleUnusedLessonDays);
    }

    private static bool LooksSensitive(string value)
    {
        return Regex.IsMatch(value, @"sk-[A-Za-z0-9_-]{12,}", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(value, @"bearer\s+[A-Za-z0-9._-]{12,}", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(value, @"api[-_\s]?key", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(value, @"access[-_\s]?token", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(value, @"refresh[-_\s]?token", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(value, @"private[-_\s]?key", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(value, @"database[-_\s]?url", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(value, @"postgres(?:ql)?://[^@\s]+:[^@\s]+@", RegexOptions.IgnoreCase) ||
               value.Contains("api_key", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("password", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("token", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeOffTargetAssistantAdvice(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var lower = value.ToLowerInvariant();
        var mentionsReading = lower.Contains("\uB3C5\uC11C", StringComparison.OrdinalIgnoreCase) ||
                              lower.Contains("reading", StringComparison.OrdinalIgnoreCase);
        var mentionsGames = lower.Contains("\uAC8C\uC784", StringComparison.OrdinalIgnoreCase) ||
                            lower.Contains("game", StringComparison.OrdinalIgnoreCase) ||
                            lower.Contains("games", StringComparison.OrdinalIgnoreCase);
        if (!mentionsReading || !mentionsGames)
        {
            return false;
        }

        return lower.Contains("\uC778\uACF5\uC9C0\uB2A5", StringComparison.OrdinalIgnoreCase) ||
               lower.Contains("\uC990\uAE30", StringComparison.OrdinalIgnoreCase) ||
               lower.Contains("\uCDE8\uBBF8", StringComparison.OrdinalIgnoreCase) ||
               lower.Contains("\uC0C1\uC0C1\uB825", StringComparison.OrdinalIgnoreCase) ||
               lower.Contains("\uC804\uB7B5", StringComparison.OrdinalIgnoreCase) ||
               lower.Contains("hobb", StringComparison.OrdinalIgnoreCase) ||
               lower.Contains("advice", StringComparison.OrdinalIgnoreCase) ||
               lower.Contains("imagination", StringComparison.OrdinalIgnoreCase) ||
               lower.Contains("strategy", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksDangerousCommand(string value)
    {
        return Regex.IsMatch(value, @"\brm\s+-rf\b", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(value, @"\bRemove-Item\b.*\b-Recurse\b", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(value, @"\bdel\s+/[sq]\b", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(value, @"\bformat\s+[A-Z]:", RegexOptions.IgnoreCase) ||
               value.Contains("git reset --hard", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateMemoryId(string title, string content)
    {
        var seed = string.IsNullOrWhiteSpace(title) ? content : title;
        var normalized = seed.Trim().ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return $"lesson-{hash[..12]}";
    }

    private static bool LessonsMatch(ProjectMemoryLesson left, ProjectMemoryLesson right)
    {
        if (!string.IsNullOrWhiteSpace(left.Id) &&
            !string.IsNullOrWhiteSpace(right.Id) &&
            string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(left.FailureFingerprint) &&
            !string.IsNullOrWhiteSpace(right.FailureFingerprint) &&
            string.Equals(left.FailureFingerprint, right.FailureFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(CreateLessonFingerprint(left), CreateLessonFingerprint(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateLessonFingerprint(ProjectMemoryLesson lesson)
    {
        var title = lesson.Title ?? string.Empty;
        var content = lesson.Content ?? string.Empty;
        var seed = string.IsNullOrWhiteSpace(title)
            ? content
            : $"{title}\n{content}";
        return Regex.Replace(seed.Trim().ToLowerInvariant(), @"\s+", " ");
    }

    private static double ScoreLesson(ProjectMemoryLesson lesson, IReadOnlySet<string> queryTerms)
    {
        var score = Math.Clamp(lesson.Confidence, 0, 1);
        if (IsErrorHistoryLesson(lesson) && LooksLikeFailureQuery(queryTerms))
        {
            score += 2;
        }

        if (queryTerms.Count == 0)
        {
            return score;
        }

        foreach (var term in ExtractTerms(CreateLessonSearchText(lesson)))
        {
            if (queryTerms.Contains(term))
            {
                score += 1;
            }
        }

        return score;
    }

    private static bool IsErrorHistoryLesson(ProjectMemoryLesson lesson) =>
        (lesson.Tags ?? []).Any(tag => tag.Equals("error-history", StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeFailureQuery(IReadOnlySet<string> queryTerms)
    {
        var failureTerms = new[]
        {
            "error",
            "failed",
            "failure",
            "exception",
            "build",
            "test",
            "verification",
            "provider",
            "model",
            "embedding",
            "timeout",
            "cancelled",
            "denied",
            "blocked",
            "400",
            "404"
        };

        return failureTerms.Any(queryTerms.Contains);
    }

    private static bool LessonMatchesQuery(ProjectMemoryLesson lesson, IReadOnlySet<string> queryTerms)
    {
        if (queryTerms.Count == 0)
        {
            return false;
        }

        return ExtractTerms(CreateLessonSearchText(lesson))
            .Any(queryTerms.Contains);
    }

    private static IReadOnlyList<ContextBankEntry> SelectRelevantContextFacts(
        ProjectContextBank contextBank,
        string query,
        int maxCount)
    {
        var queryTerms = ExtractTerms(query).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return EnumerateContextFacts(contextBank)
            .Where(entry => IsUsefulFact(entry.Fact))
            .Select(entry => new
            {
                Entry = entry,
                Score = ScoreFact(entry, queryTerms)
            })
            .Where(item => queryTerms.Count == 0 || item.Score > item.Entry.Fact.Confidence)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Entry.Category, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(maxCount, 1, 50))
            .Select(item => item.Entry)
            .ToList();
    }

    private static IEnumerable<ContextBankEntry> EnumerateContextFacts(ProjectContextBank contextBank)
    {
        foreach (var fact in contextBank.Stack ?? [])
        {
            yield return new ContextBankEntry("stack", fact);
        }

        foreach (var fact in contextBank.Rules ?? [])
        {
            yield return new ContextBankEntry("rule", fact);
        }

        foreach (var fact in contextBank.Preferences ?? [])
        {
            yield return new ContextBankEntry("preference", fact);
        }

        foreach (var fact in contextBank.ForbiddenPatterns ?? [])
        {
            yield return new ContextBankEntry("forbidden", fact);
        }

        foreach (var fact in contextBank.KeyCommands ?? [])
        {
            yield return new ContextBankEntry("command", fact);
        }

        foreach (var fact in contextBank.KeyFiles ?? [])
        {
            yield return new ContextBankEntry("file", fact);
        }

        foreach (var fact in contextBank.KeySymbols ?? [])
        {
            yield return new ContextBankEntry("symbol", fact);
        }

        foreach (var fact in contextBank.RecurringErrors ?? [])
        {
            yield return new ContextBankEntry("recurring-error", fact);
        }
    }

    private static double ScoreFact(ContextBankEntry entry, IReadOnlySet<string> queryTerms)
    {
        var score = Math.Clamp(entry.Fact.Confidence, 0, 1);
        if (queryTerms.Count == 0 && (entry.Category is "rule" or "forbidden"))
        {
            score += 0.25;
        }

        if (entry.Category == "recurring-error" && LooksLikeFailureQuery(queryTerms))
        {
            score += 2;
        }

        if (queryTerms.Count == 0)
        {
            return score;
        }

        foreach (var term in ExtractTerms(CreateFactSearchText(entry)))
        {
            if (queryTerms.Contains(term))
            {
                score += 1;
            }
        }

        return score;
    }

    private static IEnumerable<string> ExtractTerms(string value)
    {
        foreach (Match match in Regex.Matches(value, @"[\p{L}\p{N}_\.-]{3,}", RegexOptions.IgnoreCase))
        {
            yield return match.Value.ToLowerInvariant();
        }
    }

    private static string CreateLessonSearchText(ProjectMemoryLesson lesson)
    {
        IEnumerable<string> tags = lesson.Tags ?? [];
        return $"{lesson.Title ?? string.Empty} {lesson.Content ?? string.Empty} {string.Join(' ', tags)}";
    }

    private static string CreateFactSearchText(ContextBankEntry entry)
    {
        var fact = entry.Fact;
        IEnumerable<string> tags = fact.Tags ?? [];
        return $"{entry.Category} {fact.Key ?? string.Empty} {fact.Value ?? string.Empty} {string.Join(' ', tags)} {fact.Source ?? string.Empty}";
    }

    private static bool TextMatchesQuery(string text, string query)
    {
        var queryTerms = ExtractTerms(query).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (queryTerms.Count == 0)
        {
            return true;
        }

        return ExtractTerms(text).Any(queryTerms.Contains);
    }

    private static string GetSharedMemoryPath(string workspaceRoot) =>
        Path.Combine(workspaceRoot, WorkspaceMemoryDirectoryName, SharedMemoryFileName);

    private static string GetLocalMemoryPath(string workspaceRoot) =>
        Path.Combine(workspaceRoot, WorkspaceMemoryDirectoryName, LocalMemoryFileName);

    private string GetMemoryPath(string workspaceRoot)
    {
        var normalized = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return Path.Combine(_memoryDirectory, $"{hash}.json");
    }

    private sealed class ProjectMemoryFile
    {
        public int Version { get; set; } = 1;

        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public List<string> VerificationCommands { get; set; } = [];

        public List<string> ProjectHints { get; set; } = [];

        public List<string> WorkspaceRules { get; set; } = [];

        public List<ProjectMemoryLesson> Lessons { get; set; } = [];

        public List<ProjectMemoryPreference> Preferences { get; set; } = [];

        public List<ProjectMemoryCheck> Checks { get; set; } = [];

        public ProjectContextBank ContextBank { get; set; } = new();
    }

    private sealed record ContextBankEntry(string Category, ProjectMemoryFact Fact);
}
