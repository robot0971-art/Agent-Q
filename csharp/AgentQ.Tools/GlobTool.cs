using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentQ.Tools;

public class GlobTool : ITool
{
    public string Name => "glob_search";
    public string Description => "List files using glob patterns";
    public bool RequiresPermission => false;
    public object InputSchema => new

    {
        type = "object",
        properties = new
        {
            pattern = new { type = "string", description = "The glob pattern to match (e.g. '*.cs', '**/*.json')" },
            path = new { type = "string", description = "The directory to search in (default: current directory)" }
        },
        required = new[] { "pattern" }
    };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, object?> input, CancellationToken ct = default)
    {
        if (!input.TryGetValue("pattern", out var patternObj) || patternObj is not string pattern)
            return Task.FromResult(ToolResult.Error("Missing required parameter: pattern"));

        var searchPath = ".";
        if (input.TryGetValue("path", out var pathObj) && pathObj is string p) searchPath = p;

        try
        {
            var searchDir = Path.GetFullPath(searchPath);
            if (!Directory.Exists(searchDir))
                return Task.FromResult(ToolResult.Error($"Directory not found: {searchPath}"));

            var matcher = BuildGlobRegex(pattern);
            var files = Directory.EnumerateFiles(searchDir, "*", SearchOption.AllDirectories)
                .Where(f => !IsExcludedPath(f))
                .Where(f => matcher.IsMatch(ToRelativePath(searchDir, f)))
                .ToList();

            var output = new Dictionary<string, object?>
            {
                ["pattern"] = pattern,
                ["path"] = searchPath,
                ["numFiles"] = files.Count,
                ["files"] = files
            };

            return Task.FromResult(ToolResult.Success(JsonSerializer.Serialize(output)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Glob search failed: {ex.Message}"));
        }
    }

    private static Regex BuildGlobRegex(string pattern)
    {
        var normalized = pattern.Replace('\\', '/');
        var regexPattern = Regex.Escape(normalized)
            .Replace(@"\*\*", "__DOUBLE_STAR__")
            .Replace(@"\*", "[^/]*")
            .Replace(@"\?", "[^/]")
            .Replace("__DOUBLE_STAR__", ".*");

        return new Regex($"^{regexPattern}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string ToRelativePath(string root, string path)
    {
        return Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    private static bool IsExcludedPath(string path)
    {
        return path.Contains("\\bin\\") || path.Contains("\\obj\\") || path.Contains("\\.git\\") ||
               path.Contains("/bin/") || path.Contains("/obj/") || path.Contains("/.git/") ||
               path.Contains("\\node_modules\\") || path.Contains("/node_modules/");
    }
}

