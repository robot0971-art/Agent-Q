using System.IO;
using System.Text.Json;

namespace AgentQ.Desktop.Services;

public sealed class ToolReplayService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<string?> SaveAsync(ToolReplaySession session, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(session.WorkspaceRoot) || session.Entries.Count == 0)
        {
            return null;
        }

        session.WorkspaceRoot = Path.GetFullPath(session.WorkspaceRoot);
        session.CreatedAt = DateTime.Now;

        var directory = Path.Combine(session.WorkspaceRoot, ".agentq", "replay");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{session.CreatedAt:yyyyMMdd-HHmmss-fff}-{session.Id}.json");
        var tempPath = Path.Combine(directory, $"{Guid.NewGuid():N}.tmp");
        var json = JsonSerializer.Serialize(session, Options);

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

    public async Task<ToolReplaySession?> LoadLatestAsync(string workspaceRoot, CancellationToken ct = default)
    {
        var directory = Path.Combine(Path.GetFullPath(workspaceRoot), ".agentq", "replay");
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

        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<ToolReplaySession>(json, Options);
    }
}
