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
    public string Description =>
        "Create a new file or overwrite a file only when explicitly needed. For existing files, read the file first and prefer edit_file for small changes; do not create documentation files unless the user asked for them.";

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
            content = new { type = "string", description = "Content to write to the file" },
            overwrite = new { type = "boolean", description = "Whether to overwrite an existing file (default: true)" },
            allow_high_risk_edit = new { type = "boolean", description = "Set only after explicit user approval for high-risk whole-file rewrites" }
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

        var overwrite = true;
        if (TryGetBoolean(input, "overwrite", out var parsedOverwrite))
        {
            overwrite = parsedOverwrite;
        }

        try
        {
            if (!ToolPathGuard.TryResolvePath(path, out var fullPath, out var errorMessage))
            {
                return Task.FromResult(ToolResult.Error(errorMessage!));
            }

            if (Path.EndsInDirectorySeparator(fullPath))
            {
                return Task.FromResult(ToolResult.Error($"Path points to a directory, not a file: {path}"));
            }

            if (Directory.Exists(fullPath))
            {
                return Task.FromResult(ToolResult.Error($"Path points to an existing directory, not a file: {path}"));
            }

            var existedBeforeWrite = File.Exists(fullPath);
            if (existedBeforeWrite && !overwrite)
            {
                return Task.FromResult(ToolResult.Error($"Refusing to overwrite existing file without overwrite=true: {path}"));
            }

            if (existedBeforeWrite)
            {
                var existingContent = File.ReadAllText(fullPath);
                var risk = EditRiskGuard.AssessExistingFile(fullPath, existingContent);
                if (risk.IsHighRisk && !EditRiskGuard.IsRiskAcknowledged(input))
                {
                    return Task.FromResult(ToolResult.Error(EditRiskGuard.BuildWriteBlockMessage(path, risk)));
                }
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
                ["overwroteExisting"] = existedBeforeWrite,
                ["status"] = "success"
            };

            return Task.FromResult(ToolResult.Success(JsonSerializer.Serialize(output)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to write file: {ex.Message}"));
        }
    }

    private static bool TryGetBoolean(Dictionary<string, object?> input, string key, out bool value)
    {
        value = false;
        if (!input.TryGetValue(key, out var rawValue) || rawValue == null)
        {
            return false;
        }

        if (rawValue is bool boolValue)
        {
            value = boolValue;
            return true;
        }

        if (rawValue is string stringValue && bool.TryParse(stringValue, out var parsed))
        {
            value = parsed;
            return true;
        }

        if (rawValue is JsonElement json && json.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = json.GetBoolean();
            return true;
        }

        return false;
    }
}
