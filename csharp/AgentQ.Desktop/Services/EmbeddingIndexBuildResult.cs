namespace AgentQ.Desktop.Services;

public sealed class EmbeddingIndexBuildResult
{
    public required EmbeddingIndexManifest Manifest { get; init; }

    public required EmbeddingIndexPaths Paths { get; init; }

    public IReadOnlyList<EmbeddingIndexChunk> Chunks { get; init; } = [];
}
