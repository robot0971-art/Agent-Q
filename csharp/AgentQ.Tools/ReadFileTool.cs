using System.Text.Json;

namespace AgentQ.Tools;

/// <summary>
/// 파일 읽기 도구
/// </summary>
public class ReadFileTool : ITool
{
    private const int DefaultLineLimit = 200;
    private const int MaximumLineLimit = 500;
    private const int MaximumContentLength = 20000;

    /// <summary>
    /// 도구 이름
    /// </summary>
    public string Name => "read_file";

    /// <summary>
    /// 도구 설명
    /// </summary>
    public string Description =>
        "Read file contents before analyzing or editing. Use offset/limit for large files, grep_search/glob_search when the path or target text is unclear, and parallel reads when multiple known files are needed.";

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
            path = new { type = "string", description = "Path to the file to read" },
            offset = new { type = "integer", description = "Line number to start reading from (1-indexed)" },
            limit = new { type = "integer", description = "Maximum number of lines to read" }
        },
        required = new[] { "path" }
    };

    /// <summary>
    /// 도구 실행
    /// </summary>
    /// <param name="input">입력 파라미터</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>도구 실행 결과</returns>
    public Task<ToolResult> ExecuteAsync(Dictionary<string, object?> input, CancellationToken ct = default)
    {
        var path = ToolInputParser.GetString(input, "path");
        if (string.IsNullOrWhiteSpace(path))
            return Task.FromResult(ToolResult.Error("Missing required parameter: path"));

        try
        {
            if (!ToolPathGuard.TryResolvePath(path, out var fullPath, out var errorMessage))
            {
                return Task.FromResult(ToolResult.Error(errorMessage!));
            }

            if (Directory.Exists(fullPath))
                return Task.FromResult(ToolResult.Error($"Path points to a directory, not a file: {path}"));

            if (!File.Exists(fullPath))
                return Task.FromResult(ToolResult.Error($"File not found: {path}"));

            var offset = 0;
            var limit = DefaultLineLimit;

            if (ToolInputParser.TryGetInt32(input, "offset", out var parsedOffset)) offset = Math.Max(0, parsedOffset - 1);
            if (ToolInputParser.TryGetInt32(input, "limit", out var parsedLimit)) limit = parsedLimit;

            if (limit <= 0)
                return Task.FromResult(ToolResult.Error("limit must be greater than 0"));

            var requestedLimit = limit;
            limit = Math.Min(limit, MaximumLineLimit);

            if (LooksLikeBinaryFile(fullPath))
                return Task.FromResult(ToolResult.Error($"Binary file is not supported by read_file: {path}"));

            var readResult = ReadSelectedLines(fullPath, offset, limit);
            var content = string.Join("\n", readResult.SelectedLines);
            var contentTruncated = false;
            if (content.Length > MaximumContentLength)
            {
                content = content[..MaximumContentLength] + "\n[truncated]";
                contentTruncated = true;
            }

            var output = new Dictionary<string, object?>
            {
                ["path"] = path,
                ["content"] = content,
                ["totalLines"] = readResult.TotalLines,
                ["readLines"] = readResult.SelectedLines.Count,
                ["offset"] = Math.Min(offset, readResult.TotalLines) + 1,
                ["limit"] = Math.Min(limit, Math.Max(0, readResult.TotalLines - Math.Min(offset, readResult.TotalLines))),
                ["requestedLimit"] = requestedLimit,
                ["limitClamped"] = requestedLimit != limit || limit > readResult.SelectedLines.Count,
                ["contentTruncated"] = contentTruncated
            };

            return Task.FromResult(ToolResult.Success(JsonSerializer.Serialize(output)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to read file: {ex.Message}"));
        }
    }

    /// <summary>
    /// Int32 값 파싱 시도
    /// </summary>
    /// <param name="input">입력 딕셔너리</param>
    /// <param name="key">키</param>
    /// <param name="value">파싱된 값 (out)</param>
    /// <returns>파싱 성공 여부</returns>
    private static ReadLinesResult ReadSelectedLines(string fullPath, int offset, int limit)
    {
        var selectedLines = new List<string>(Math.Min(limit, MaximumLineLimit));
        var totalLines = 0;

        using var reader = new StreamReader(fullPath, detectEncodingFromByteOrderMarks: true);
        while (reader.ReadLine() is { } line)
        {
            if (totalLines >= offset && selectedLines.Count < limit)
            {
                selectedLines.Add(line);
            }

            totalLines++;
        }

        return new ReadLinesResult(totalLines, selectedLines);
    }

    private static bool LooksLikeBinaryFile(string fullPath)
    {
        const int sampleSize = 4096;
        Span<byte> buffer = stackalloc byte[sampleSize];
        using var stream = File.OpenRead(fullPath);
        var read = stream.Read(buffer);
        return buffer[..read].Contains((byte)0);
    }

    private sealed record ReadLinesResult(int TotalLines, IReadOnlyList<string> SelectedLines);
}
