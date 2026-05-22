using System.Text.Json;
using System.Text.RegularExpressions;
using AgentQ.Tools;
using System.IO;

namespace AgentQ.Desktop.Services;

public sealed class DesktopHybridSearchTool(
    string workspaceRoot,
    EmbeddingIndexStore embeddingIndexStore,
    IEmbeddingClient? embeddingClient,
    string embeddingModel) : ITool
{
    private const int MaximumKeywordFiles = 800;
    private const int MaximumKeywordMatches = 80;
    private const int MaximumFileBytes = 512 * 1024;

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        "bin",
        "obj",
        "node_modules",
        "artifacts",
        ".codex-build",
        ".venv",
        "venv",
        "env",
        "__pycache__",
        ".agentq"
    };

    public string Name => "hybrid_search";

    public string Description => "Rank candidate project files by combining symbol, semantic, keyword, and project-map signals";

    public bool RequiresPermission => false;

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            query = new { type = "string", description = "Natural language intent, symbol name, or keyword to search for" },
            limit = new { type = "integer", description = "Maximum number of ranked candidate files to return (default: 8)" },
            includeSemantic = new { type = "boolean", description = "Whether to use embedding semantic search when available (default: true)" }
        },
        required = new[] { "query" }
    };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object?> input, CancellationToken ct = default)
    {
        if (!TryGetString(input, "query", out var query))
        {
            return ToolResult.Error("Missing required parameter: query");
        }

        var limit = Math.Clamp(TryGetInt32(input, "limit", 8), 1, 20);
        var includeSemantic = TryGetBool(input, "includeSemantic", fallback: true);
        var candidates = new Dictionary<string, HybridCandidate>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        AddSymbolSignals(candidates, query);
        AddKeywordSignals(candidates, query, warnings);
        await AddProjectMapSignalsAsync(candidates, query, ct);

        if (includeSemantic)
        {
            await AddSemanticSignalsAsync(candidates, query, warnings, ct);
        }

        var results = candidates.Values
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(candidate => new
            {
                candidate.RelativePath,
                candidate.Score,
                Reasons = candidate.Reasons.Take(8).ToArray(),
                Lines = candidate.Lines.OrderBy(line => line).Take(8).ToArray(),
                Sources = candidate.Sources.OrderBy(source => source, StringComparer.OrdinalIgnoreCase).ToArray()
            })
            .ToList();

        return ToolResult.Success(JsonSerializer.Serialize(new
        {
            query,
            numResults = results.Count,
            results,
            warnings
        }));
    }

    private void AddSymbolSignals(Dictionary<string, HybridCandidate> candidates, string query)
    {
        var symbols = new WorkspaceSymbolIndexService().Build(workspaceRoot).Symbols;
        foreach (var symbol in symbols)
        {
            var score = ScoreSymbol(symbol, query);
            if (score <= 0)
            {
                continue;
            }

            var candidate = GetCandidate(candidates, symbol.RelativePath);
            candidate.Score += score;
            candidate.Sources.Add("symbol");
            candidate.Lines.Add(symbol.Line);
            candidate.Reasons.Add($"symbol: {symbol.DisplayName}");
        }
    }

    private void AddKeywordSignals(Dictionary<string, HybridCandidate> candidates, string query, List<string> warnings)
    {
        var tokens = BuildSearchTokens(query);
        if (tokens.Count == 0 || !Directory.Exists(workspaceRoot))
        {
            return;
        }

        var scannedFiles = 0;
        var matches = 0;
        foreach (var file in EnumerateSearchableFiles(workspaceRoot).Take(MaximumKeywordFiles))
        {
            scannedFiles++;
            if (matches >= MaximumKeywordMatches)
            {
                break;
            }

            string[] lines;
            try
            {
                if (new FileInfo(file).Length > MaximumFileBytes)
                {
                    continue;
                }

                lines = File.ReadAllLines(file);
            }
            catch
            {
                continue;
            }

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var matchedToken = tokens.FirstOrDefault(token => line.Contains(token, StringComparison.OrdinalIgnoreCase));
                if (matchedToken == null)
                {
                    continue;
                }

                matches++;
                var relativePath = Path.GetRelativePath(workspaceRoot, file);
                var candidate = GetCandidate(candidates, relativePath);
                candidate.Score += 24;
                candidate.Sources.Add("keyword");
                candidate.Lines.Add(i + 1);
                candidate.Reasons.Add($"keyword: '{matchedToken}' at line {i + 1}");

                if (matches >= MaximumKeywordMatches)
                {
                    break;
                }
            }
        }

        if (scannedFiles >= MaximumKeywordFiles)
        {
            warnings.Add($"Keyword scan stopped at {MaximumKeywordFiles:0} files.");
        }
    }

    private async Task AddProjectMapSignalsAsync(Dictionary<string, HybridCandidate> candidates, string query, CancellationToken ct)
    {
        var tokens = BuildSearchTokens(query);
        if (tokens.Count == 0)
        {
            return;
        }

        var analysis = await new WorkspaceAnalysisService().AnalyzeAsync(workspaceRoot, ct);
        foreach (var keyFile in analysis.KeyFiles)
        {
            if (!tokens.Any(token => keyFile.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var candidate = GetCandidate(candidates, keyFile);
            candidate.Score += 30;
            candidate.Sources.Add("project-map");
            candidate.Reasons.Add("project-map: key file matched query");
        }

        foreach (var entry in analysis.ProjectMap)
        {
            if (!tokens.Any(token => entry.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var parts = entry.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            foreach (var path in parts[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = GetCandidate(candidates, path);
                candidate.Score += 18;
                candidate.Sources.Add("project-map");
                candidate.Reasons.Add($"project-map: {parts[0]}");
            }
        }
    }

    private async Task AddSemanticSignalsAsync(
        Dictionary<string, HybridCandidate> candidates,
        string query,
        List<string> warnings,
        CancellationToken ct)
    {
        if (embeddingClient == null || string.IsNullOrWhiteSpace(embeddingModel))
        {
            warnings.Add("Semantic search skipped: embedding provider is not configured.");
            return;
        }

        var chunks = await embeddingIndexStore.LoadChunksAsync(workspaceRoot, ct);
        var searchableChunks = chunks.Where(chunk => chunk.Vector.Length > 0).ToList();
        if (searchableChunks.Count == 0)
        {
            warnings.Add("Semantic search skipped: no embedded chunks found.");
            return;
        }

        IReadOnlyList<float> queryVector;
        try
        {
            queryVector = (await embeddingClient.CreateEmbeddingsAsync([query], embeddingModel, ct)).FirstOrDefault() ?? [];
        }
        catch (Exception ex)
        {
            warnings.Add($"Semantic search skipped: {ex.Message}");
            return;
        }

        if (queryVector.Count == 0)
        {
            warnings.Add("Semantic search skipped: embedding provider returned no query vector.");
            return;
        }

        foreach (var result in searchableChunks
                     .Select(chunk => new
                     {
                         Chunk = chunk,
                         Score = CosineSimilarity(queryVector, chunk.Vector)
                     })
                     .OrderByDescending(result => result.Score)
                     .Take(12))
        {
            if (result.Score <= 0)
            {
                continue;
            }

            var candidate = GetCandidate(candidates, result.Chunk.RelativePath);
            candidate.Score += Math.Round(result.Score * 80, 2);
            candidate.Sources.Add("semantic");
            candidate.Lines.Add(result.Chunk.StartLine);
            candidate.Reasons.Add($"semantic: score {result.Score:0.000}, lines {result.Chunk.StartLine:0}-{result.Chunk.EndLine:0}");
        }
    }

    private static double ScoreSymbol(CodeSymbol symbol, string query)
    {
        var score = 0.0;
        if (symbol.Name.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            score += 120;
        }
        else if (symbol.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            score += 90;
        }
        else if (symbol.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            score += 70;
        }

        if (!string.IsNullOrWhiteSpace(symbol.Container))
        {
            var qualifiedName = $"{symbol.Container}.{symbol.Name}";
            if (qualifiedName.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                score += 80;
            }
        }

        if (symbol.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            score += 25;
        }

        return score;
    }

    private static List<string> BuildSearchTokens(string query)
    {
        return Regex.Matches(query, """[\p{L}\p{N}_$.-]{3,}""")
            .Select(match => match.Value.Trim())
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static IEnumerable<string> EnumerateSearchableFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current)
                    .Where(file => !IsBinaryFile(file));
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (var directory in directories)
            {
                if (!ExcludedDirectories.Contains(Path.GetFileName(directory)))
                {
                    pending.Push(directory);
                }
            }
        }
    }

    private static bool IsBinaryFile(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".dll" or ".exe" or ".png" or ".jpg" or ".jpeg" or ".gif" or ".ico" or ".zip" or ".rar" or ".bin" or ".pdb" or ".so" or ".dylib" or ".pdf";
    }

    private static HybridCandidate GetCandidate(Dictionary<string, HybridCandidate> candidates, string relativePath)
    {
        relativePath = relativePath.Replace('\\', '/');
        if (!candidates.TryGetValue(relativePath, out var candidate))
        {
            candidate = new HybridCandidate(relativePath);
            candidates[relativePath] = candidate;
        }

        return candidate;
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

        return leftMagnitude <= 0 || rightMagnitude <= 0
            ? 0
            : dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }

    private static bool TryGetString(Dictionary<string, object?> input, string key, out string value)
    {
        if (input.TryGetValue(key, out var raw))
        {
            if (raw is string text && !string.IsNullOrWhiteSpace(text))
            {
                value = text.Trim();
                return true;
            }

            if (raw is JsonElement { ValueKind: JsonValueKind.String } element)
            {
                var jsonText = element.GetString();
                if (!string.IsNullOrWhiteSpace(jsonText))
                {
                    value = jsonText.Trim();
                    return true;
                }
            }
        }

        value = string.Empty;
        return false;
    }

    private static int TryGetInt32(Dictionary<string, object?> input, string key, int fallback)
    {
        if (!input.TryGetValue(key, out var raw))
        {
            return fallback;
        }

        return raw switch
        {
            int integer => integer,
            long integer => (int)integer,
            JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var parsed) => parsed,
            string text when int.TryParse(text, out var parsed) => parsed,
            _ => fallback
        };
    }

    private static bool TryGetBool(Dictionary<string, object?> input, string key, bool fallback)
    {
        if (!input.TryGetValue(key, out var raw))
        {
            return fallback;
        }

        return raw switch
        {
            bool value => value,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => fallback
        };
    }

    private sealed class HybridCandidate(string relativePath)
    {
        public string RelativePath { get; } = relativePath;

        public double Score { get; set; }

        public HashSet<string> Sources { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<int> Lines { get; } = [];

        public List<string> Reasons { get; } = [];
    }
}
