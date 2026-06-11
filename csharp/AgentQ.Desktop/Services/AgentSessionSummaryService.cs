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

    private readonly string _summaryRoot;

    public AgentSessionSummaryService(string? summaryRoot = null)
    {
        _summaryRoot = string.IsNullOrWhiteSpace(summaryRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".agentq",
                "session-memory")
            : Path.GetFullPath(summaryRoot);
    }

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
        var latestTempPath = Path.Combine(directory, $"{Guid.NewGuid():N}.latest.tmp");

        try
        {
            await File.WriteAllTextAsync(tempPath, json, ct);
            File.Move(tempPath, historyPath, overwrite: true);
            await File.WriteAllTextAsync(latestTempPath, json, ct);
            File.Move(latestTempPath, latestPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            if (File.Exists(latestTempPath))
            {
                File.Delete(latestTempPath);
            }
        }
    }

    public async Task<AgentSessionSummary?> LoadLatestAsync(string workspaceRoot, CancellationToken ct = default)
    {
        var directory = GetWorkspaceDirectory(Path.GetFullPath(workspaceRoot));
        if (!Directory.Exists(directory))
        {
            return null;
        }

        var latestPath = Path.Combine(directory, "latest.json");
        if (File.Exists(latestPath) &&
            await TryLoadAsync(latestPath, ct) is { } latest)
        {
            return latest;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                     .Where(path => !string.Equals(Path.GetFileName(path), "latest.json", StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            if (await TryLoadAsync(path, ct) is { } summary)
            {
                return summary;
            }
        }

        return null;
    }

    private static async Task<AgentSessionSummary?> TryLoadAsync(string path, CancellationToken ct)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
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
