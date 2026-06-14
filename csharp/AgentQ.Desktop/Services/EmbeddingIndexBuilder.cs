using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AgentQ.Desktop.Services;

public sealed class EmbeddingIndexBuilder(EmbeddingIndexStore store)
{
    private const int MaximumFileBytes = 512 * 1024;
    private const int MaximumChunkChars = 2200;
    private const int ChunkOverlapLines = 4;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".xaml", ".csproj", ".sln", ".props", ".targets",
        ".json", ".md", ".txt", ".xml", ".yml", ".yaml",
        ".ps1", ".cmd", ".bat", ".sh", ".js", ".ts", ".tsx", ".jsx",
        ".html", ".css", ".scss", ".py", ".go", ".rs", ".java", ".kt"
    };

    public async Task<EmbeddingIndexBuildResult> BuildTextChunkIndexAsync(
        string workspaceRoot,
        string provider = "openai",
        string model = "text-embedding-3-small",
        CancellationToken ct = default)
    {
        return await BuildIndexAsync(
            workspaceRoot,
            provider,
            model,
            embeddingClient: null,
            maximumEmbeddedChunks: 0,
            ct);
    }

    public async Task<EmbeddingIndexBuildResult> BuildVectorIndexAsync(
        string workspaceRoot,
        IEmbeddingClient embeddingClient,
        string provider = "openai",
        string model = "text-embedding-3-small",
        int maximumEmbeddedChunks = 100,
        CancellationToken ct = default)
    {
        return await BuildIndexAsync(
            workspaceRoot,
            provider,
            model,
            embeddingClient,
            maximumEmbeddedChunks,
            ct);
    }

    private async Task<EmbeddingIndexBuildResult> BuildIndexAsync(
        string workspaceRoot,
        string provider,
        string model,
        IEmbeddingClient? embeddingClient,
        int maximumEmbeddedChunks,
        CancellationToken ct)
    {
        var paths = store.GetPaths(workspaceRoot);
        var root = paths.WorkspaceRoot;
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Workspace not found: {workspaceRoot}");
        }

        store.EnsureStorage(root);

        var chunks = new List<EmbeddingIndexChunk>();
        var files = SafeEnumerateFiles(root)
            .Where(file => WorkspacePathResolver.IsResolvedInsideWorkspace(root, file))
            .Where(file => IsIndexableFile(root, file))
            .OrderBy(file => Path.GetRelativePath(root, file), StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var fileChunks = await BuildChunksForFileAsync(root, file, model, ct);
            chunks.AddRange(fileChunks);
        }

        if (embeddingClient != null && maximumEmbeddedChunks > 0 && chunks.Count > 0)
        {
            await FillVectorsAsync(chunks, embeddingClient, model, maximumEmbeddedChunks, ct);
        }

        await store.SaveChunksAsync(root, chunks, ct);

        var manifest = new EmbeddingIndexManifest
        {
            Provider = provider,
            Model = model,
            FileCount = files.Count,
            ChunkCount = chunks.Count
        };

        await store.SaveManifestAsync(root, manifest, ct);

        return new EmbeddingIndexBuildResult
        {
            Manifest = manifest,
            Paths = paths,
            Chunks = chunks
        };
    }

    private static async Task FillVectorsAsync(
        List<EmbeddingIndexChunk> chunks,
        IEmbeddingClient embeddingClient,
        string model,
        int maximumEmbeddedChunks,
        CancellationToken ct)
    {
        const int batchSize = 32;
        var limitedChunks = chunks.Take(maximumEmbeddedChunks).ToList();

        for (var offset = 0; offset < limitedChunks.Count; offset += batchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = limitedChunks.Skip(offset).Take(batchSize).ToList();
            var vectors = await embeddingClient.CreateEmbeddingsAsync(batch.Select(chunk => chunk.Content).ToList(), model, ct);
            if (vectors.Count != batch.Count)
            {
                throw new InvalidOperationException($"Embedding response count mismatch. Expected {batch.Count}, got {vectors.Count}.");
            }

            for (var i = 0; i < batch.Count; i++)
            {
                batch[i].Vector = vectors[i];
            }
        }
    }

    private static async Task<IReadOnlyList<EmbeddingIndexChunk>> BuildChunksForFileAsync(
        string root,
        string file,
        string model,
        CancellationToken ct)
    {
        var text = await ReadTextFileAsync(file, ct);
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var relativePath = Path.GetRelativePath(root, file).Replace('\\', '/');
        var fileHash = ComputeSha256(text);
        var modifiedAt = File.GetLastWriteTimeUtc(file);
        var extension = Path.GetExtension(file);
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var chunks = new List<EmbeddingIndexChunk>();
        var chunkIndex = 0;
        var startLineIndex = 0;

        while (startLineIndex < lines.Length)
        {
            var builder = new StringBuilder();
            var endLineIndex = startLineIndex;

            while (endLineIndex < lines.Length)
            {
                var nextLine = lines[endLineIndex];
                var extraLength = nextLine.Length + 1;
                if (builder.Length > 0 && builder.Length + extraLength > MaximumChunkChars)
                {
                    break;
                }

                builder.AppendLine(nextLine);
                endLineIndex++;
            }

            var content = builder.ToString().TrimEnd();
            if (!string.IsNullOrWhiteSpace(content))
            {
                chunks.Add(new EmbeddingIndexChunk
                {
                    Id = $"{relativePath}:{startLineIndex + 1}-{endLineIndex}:{chunkIndex}",
                    RelativePath = relativePath,
                    Content = content,
                    StartLine = startLineIndex + 1,
                    EndLine = endLineIndex,
                    FileHash = fileHash,
                    FileModifiedAt = modifiedAt,
                    Extension = extension,
                    Model = model
                });
                chunkIndex++;
            }

            if (endLineIndex >= lines.Length)
            {
                break;
            }

            startLineIndex = Math.Max(endLineIndex - ChunkOverlapLines, startLineIndex + 1);
        }

        return chunks;
    }

    private static async Task<string> ReadTextFileAsync(string file, CancellationToken ct)
    {
        try
        {
            return await File.ReadAllTextAsync(file, ct);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ComputeSha256(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool IsIndexableFile(string root, string file)
    {
        if (IsExcludedPath(root, file))
        {
            return false;
        }

        var info = new FileInfo(file);
        if (!info.Exists || info.Length <= 0 || info.Length > MaximumFileBytes)
        {
            return false;
        }

        return TextExtensions.Contains(info.Extension);
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(current);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (var directory in directories)
            {
                if (IsExcludedDirectory(directory) ||
                    IsReparseDirectory(directory) ||
                    !WorkspacePathResolver.IsResolvedInsideWorkspace(root, directory))
                {
                    continue;
                }

                pending.Push(directory);
            }
        }
    }

    private static bool IsExcludedDirectory(string directory)
    {
        var name = Path.GetFileName(directory);
        return name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
               name.Equals(".agentq", StringComparison.OrdinalIgnoreCase) ||
               name.Equals(".agents", StringComparison.OrdinalIgnoreCase) ||
               name.Equals(".codex", StringComparison.OrdinalIgnoreCase) ||
               name.Equals(".codex-build", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
               name.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("artifacts", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("embeddings", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReparseDirectory(string directory)
    {
        try
        {
            return new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return true;
        }
    }

    private static bool IsExcludedPath(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
        var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (IsArchivedDocumentationPath(parts))
        {
            return true;
        }

        return parts.Any(part =>
            part.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
            part.Equals(".agentq", StringComparison.OrdinalIgnoreCase) ||
            part.Equals(".agents", StringComparison.OrdinalIgnoreCase) ||
            part.Equals(".codex", StringComparison.OrdinalIgnoreCase) ||
            part.Equals(".codex-build", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
            part.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("artifacts", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("embeddings", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsArchivedDocumentationPath(IReadOnlyList<string> parts) =>
        parts.Count >= 2 &&
        parts[0].Equals("docs", StringComparison.OrdinalIgnoreCase) &&
        parts[1].Equals("archive", StringComparison.OrdinalIgnoreCase);
}
