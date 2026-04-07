using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentQ.Tools;

/// <summary>
/// Glob 파일 검색 도구
/// </summary>
public class GlobTool : ITool
{
    private const int MaximumFiles = 500;

    /// <summary>
    /// 도구 이름
    /// </summary>
    public string Name => "glob_search";

    /// <summary>
    /// 도구 설명
    /// </summary>
    public string Description => "List files using glob patterns";

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
            pattern = new { type = "string", description = "The glob pattern to match (e.g. '*.cs', '**/*.json')" },
            path = new { type = "string", description = "The directory to search in (default: current directory)" }
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

        if (string.IsNullOrWhiteSpace(pattern))
            return Task.FromResult(ToolResult.Error("pattern must not be empty"));

        var searchPath = ".";
        if (input.TryGetValue("path", out var pathObj) && pathObj is string p) searchPath = p;

        try
        {
            if (!ToolPathGuard.TryResolvePath(searchPath, out var searchDir, out var errorMessage))
            {
                return Task.FromResult(ToolResult.Error(errorMessage!));
            }

            if (!Directory.Exists(searchDir))
                return Task.FromResult(ToolResult.Error($"Directory not found: {searchPath}"));

            var matcher = BuildGlobRegex(pattern);
            var files = Directory.EnumerateFiles(searchDir, "*", SearchOption.AllDirectories)
                .Where(f => !IsExcludedPath(f))
                .Where(f => matcher.IsMatch(ToRelativePath(searchDir, f)))
                .Take(MaximumFiles + 1)
                .ToList();

            var limitReached = files.Count > MaximumFiles;
            if (limitReached)
            {
                files = files.Take(MaximumFiles).ToList();
            }

            var output = new Dictionary<string, object?>
            {
                ["pattern"] = pattern,
                ["path"] = searchPath,
                ["numFiles"] = files.Count,
                ["limitReached"] = limitReached,
                ["files"] = files
            };

            return Task.FromResult(ToolResult.Success(JsonSerializer.Serialize(output)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Glob search failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Glob 패턴을 정규식으로 변환
    /// </summary>
    /// <param name="pattern">Glob 패턴</param>
    /// <returns>정규식 객체</returns>
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

    /// <summary>
    /// 상대 경로로 변환
    /// </summary>
    /// <param name="root">루트 경로</param>
    /// <param name="path">전체 경로</param>
    /// <returns>상대 경로</returns>
    private static string ToRelativePath(string root, string path)
    {
        return Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    /// <summary>
    /// 제외 경로 여부 확인
    /// </summary>
    /// <param name="path">경로</param>
    /// <returns>제외 경로 여부</returns>
    private static bool IsExcludedPath(string path)
    {
        return path.Contains("\\bin\\") || path.Contains("\\obj\\") || path.Contains("\\.git\\") ||
               path.Contains("/bin/") || path.Contains("/obj/") || path.Contains("/.git/") ||
               path.Contains("\\node_modules\\") || path.Contains("/node_modules/");
    }
}
