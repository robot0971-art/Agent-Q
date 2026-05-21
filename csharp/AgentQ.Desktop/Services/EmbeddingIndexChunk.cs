namespace AgentQ.Desktop.Services;

public sealed class EmbeddingIndexChunk
{
    public required string Id { get; init; }

    public required string RelativePath { get; init; }

    public required string Content { get; init; }

    public int StartLine { get; init; }

    public int EndLine { get; init; }

    public string FileHash { get; init; } = string.Empty;

    public DateTime FileModifiedAt { get; init; }

    public string Extension { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public float[] Vector { get; set; } = [];
}
