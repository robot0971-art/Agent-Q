using System.Text.Json;
using AgentQ.Tools;

namespace AgentQ.Desktop.Services;

public sealed class DesktopSemanticSearchTool(
    EmbeddingIndexStore store,
    IEmbeddingClient embeddingClient,
    string workspaceRoot,
    string model) : ITool
{
    public string Name => "semantic_search";

    public string Description => "Search indexed project chunks by semantic meaning using the local embedding index";

    public bool RequiresPermission => false;

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            query = new { type = "string", description = "The natural-language search query" },
            limit = new { type = "integer", description = "Maximum number of results to return (default: 8)" }
        },
        required = new[] { "query" }
    };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object?> input, CancellationToken ct = default)
    {
        if (!input.TryGetValue("query", out var queryValue) || queryValue is not string query || string.IsNullOrWhiteSpace(query))
        {
            return ToolResult.Error("Missing required parameter: query");
        }

        var limit = 8;
        if (input.TryGetValue("limit", out var limitValue))
        {
            limit = limitValue switch
            {
                int integer => integer,
                long integer => (int)integer,
                JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var parsed) => parsed,
                _ => limit
            };
        }

        limit = Math.Clamp(limit, 1, 20);
        var chunks = await store.LoadChunksAsync(workspaceRoot, ct);
        var searchableChunks = chunks.Where(chunk => chunk.Vector.Length > 0).ToList();
        if (searchableChunks.Count == 0)
        {
            return ToolResult.Error("No embedded chunks found. Build the embedding vector index before using semantic_search.");
        }

        var queryVectors = await embeddingClient.CreateEmbeddingsAsync([query], model, ct);
        var queryVector = queryVectors.FirstOrDefault();
        if (queryVector is not { Length: > 0 })
        {
            return ToolResult.Error("Embedding provider returned no vector for the query.");
        }

        var results = searchableChunks
            .Select(chunk => new SemanticSearchResult(
                chunk.RelativePath,
                chunk.StartLine,
                chunk.EndLine,
                CosineSimilarity(queryVector, chunk.Vector),
                BuildPreview(chunk.Content)))
            .OrderByDescending(result => result.Score)
            .Take(limit)
            .ToList();

        return ToolResult.Success(JsonSerializer.Serialize(new
        {
            query,
            model,
            numResults = results.Count,
            results
        }));
    }

    private static double CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        var length = Math.Min(left.Count, right.Count);
        if (length == 0)
        {
            return 0;
        }

        double dot = 0;
        double leftMagnitude = 0;
        double rightMagnitude = 0;
        for (var i = 0; i < length; i++)
        {
            dot += left[i] * right[i];
            leftMagnitude += left[i] * left[i];
            rightMagnitude += right[i] * right[i];
        }

        if (leftMagnitude <= 0 || rightMagnitude <= 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }

    private static string BuildPreview(string content)
    {
        var preview = content.ReplaceLineEndings(" ").Trim();
        return preview.Length <= 240 ? preview : preview[..240] + "...";
    }

    private sealed record SemanticSearchResult(
        string RelativePath,
        int StartLine,
        int EndLine,
        double Score,
        string Preview);
}
