using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgentQ.Desktop.Services;

public sealed class AgentSessionSummaryService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _summaryRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".agentq",
        "session-memory");

    public async Task SaveAsync(AgentSessionSummary summary, CancellationToken ct = default)
    {
        summary.WorkspaceRoot = Path.GetFullPath(summary.WorkspaceRoot);
        summary.CreatedAt = DateTime.Now;

        var directory = GetWorkspaceDirectory(summary.WorkspaceRoot);
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(summary, Options);
        var historyPath = Path.Combine(directory, $"{summary.CreatedAt:yyyyMMdd-HHmmss-fff}-{summary.Id}.json");
        var latestPath = Path.Combine(directory, "latest.json");
        var tempPath = Path.Combine(directory, $"{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(tempPath, json, ct);
            File.Move(tempPath, historyPath, overwrite: true);
            await File.WriteAllTextAsync(latestPath, json, ct);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public async Task<AgentSessionSummary?> LoadLatestAsync(string workspaceRoot, CancellationToken ct = default)
    {
        var latestPath = Path.Combine(GetWorkspaceDirectory(Path.GetFullPath(workspaceRoot)), "latest.json");
        if (!File.Exists(latestPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(latestPath, ct);
            return JsonSerializer.Deserialize<AgentSessionSummary>(json, Options);
        }
        catch
        {
            return null;
        }
    }

    private string GetWorkspaceDirectory(string workspaceRoot)
    {
        var normalized = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return Path.Combine(_summaryRoot, hash);
    }
}
