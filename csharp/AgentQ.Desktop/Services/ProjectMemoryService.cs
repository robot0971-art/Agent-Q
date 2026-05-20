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

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _memoryDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".agentq",
        "project-memory");

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
                    return memory;
                }
            }
            catch
            {
                // Corrupt memory should not block a run; rediscover below.
            }
        }

        var discovered = Discover(root);
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

    public string BuildContext(ProjectMemory memory)
    {
        if (memory.VerificationCommands.Count == 0 &&
            memory.ProjectHints.Count == 0 &&
            memory.WorkspaceRules.Count == 0 &&
            memory.Lessons.Count == 0 &&
            memory.Preferences.Count == 0 &&
            memory.Checks.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Project memory:");
        builder.AppendLine($"Workspace: {memory.WorkspaceRoot}");

        if (memory.VerificationCommands.Count > 0)
        {
            builder.AppendLine("Known verification commands:");
            foreach (var command in memory.VerificationCommands)
            {
                builder.AppendLine($"- {command}");
            }
        }

        if (memory.ProjectHints.Count > 0)
        {
            builder.AppendLine("Project hints:");
            foreach (var hint in memory.ProjectHints)
            {
                builder.AppendLine($"- {hint}");
            }
        }

        if (memory.WorkspaceRules.Count > 0)
        {
            builder.AppendLine("Workspace rules:");
            foreach (var rule in memory.WorkspaceRules)
            {
                builder.AppendLine($"- {rule}");
            }
        }

        if (memory.Lessons.Count > 0)
        {
            builder.AppendLine("Learned lessons:");
            foreach (var lesson in memory.Lessons.OrderByDescending(lesson => lesson.Confidence).Take(12))
            {
                var title = string.IsNullOrWhiteSpace(lesson.Title) ? lesson.Id : lesson.Title;
                builder.AppendLine($"- {title}: {lesson.Content}");
            }
        }

        if (memory.Preferences.Count > 0)
        {
            builder.AppendLine("User/project preferences:");
            foreach (var preference in memory.Preferences)
            {
                builder.AppendLine($"- {preference.Key}: {preference.Value}");
            }
        }

        if (memory.Checks.Count > 0)
        {
            builder.AppendLine("Remembered checks:");
            foreach (var check in memory.Checks)
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
        var document = await LoadWorkspaceMemoryFileAsync(GetLocalMemoryPath(root), ct) ?? new ProjectMemoryFile();
        if (string.IsNullOrWhiteSpace(lesson.Id))
        {
            lesson.Id = CreateMemoryId(lesson.Title, lesson.Content);
        }

        lesson.CreatedAt = lesson.CreatedAt == default ? DateTime.Now : lesson.CreatedAt;
        document.Lessons.RemoveAll(existing => string.Equals(existing.Id, lesson.Id, StringComparison.OrdinalIgnoreCase));
        document.Lessons.Add(lesson);
        await SaveWorkspaceMemoryFileAsync(GetLocalMemoryPath(root), document, ct);
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
        var shared = await LoadWorkspaceMemoryFileAsync(GetSharedMemoryPath(root), ct);
        if (shared != null)
        {
            ApplyWorkspaceMemoryFile(memory, shared);
            AddUnique(memory.ProjectHints, "Project .agentq/memory.shared.json loaded.");
        }

        var local = await LoadWorkspaceMemoryFileAsync(GetLocalMemoryPath(root), ct);
        if (local != null)
        {
            ApplyWorkspaceMemoryFile(memory, local);
            AddUnique(memory.ProjectHints, "Project .agentq/memory.local.json loaded.");
        }
    }

    private static void ApplyWorkspaceMemoryFile(ProjectMemory memory, ProjectMemoryFile file)
    {
        AddUniqueRange(memory.ProjectHints, file.ProjectHints);
        AddUniqueRange(memory.WorkspaceRules, file.WorkspaceRules);
        AddUniqueRange(memory.VerificationCommands, file.VerificationCommands);

        foreach (var lesson in file.Lessons.Where(IsUsefulLesson))
        {
            AddUnique(memory.Lessons, lesson, existing => string.Equals(existing.Id, lesson.Id, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var preference in file.Preferences.Where(IsUsefulPreference))
        {
            AddUnique(memory.Preferences, preference, existing => string.Equals(existing.Key, preference.Key, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var check in file.Checks.Where(IsUsefulCheck))
        {
            AddUnique(memory.Checks, check, existing => string.Equals(existing.Name, check.Name, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static async Task<ProjectMemoryFile?> LoadWorkspaceMemoryFileAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
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

    private static async Task SaveWorkspaceMemoryFileAsync(string path, ProjectMemoryFile document, CancellationToken ct)
    {
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

    private static void AddUniqueRange(List<string> values, IEnumerable<string> additions)
    {
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

    private static bool IsUsefulLesson(ProjectMemoryLesson lesson) =>
        !string.IsNullOrWhiteSpace(lesson.Content) &&
        !LooksSensitive(lesson.Content) &&
        !LooksSensitive(lesson.Title);

    private static bool IsUsefulPreference(ProjectMemoryPreference preference) =>
        !string.IsNullOrWhiteSpace(preference.Key) &&
        !string.IsNullOrWhiteSpace(preference.Value) &&
        !LooksSensitive(preference.Value);

    private static bool IsUsefulCheck(ProjectMemoryCheck check) =>
        !string.IsNullOrWhiteSpace(check.Name) &&
        !string.IsNullOrWhiteSpace(check.Command) &&
        !LooksSensitive(check.Command);

    private static bool LooksSensitive(string value)
    {
        return Regex.IsMatch(value, @"sk-[A-Za-z0-9_-]{12,}", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(value, @"bearer\s+[A-Za-z0-9._-]{12,}", RegexOptions.IgnoreCase) ||
               value.Contains("api_key", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("token", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateMemoryId(string title, string content)
    {
        var seed = string.IsNullOrWhiteSpace(title) ? content : title;
        var normalized = seed.Trim().ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return $"lesson-{hash[..12]}";
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
    }
}
