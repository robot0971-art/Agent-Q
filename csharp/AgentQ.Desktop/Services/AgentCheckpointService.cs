using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentQ.Api;

namespace AgentQ.Desktop.Services;

public sealed class AgentCheckpointService
{
    private static readonly JsonSerializerOptions Options = AgentQJsonOptions.CaseInsensitiveIndented;

    private readonly string _checkpointRoot;

    public AgentCheckpointService(string? checkpointRoot = null)
    {
        _checkpointRoot = string.IsNullOrWhiteSpace(checkpointRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".agentq",
                "checkpoints")
            : Path.GetFullPath(checkpointRoot);
    }

    public async Task SaveAsync(AgentCheckpoint checkpoint, CancellationToken ct = default)
    {
        checkpoint.WorkspaceRoot = Path.GetFullPath(checkpoint.WorkspaceRoot);
        checkpoint.CreatedAt = DateTime.Now;
        Directory.CreateDirectory(GetWorkspaceDirectory(checkpoint.WorkspaceRoot));

        var path = GetCheckpointPath(checkpoint);
        var tempPath = Path.Combine(GetWorkspaceDirectory(checkpoint.WorkspaceRoot), $"{Guid.NewGuid():N}.tmp");
        var json = JsonSerializer.Serialize(checkpoint, Options);

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

    public async Task<AgentCheckpoint?> LoadLatestAsync(string workspaceRoot, CancellationToken ct = default)
    {
        var directory = GetWorkspaceDirectory(Path.GetFullPath(workspaceRoot));
        if (!Directory.Exists(directory))
        {
            return null;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                var json = await File.ReadAllTextAsync(path, ct);
                var checkpoint = JsonSerializer.Deserialize<AgentCheckpoint>(json, Options);
                if (checkpoint != null)
                {
                    return checkpoint;
                }
            }
            catch
            {
                // Keep looking for the previous valid checkpoint.
            }
        }

        return null;
    }

    private string GetCheckpointPath(AgentCheckpoint checkpoint)
    {
        var fileName = $"{checkpoint.CreatedAt:yyyyMMdd-HHmmss-fff}-{checkpoint.Id}.json";
        return Path.Combine(GetWorkspaceDirectory(checkpoint.WorkspaceRoot), fileName);
    }

    private string GetWorkspaceDirectory(string workspaceRoot)
    {
        var normalized = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return Path.Combine(_checkpointRoot, hash);
    }
}
