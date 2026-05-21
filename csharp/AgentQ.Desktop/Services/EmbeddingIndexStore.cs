using System.IO;
using System.Text.Json;

namespace AgentQ.Desktop.Services;

public sealed class EmbeddingIndexStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public EmbeddingIndexPaths GetPaths(string workspaceRoot) => EmbeddingIndexPaths.ForWorkspace(workspaceRoot);

    public void EnsureStorage(string workspaceRoot)
    {
        var paths = GetPaths(workspaceRoot);
        Directory.CreateDirectory(paths.EmbeddingsDirectory);
    }

    public async Task<EmbeddingIndexManifest?> LoadManifestAsync(string workspaceRoot, CancellationToken ct = default)
    {
        var paths = GetPaths(workspaceRoot);
        if (!File.Exists(paths.IndexPath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(paths.IndexPath);
            return await JsonSerializer.DeserializeAsync<EmbeddingIndexManifest>(stream, JsonOptions, ct);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveManifestAsync(string workspaceRoot, EmbeddingIndexManifest manifest, CancellationToken ct = default)
    {
        var paths = GetPaths(workspaceRoot);
        Directory.CreateDirectory(paths.EmbeddingsDirectory);
        manifest.UpdatedAt = DateTime.UtcNow;
        await using var stream = File.Create(paths.IndexPath);
        await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, ct);
    }
}
