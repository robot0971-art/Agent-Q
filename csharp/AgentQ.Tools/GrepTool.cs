using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentQ.Tools;

/// <summary>
/// Grep 검색 도구
/// </summary>
public class GrepTool : ITool
{
    private const int MaximumMatches = 200;
    private const int MaximumFilesToScan = 2000;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

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
        var pattern = ToolInputParser.GetString(input, "pattern");
        if (pattern == null)
            return Task.FromResult(ToolResult.Error("Missing required parameter: pattern"));

        if (string.IsNullOrWhiteSpace(pattern))
            return Task.FromResult(ToolResult.Error("pattern must not be empty"));

        var searchPath = ".";
        var outputMode = "content";
        var include = "*";

        if (ToolInputParser.GetString(input, "path") is { } p) searchPath = p;
        if (ToolInputParser.GetString(input, "output_mode") is { } m) outputMode = m;
        if (ToolInputParser.GetString(input, "include") is { } incPattern) include = incPattern;

        try
        {
            if (!ToolPathGuard.TryResolvePath(searchPath, out var resolvedPath, out var errorMessage))
            {
                return Task.FromResult(ToolResult.Error(errorMessage!));
            }

            var regex = new Regex(pattern, RegexOptions.CultureInvariant, RegexTimeout);

            var searchDir = resolvedPath;
            string? targetFile = null;
            if (File.Exists(resolvedPath))
            {
                targetFile = resolvedPath;
                searchDir = Path.GetDirectoryName(resolvedPath) ?? resolvedPath;
            }
            else if (!Directory.Exists(resolvedPath))
            {
                return Task.FromResult(ToolResult.Error($"Directory or file not found: {searchPath}"));
            }

            var results = new List<GrepMatch>();
            var candidateFiles = EnumerateCandidateFiles(searchDir, include, targetFile)
                .Take(MaximumFilesToScan + 1)
                .ToList();
            var fileLimitReached = candidateFiles.Count > MaximumFilesToScan;
            var files = fileLimitReached
                ? candidateFiles.Take(MaximumFilesToScan).ToList()
                : candidateFiles;
            var scannedFiles = 0;
            var matchLimitReached = false;

            foreach (var file in files)
            {
                scannedFiles++;

                try
                {
                    var lines = File.ReadAllLines(file);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (regex.IsMatch(lines[i]))
                        {
                            results.Add(new GrepMatch
                            {
                                File = file,
                                Line = i + 1,
                                Content = lines[i].Trim()
                            });

                            if (results.Count >= MaximumMatches)
                            {
                                matchLimitReached = true;
                                break;
                            }
                        }
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    return Task.FromResult(ToolResult.Error("Pattern evaluation timed out; use a simpler regex"));
                }
                catch
                {
                    // Skip files that can't be read
                }

                if (matchLimitReached)
                {
                    break;
                }
            }

            if (outputMode == "count")
            {
                var output = new Dictionary<string, object?>
                {
                    ["pattern"] = pattern,
                    ["numMatches"] = results.Count,
                    ["searchPath"] = searchPath,
                    ["scannedFiles"] = scannedFiles,
                    ["matchLimitReached"] = matchLimitReached,
                    ["fileLimitReached"] = fileLimitReached
                };
                return Task.FromResult(ToolResult.Success(JsonSerializer.Serialize(output)));
            }

            var contentResult = new Dictionary<string, object?>
            {
                ["pattern"] = pattern,
                ["numMatches"] = results.Count,
                ["searchPath"] = searchPath,
                ["scannedFiles"] = scannedFiles,
                ["matchLimitReached"] = matchLimitReached,
                ["fileLimitReached"] = fileLimitReached,
                ["matches"] = results.Select(r => new
                {
                    file = r.File,
                    line = r.Line,
                    content = r.Content
                }).ToList()
            };

            return Task.FromResult(ToolResult.Success(JsonSerializer.Serialize(contentResult)));
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"Invalid grep pattern: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Grep search failed: {ex.Message}"));
        }
    }

    private static IEnumerable<string> EnumerateCandidateFiles(string searchDir, string include, string? targetFile)
    {
        if (!string.IsNullOrEmpty(targetFile))
        {
            if (IsBinaryFile(targetFile) || IsExcludedPath(targetFile))
            {
                return [];
            }

            return [targetFile];
        }

        return EnumerateFilesWithoutFollowingLinks(searchDir, include)
            .Where(f => !IsBinaryFile(f) && !IsExcludedPath(f));
    }

    private static IEnumerable<string> EnumerateFilesWithoutFollowingLinks(string searchDir, string include)
    {
        var pending = new Stack<string>();
        pending.Push(searchDir);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current, include, SearchOption.TopDirectoryOnly).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (!IsReparsePoint(file))
                {
                    yield return file;
                }
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var directory in directories)
            {
                if (!IsReparsePoint(directory))
                {
                    pending.Push(directory);
                }
            }
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return true;
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
        if (ext is ".dll" or ".exe" or ".png" or ".jpg" or ".gif" or ".ico" or ".zip" or ".rar" or ".bin" or ".pdb" or ".so" or ".dylib")
        {
            return true;
        }

        try
        {
            Span<byte> buffer = stackalloc byte[4096];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bytesRead = stream.Read(buffer);
            return buffer[..bytesRead].Contains((byte)0);
        }
        catch
        {
            return true;
        }
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
