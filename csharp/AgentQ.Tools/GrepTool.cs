using System.Text.Json;

namespace AgentQ.Tools;

/// <summary>
/// Grep 검색 도구
/// </summary>
public class GrepTool : ITool
{
    /// <summary>
    /// 도구 이름
    /// </summary>
    public string Name => "grep_search";

    /// <summary>
    /// 도구 설명
    /// </summary>
    public string Description => "Search for a pattern in files using grep-like functionality";

    /// <summary>
    /// 권한 확인 필요 여부
    /// </summary>
    public bool RequiresPermission => false;

    /// <summary>
    /// 입력 스키마 (JSON Schema)
    /// </summary>
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

    /// <summary>
    /// 도구 실행
    /// </summary>
    /// <param name="input">입력 파라미터</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>도구 실행 결과</returns>
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

    /// <summary>
    /// 바이너리 파일 여부 확인
    /// </summary>
    /// <param name="path">파일 경로</param>
    /// <returns>바이너리 파일 여부</returns>
    private static bool IsBinaryFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".dll" or ".exe" or ".png" or ".jpg" or ".gif" or ".ico" or ".zip" or ".rar" or ".bin" or ".pdb" or ".so" or ".dylib";
    }

    /// <summary>
    /// 제외 경로 여부 확인
    /// </summary>
    /// <param name="path">경로</param>
    /// <returns>제외 경로 여부</returns>
    private static bool IsExcludedPath(string path)
    {
        var normalized = path.Replace('\\', '/').ToLowerInvariant();
        return normalized.Contains("/bin/") || normalized.Contains("/obj/") || normalized.Contains("/.git/") ||
               normalized.Contains("/node_modules/");
    }
}

/// <summary>
/// Grep 검색 결과
/// </summary>
public class GrepMatch
{
    /// <summary>
    /// 파일 경로
    /// </summary>
    public string File { get; init; } = string.Empty;

    /// <summary>
    /// 줄 번호
    /// </summary>
    public int Line { get; init; }

    /// <summary>
    /// 내용
    /// </summary>
    public string Content { get; init; } = string.Empty;
}

