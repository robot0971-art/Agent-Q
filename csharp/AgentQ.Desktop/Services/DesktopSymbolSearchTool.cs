using System.Text.Json;
using AgentQ.Tools;

namespace AgentQ.Desktop.Services;

public sealed class DesktopSymbolSearchTool(
    string workspaceRoot,
    WorkspaceSymbolIndexService? symbolIndexService = null) : ITool
{
    private readonly WorkspaceSymbolIndexService _symbolIndexService = symbolIndexService ?? new WorkspaceSymbolIndexService();

    public string Name => "symbol_search";

    public string Description => "Search project symbols by name, kind, language, and path using the local workspace symbol index";

    public bool RequiresPermission => false;

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            query = new { type = "string", description = "Symbol name or partial name to search for" },
            kind = new { type = "string", description = "Optional symbol kind filter, such as class, method, function, record, interface, struct, enum" },
            language = new { type = "string", description = "Optional language filter, such as C#, Python, TypeScript, JavaScript" },
            path = new { type = "string", description = "Optional relative path substring filter" },
            limit = new { type = "integer", description = "Maximum number of results to return (default: 12)" }
        },
        required = new[] { "query" }
    };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, object?> input, CancellationToken ct = default)
    {
        if (!TryGetString(input, "query", out var query))
        {
            return Task.FromResult(ToolResult.Error("Missing required parameter: query"));
        }

        var limit = Math.Clamp(TryGetInt32(input, "limit", 12), 1, 30);
        TryGetString(input, "kind", out var kind);
        TryGetString(input, "language", out var language);
        TryGetString(input, "path", out var path);

        var index = _symbolIndexService.Build(workspaceRoot);
        var results = index.Symbols
            .Select(symbol => new SymbolSearchMatch(symbol, Score(symbol, query, kind, language, path)))
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Symbol.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(match => match.Symbol.Line)
            .Take(limit)
            .Select(match => new
            {
                match.Symbol.Name,
                match.Symbol.Kind,
                match.Symbol.Language,
                match.Symbol.RelativePath,
                match.Symbol.Line,
                match.Symbol.Container,
                match.Symbol.DisplayName,
                match.Score
            })
            .ToList();

        return Task.FromResult(ToolResult.Success(JsonSerializer.Serialize(new
        {
            query,
            kind = string.IsNullOrWhiteSpace(kind) ? null : kind,
            language = string.IsNullOrWhiteSpace(language) ? null : language,
            path = string.IsNullOrWhiteSpace(path) ? null : path,
            numResults = results.Count,
            indexedSymbols = index.SymbolCount,
            indexedFiles = index.FilesIndexed,
            results
        })));
    }

    private static int Score(CodeSymbol symbol, string query, string kind, string language, string path)
    {
        if (!string.IsNullOrWhiteSpace(kind) &&
            !symbol.Kind.Contains(kind, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(language) &&
            !symbol.Language.Contains(language, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(path) &&
            !symbol.RelativePath.Contains(path, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var score = 0;
        if (symbol.Name.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }
        else if (symbol.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            score += 80;
        }
        else if (symbol.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            score += 60;
        }

        if (!string.IsNullOrWhiteSpace(symbol.Container))
        {
            var qualifiedName = $"{symbol.Container}.{symbol.Name}";
            if (qualifiedName.Equals(query, StringComparison.OrdinalIgnoreCase))
            {
                score += 100;
            }
            else if (qualifiedName.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                score += 70;
            }
        }

        if (symbol.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        if (symbol.Kind is "class" or "record" or "interface")
        {
            score += 5;
        }

        return score;
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

    private sealed record SymbolSearchMatch(CodeSymbol Symbol, int Score);
}
