using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentQ.Api;

namespace AgentQ.Desktop.Services;

public sealed class FileMutationSnapshotService
{
    private static readonly JsonSerializerOptions Options = AgentQJsonOptions.CaseInsensitiveIndented;

    public async Task<string> SaveAsync(FileMutationSnapshot snapshot, CancellationToken ct = default)
    {
        snapshot.WorkspaceRoot = Path.GetFullPath(snapshot.WorkspaceRoot);
        snapshot.CreatedAt = DateTime.Now;

        var directory = GetSnapshotDirectory(snapshot.WorkspaceRoot);
        if (!WorkspacePathResolver.IsResolvedInsideWorkspace(snapshot.WorkspaceRoot, directory))
        {
            throw new InvalidOperationException("File mutation snapshot path resolves outside the workspace.");
        }

        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{snapshot.CreatedAt:yyyyMMdd-HHmmss-fff}-{snapshot.Id}.json");
        var tempPath = Path.Combine(directory, $"{Guid.NewGuid():N}.tmp");
        var json = JsonSerializer.Serialize(snapshot, Options);

        try
        {
            await File.WriteAllTextAsync(tempPath, json, ct);
            File.Move(tempPath, path, overwrite: true);
            return path;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static string GetSnapshotDirectory(string workspaceRoot)
    {
        var normalized = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return Path.Combine(normalized, ".agentq", "snapshots", hash);
    }
}
