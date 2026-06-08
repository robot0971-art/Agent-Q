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
        foreach (var entry in session.Entries)
        {
            entry.InputJson = SensitiveTextRedactor.Redact(entry.InputJson);
            entry.ResultPreview = SensitiveTextRedactor.Redact(entry.ResultPreview);
        }

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
        return (await LoadRecentAsync(workspaceRoot, maxSessions: 1, ct)).FirstOrDefault();
    }

    public async Task<IReadOnlyList<ToolReplaySession>> LoadRecentAsync(
        string workspaceRoot,
        int maxSessions = 10,
        CancellationToken ct = default)
    {
        var directory = Path.Combine(Path.GetFullPath(workspaceRoot), ".agentq", "replay");
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var paths = Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(Math.Max(1, maxSessions))
            .ToList();

        var sessions = new List<ToolReplaySession>();
        foreach (var path in paths)
        {
            try
            {
                var json = await File.ReadAllTextAsync(path, ct);
                var session = JsonSerializer.Deserialize<ToolReplaySession>(json, Options);
                if (session != null)
                {
                    sessions.Add(session);
                }
            }
            catch (JsonException)
            {
                // A partial replay file should not break the dashboard.
            }
        }

        return sessions;
    }
}
