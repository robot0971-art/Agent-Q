using System.Text.Json;

namespace AgentQ.Tools;

public class GrepTool : ITool
{
    public string Name => "grep_search";
    public string Description => "Search for a pattern in files using grep-like functionality";
    public bool RequiresPermission => false;
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            pattern = new { type = "string", description = "The regex pattern to search for" },
            path = new { type = "string", description = "The directory or file to search in (default: current directory)" },
            output_mode = new { type = "string", description = "Output mode: 'content' or 'count' (default: content)" },
            include = new { type = "string", description = "File glob pattern to include (e.g. '*.cs')" }
        },
        required = new[] { "pattern" }
    };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, object?> input, CancellationToken ct = default)
    {
        if (!input.TryGetValue("pattern", out var patternObj) || patternObj is not string pattern)
            return Task.FromResult(ToolResult.Error("Missing required parameter: pattern"));

        var searchPath = ".";
        var outputMode = "content";
        var include = "*";

        if (input.TryGetValue("path", out var pathObj) && pathObj is string p) searchPath = p;
        if (input.TryGetValue("output_mode", out var modeObj) && modeObj is string m) outputMode = m;
        if (input.TryGetValue("include", out var incObj) && incObj is string incPattern) include = incPattern;

        try
        {
            var searchDir = Path.GetFullPath(searchPath);
            if (!Directory.Exists(searchDir))
                return Task.FromResult(ToolResult.Error($"Directory not found: {searchPath}"));

            var results = new List<GrepMatch>();
            var files = Directory.EnumerateFiles(searchDir, include, SearchOption.AllDirectories)
                .Where(f => !IsBinaryFile(f) && !IsExcludedPath(f));

            foreach (var file in files)
            {
                try
                {
                    var lines = File.ReadAllLines(file);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (System.Text.RegularExpressions.Regex.IsMatch(lines[i], pattern))
                        {
                            results.Add(new GrepMatch
                            {
                                File = file,
                                Line = i + 1,
                                Content = lines[i].Trim()
                            });
                        }
                    }
                }
                catch
                {
                    // Skip files that can't be read
                }
            }

            if (outputMode == "count")
            {
                var output = new Dictionary<string, object?>
                {
                    ["pattern"] = pattern,
                    ["numMatches"] = results.Count,
                    ["searchPath"] = searchPath
                };
                return Task.FromResult(ToolResult.Success(JsonSerializer.Serialize(output)));
            }

            var contentResult = new Dictionary<string, object?>
            {
                ["pattern"] = pattern,
                ["numMatches"] = results.Count,
                ["matches"] = results.Select(r => new
                {
                    file = r.File,
                    line = r.Line,
                    content = r.Content
                }).ToList()
            };

            return Task.FromResult(ToolResult.Success(JsonSerializer.Serialize(contentResult)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Grep search failed: {ex.Message}"));
        }
    }

    private static bool IsBinaryFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".dll" or ".exe" or ".png" or ".jpg" or ".gif" or ".ico" or ".zip" or ".rar" or ".bin" or ".pdb" or ".so" or ".dylib";
    }

    private static bool IsExcludedPath(string path)
    {
        var normalized = path.Replace('\\', '/').ToLowerInvariant();
        return normalized.Contains("/bin/") || normalized.Contains("/obj/") || normalized.Contains("/.git/") ||
               normalized.Contains("/node_modules/");
    }
}

public class GrepMatch
{
    public string File { get; init; } = string.Empty;
    public int Line { get; init; }
    public string Content { get; init; } = string.Empty;
}

