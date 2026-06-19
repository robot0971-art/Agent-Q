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
    public string Description =>
        "Edit an existing file with an exact old_string/new_string replacement. Read the file first, preserve exact indentation and surrounding text, prefer this over write_file for existing files, and use replace_all only for intentional file-wide replacements.";

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
            replace_all = new { type = "boolean", description = "Replace all occurrences (default: false)" },
            allow_high_risk_edit = new { type = "boolean", description = "Set only after explicit user approval for broad edits to high-risk files" }
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
        var path = ToolInputParser.GetString(input, "path");
        if (string.IsNullOrWhiteSpace(path))
            return Task.FromResult(ToolResult.Error("Missing required parameter: path"));

        var oldString = ToolInputParser.GetString(input, "old_string");
        if (oldString == null)
            return Task.FromResult(ToolResult.Error("Missing required parameter: old_string"));

        var newString = ToolInputParser.GetString(input, "new_string");
        if (newString == null)
            return Task.FromResult(ToolResult.Error("Missing required parameter: new_string"));

        var replaceAll = false;
        if (ToolInputParser.TryGetBoolean(input, "replace_all", out var parsedReplaceAll))
        {
            replaceAll = parsedReplaceAll;
        }

        try
        {
            if (!ToolPathGuard.TryResolvePath(path, out var fullPath, out var errorMessage))
            {
                return Task.FromResult(ToolResult.Error(errorMessage!));
            }

            if (string.IsNullOrEmpty(oldString))
                return Task.FromResult(ToolResult.Error("old_string must not be empty"));

            if (oldString == newString)
                return Task.FromResult(ToolResult.Error("old_string and new_string are identical; refusing no-op edit"));

            if (Directory.Exists(fullPath))
                return Task.FromResult(ToolResult.Error($"Path points to a directory, not a file: {path}"));

            if (!File.Exists(fullPath))
                return Task.FromResult(ToolResult.Error($"File not found: {path}"));

            var existingFile = TextFileIo.ReadAllTextPreservingEncoding(fullPath);
            var content = existingFile.Content;
            var riskError = EditRiskGuard.ValidateReplacement(path, content, oldString, replaceAll, input);
            if (riskError != null)
            {
                return Task.FromResult(ToolResult.Error(riskError));
            }

            var count = CountOccurrences(content, oldString);
            if (count == 0)
                return Task.FromResult(ToolResult.Error($"String not found in file: {path}"));

            if (replaceAll)
            {
                content = content.Replace(oldString, newString);
            }
            else
            {
                if (count > 1)
                    return Task.FromResult(ToolResult.Error($"String appears multiple times in file; use replace_all=true: {path}"));

                var index = content.IndexOf(oldString, StringComparison.Ordinal);
                content = content.Remove(index, oldString.Length).Insert(index, newString);
            }

            TextFileIo.WriteAllTextAtomically(fullPath, content, existingFile.Encoding);

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
}
