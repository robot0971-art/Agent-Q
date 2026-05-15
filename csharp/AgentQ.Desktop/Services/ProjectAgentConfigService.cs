using System.IO;
using System.Text.Json;

namespace AgentQ.Desktop.Services;

public sealed class ProjectAgentConfigService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<ProjectAgentConfig?> LoadAsync(string workspaceRoot, CancellationToken ct = default)
    {
        var path = GetConfigPath(workspaceRoot);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            return JsonSerializer.Deserialize<ProjectAgentConfig>(json, Options);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveAsync(string workspaceRoot, ProjectAgentConfig config, CancellationToken ct = default)
    {
        config.UpdatedAt = DateTime.Now;
        var path = GetConfigPath(workspaceRoot);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        var json = JsonSerializer.Serialize(config, Options);
        try
        {
            await File.WriteAllTextAsync(tempPath, json, ct);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public static ProjectAgentConfig? LoadLocal(string workspaceRoot)
    {
        var path = GetConfigPath(workspaceRoot);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ProjectAgentConfig>(json, Options);
        }
        catch
        {
            return null;
        }
    }

    public static string GetConfigPath(string workspaceRoot)
    {
        var root = string.IsNullOrWhiteSpace(workspaceRoot)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(workspaceRoot);
        return Path.Combine(root, ".agentq", "config.json");
    }
}
