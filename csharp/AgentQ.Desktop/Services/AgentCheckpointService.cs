using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgentQ.Desktop.Services;

public sealed class AgentCheckpointService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _checkpointRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".agentq",
        "checkpoints");

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

        var path = Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            return JsonSerializer.Deserialize<AgentCheckpoint>(json, Options);
        }
        catch
        {
            return null;
        }
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
