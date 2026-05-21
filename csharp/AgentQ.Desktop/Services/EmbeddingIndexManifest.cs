namespace AgentQ.Desktop.Services;

public sealed class EmbeddingIndexManifest
{
    public int Version { get; set; } = 1;

    public string Provider { get; set; } = "openai";

    public string Model { get; set; } = "text-embedding-3-small";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int ChunkCount { get; set; }

    public int FileCount { get; set; }

    public string ChunksFile { get; set; } = EmbeddingIndexPaths.ChunksFileName;
}
