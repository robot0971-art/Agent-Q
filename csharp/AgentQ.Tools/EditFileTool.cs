using System.Text.Json;

namespace AgentQ.Tools;

/// <summary>
/// 파일 편집 도구
/// </summary>
public class EditFileTool : ITool
{
    /// <summary>
    /// 도구 이름
    /// </summary>
    public string Name => "edit_file";

    /// <summary>
    /// 도구 설명
    /// </summary>
    public string Description => "Edit a file by replacing a specific string with a new string";

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
            path = new { type = "string", description = "Path to the file to edit" },
            old_string = new { type = "string", description = "The text to find and replace" },
            new_string = new { type = "string", description = "The text to replace it with" },
            replace_all = new { type = "boolean", description = "Replace all occurrences (default: false)" }
        },
        required = new[] { "path", "old_string", "new_string" }
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

        if (!input.TryGetValue("old_string", out var oldObj) || oldObj is not string oldString)
            return Task.FromResult(ToolResult.Error("Missing required parameter: old_string"));

        if (!input.TryGetValue("new_string", out var newObj) || newObj is not string newString)
            return Task.FromResult(ToolResult.Error("Missing required parameter: new_string"));

        var replaceAll = false;
        if (TryGetBoolean(input, "replace_all", out var parsedReplaceAll))
        {
            replaceAll = parsedReplaceAll;
        }

        try
        {
            if (!ToolPathGuard.TryResolvePath(path, out var fullPath, out var errorMessage))
            {
                return Task.FromResult(ToolResult.Error(errorMessage!));
            }

            if (!File.Exists(fullPath))
                return Task.FromResult(ToolResult.Error($"File not found: {path}"));

            var content = File.ReadAllText(fullPath);
            var count = 0;

            if (replaceAll)
            {
                count = CountOccurrences(content, oldString);
                if (count == 0)
                    return Task.FromResult(ToolResult.Error($"String not found in file: {path}"));

                content = content.Replace(oldString, newString);
            }
            else
            {
                var index = content.IndexOf(oldString, StringComparison.Ordinal);
                if (index == -1)
                    return Task.FromResult(ToolResult.Error($"String not found in file: {path}"));

                content = content.Remove(index, oldString.Length).Insert(index, newString);
                count = 1;
            }

            File.WriteAllText(fullPath, content);

            var output = new Dictionary<string, object?>
            {
                ["path"] = path,
                ["filePath"] = fullPath,
                ["replacements"] = count,
                ["status"] = "success"
            };

            return Task.FromResult(ToolResult.Success(JsonSerializer.Serialize(output)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to edit file: {ex.Message}"));
        }
    }

    /// <summary>
    /// 문자열 발생 횟수 계산
    /// </summary>
    /// <param name="content">원본 내용</param>
    /// <param name="oldString">찾을 문자열</param>
    /// <returns>발생 횟수</returns>
    private static int CountOccurrences(string content, string oldString)
    {
        if (string.IsNullOrEmpty(oldString))
        {
            return 0;
        }

        var count = 0;
        var index = 0;
        while ((index = content.IndexOf(oldString, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += oldString.Length;
        }

        return count;
    }

    /// <summary>
    /// Boolean 값 파싱 시도
    /// </summary>
    /// <param name="input">입력 딕셔너리</param>
    /// <param name="key">키</param>
    /// <param name="value">파싱된 값 (out)</param>
    /// <returns>파싱 성공 여부</returns>
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

