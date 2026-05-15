using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgentQ.Desktop.Services;

public sealed class ProjectMemoryService
{
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
            memory.WorkspaceRules.Count == 0)
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

        return builder.ToString().TrimEnd();
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
            !values.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(value);
        }
    }

    private string GetMemoryPath(string workspaceRoot)
    {
        var normalized = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return Path.Combine(_memoryDirectory, $"{hash}.json");
    }
}
