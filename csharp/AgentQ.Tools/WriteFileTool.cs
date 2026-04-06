using System.Text;
using System.Text.Json;

namespace AgentQ.Tools;

/// <summary>
/// 파일 쓰기 도구
/// </summary>
public class WriteFileTool : ITool
{
    /// <summary>
    /// 도구 이름
    /// </summary>
    public string Name => "write_file";

    /// <summary>
    /// 도구 설명
    /// </summary>
    public string Description => "Write content to a file, creating it if it doesn't exist";

    /// <summary>
    /// 권한 확인 필요 여부
    /// </summary>
    public bool RequiresPermission => true;

    /// <summary>
    /// 입력 스키마 (JSON Schema)
    /// </summary>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Path to the file to write" },
            content = new { type = "string", description = "Content to write to the file" }
        },
        required = new[] { "path", "content" }
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

        if (!input.TryGetValue("content", out var contentObj) || contentObj is not string content)
            return Task.FromResult(ToolResult.Error("Missing required parameter: content"));

        try
        {
            if (!ToolPathGuard.TryResolvePath(path, out var fullPath, out var errorMessage))
            {
                return Task.FromResult(ToolResult.Error(errorMessage!));
            }

            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, content);

            var output = new Dictionary<string, object?>
            {
                ["path"] = path,
                ["filePath"] = fullPath,
                ["bytesWritten"] = Encoding.UTF8.GetByteCount(content),
                ["status"] = "success"
            };

            return Task.FromResult(ToolResult.Success(JsonSerializer.Serialize(output)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to write file: {ex.Message}"));
        }
    }
}

