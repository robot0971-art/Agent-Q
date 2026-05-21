using System.IO;

namespace AgentQ.Desktop.Services;

public sealed class EmbeddingIndexPaths
{
    public const string AgentQDirectoryName = ".agentq";
    public const string EmbeddingsDirectoryName = "embeddings";
    public const string IndexFileName = "index.json";
    public const string ChunksFileName = "chunks.jsonl";

    public required string WorkspaceRoot { get; init; }

    public required string AgentQDirectory { get; init; }

    public required string EmbeddingsDirectory { get; init; }

    public required string IndexPath { get; init; }

    public required string ChunksPath { get; init; }

    public static EmbeddingIndexPaths ForWorkspace(string workspaceRoot)
    {
        var root = string.IsNullOrWhiteSpace(workspaceRoot)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(workspaceRoot);
        var agentQDirectory = Path.Combine(root, AgentQDirectoryName);
        var embeddingsDirectory = Path.Combine(agentQDirectory, EmbeddingsDirectoryName);

        return new EmbeddingIndexPaths
        {
            WorkspaceRoot = root,
            AgentQDirectory = agentQDirectory,
            EmbeddingsDirectory = embeddingsDirectory,
            IndexPath = Path.Combine(embeddingsDirectory, IndexFileName),
            ChunksPath = Path.Combine(embeddingsDirectory, ChunksFileName)
        };
    }
}
