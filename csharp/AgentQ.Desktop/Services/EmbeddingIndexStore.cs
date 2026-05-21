using System.IO;
using System.Text.Json;

namespace AgentQ.Desktop.Services;

public sealed class EmbeddingIndexStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions JsonLineOptions = new();

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

    public async Task SaveChunksAsync(string workspaceRoot, IEnumerable<EmbeddingIndexChunk> chunks, CancellationToken ct = default)
    {
        var paths = GetPaths(workspaceRoot);
        Directory.CreateDirectory(paths.EmbeddingsDirectory);
        await using var stream = File.Create(paths.ChunksPath);
        await using var writer = new StreamWriter(stream);

        foreach (var chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();
            var json = JsonSerializer.Serialize(chunk, JsonLineOptions);
            await writer.WriteLineAsync(json.AsMemory(), ct);
        }
    }

    public async Task<IReadOnlyList<EmbeddingIndexChunk>> LoadChunksAsync(string workspaceRoot, CancellationToken ct = default)
    {
        var paths = GetPaths(workspaceRoot);
        if (!File.Exists(paths.ChunksPath))
        {
            return [];
        }

        var chunks = new List<EmbeddingIndexChunk>();
        foreach (var line in await File.ReadAllLinesAsync(paths.ChunksPath, ct))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var chunk = JsonSerializer.Deserialize<EmbeddingIndexChunk>(line, JsonLineOptions);
                if (chunk != null)
                {
                    chunks.Add(chunk);
                }
            }
            catch
            {
                // Ignore malformed cache rows; the index can be rebuilt.
            }
        }

        return chunks;
    }
}
