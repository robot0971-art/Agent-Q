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
    public string Description => "Read the contents of a file at the specified path";

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
        if (!input.TryGetValue("path", out var pathObj) || pathObj is not string path)
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

            var lines = File.ReadAllLines(fullPath);
            var offset = 0;
            var limit = Math.Min(lines.Length, DefaultLineLimit);

            if (TryGetInt32(input, "offset", out var parsedOffset)) offset = Math.Max(0, parsedOffset - 1);
            if (TryGetInt32(input, "limit", out var parsedLimit)) limit = parsedLimit;

            if (limit <= 0)
                return Task.FromResult(ToolResult.Error("limit must be greater than 0"));

            offset = Math.Min(offset, lines.Length);
            var requestedLimit = limit;
            limit = Math.Min(Math.Min(limit, MaximumLineLimit), lines.Length - offset);

            var selectedLines = lines.Skip(offset).Take(limit).ToArray();
            var content = string.Join("\n", selectedLines);
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
                ["totalLines"] = lines.Length,
                ["readLines"] = selectedLines.Length,
                ["offset"] = offset + 1,
                ["limit"] = limit,
                ["requestedLimit"] = requestedLimit,
                ["limitClamped"] = requestedLimit != limit,
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
    private static bool TryGetInt32(Dictionary<string, object?> input, string key, out int value)
    {
        value = 0;
        if (!input.TryGetValue(key, out var rawValue) || rawValue == null)
        {
            return false;
        }

        if (rawValue is int intValue)
        {
            value = intValue;
            return true;
        }

        if (rawValue is long longValue && longValue is >= int.MinValue and <= int.MaxValue)
        {
            value = (int)longValue;
            return true;
        }

        if (rawValue is string stringValue && int.TryParse(stringValue, out var parsed))
        {
            value = parsed;
            return true;
        }

        if (rawValue is JsonElement json && json.TryGetInt32(out parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }
}
