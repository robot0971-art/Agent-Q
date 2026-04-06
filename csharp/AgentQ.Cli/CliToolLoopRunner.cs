using System.Text;
using System.Text.Json;
using AgentQ.Core.Models;
using AgentQ.Core.Providers;
using AgentQ.Tools;

namespace AgentQ.Cli;

/// <summary>
/// CLI 도구 실행 루프
/// </summary>
public sealed class CliToolLoopRunner
{
    /// <summary>
    /// 대화 턴 실행
    /// </summary>
    /// <param name="provider">LLM 제공자</param>
    /// <param name="model">모델 이름</param>
    /// <param name="history">대화 기록</param>
    /// <param name="registry">도구 레지스트리</param>
    /// <param name="enforcer">권한 인포서</param>
    /// <param name="onTextDelta">텍스트 델타 콜백</param>
    /// <param name="onToolExecution">도구 실행 콜백</param>
    /// <param name="onToolOutput">도구 출력 콜백</param>
    /// <param name="onToolError">도구 오류 콜백</param>
    /// <param name="onPermissionDenied">권한 거부 콜백</param>
    /// <param name="ct">취소 토큰</param>
    public async Task ExecuteConversationTurnAsync(
        ILlmProvider provider,
        string model,
        ChatConversationHistory history,
        ToolRegistry registry,
        IPermissionEnforcer enforcer,
        Action<string>? onTextDelta = null,
        Action<string>? onToolExecution = null,
        Action<string>? onToolOutput = null,
        Action<string>? onToolError = null,
        Action<string>? onPermissionDenied = null,
        CancellationToken ct = default)
    {
        while (true)
        {
            var context = new ChatContext
            {
                Model = model,
                Messages = history.Messages.ToList(),
                MaxTokens = 1024,
                Stream = true
            };

            var tools = registry.GetToolDefinitions().Select(t => new ToolDefinition
            {
                Name = t.Name,
                Description = t.Description,
                InputSchema = t.InputSchema
            });

            var toolUses = new List<ChatContent>();
            var textBuilder = new StringBuilder();

            await foreach (var chunk in provider.GenerateStreamAsync(context, tools, ct))
            {
                if (chunk.TextDelta != null)
                {
                    textBuilder.Append(chunk.TextDelta);
                    onTextDelta?.Invoke(chunk.TextDelta);
                }

                if (chunk.ToolUseDelta?.IsComplete == true)
                {
                    toolUses.Add(ChatContent.CreateToolUse(
                        chunk.ToolUseDelta.ToolId,
                        chunk.ToolUseDelta.ToolName,
                        chunk.ToolUseDelta.PartialInput ?? "{}"));
                }
            }

            var assistantContent = new List<ChatContent>();
            if (textBuilder.Length > 0)
            {
                assistantContent.Add(ChatContent.CreateText(textBuilder.ToString()));
            }

            assistantContent.AddRange(toolUses);

            if (assistantContent.Any())
            {
                history.AddAssistantMessage(assistantContent);
            }

            if (!toolUses.Any())
            {
                break;
            }

            var toolResults = new List<ChatContent>();

            foreach (var toolUse in toolUses)
            {
                var toolName = toolUse.ToolName!;
                var toolId = toolUse.ToolId!;
                var input = toolUse.ToolInput;
                var inputJson = JsonSerializer.Serialize(input, new JsonSerializerOptions { WriteIndented = true });

                var tool = registry.Get(toolName);
                if (tool == null)
                {
                    toolResults.Add(ChatContent.CreateToolResult(toolId, $"Tool not found: {toolName}", true));
                    continue;
                }

                if (tool.RequiresPermission)
                {
                    if (!await enforcer.RequestPermissionAsync(toolName, tool.Description, inputJson))
                    {
                        onPermissionDenied?.Invoke(toolName);
                        toolResults.Add(ChatContent.CreateToolResult(toolId, "Permission denied by user", true));
                        continue;
                    }
                }

                onToolExecution?.Invoke(toolName);

                try
                {
                    var result = await tool.ExecuteAsync(ParseInput(input), ct);
                    if (result.IsError)
                    {
                        onToolError?.Invoke(result.Content);
                    }
                    else
                    {
                        onToolOutput?.Invoke(result.Content);
                    }

                    toolResults.Add(ChatContent.CreateToolResult(toolId, result.Content, result.IsError));
                }
                catch (Exception ex)
                {
                    var message = $"Error: {ex.Message}";
                    onToolError?.Invoke(message);
                    toolResults.Add(ChatContent.CreateToolResult(toolId, message, true));
                }
            }

            history.AddToolResults(toolResults);
        }
    }

    /// <summary>
    /// JSON 인수 파싱
    /// </summary>
    /// <param name="jsonArgs">JSON 인수 문자열</param>
    /// <returns>파싱된 인수 딕셔너리</returns>
    public Dictionary<string, object?> ParseJsonArguments(string jsonArgs)
    {
        using var doc = JsonDocument.Parse(jsonArgs);
        return ParseJsonObject(doc.RootElement);
    }

    /// <summary>
    /// 입력 파싱
    /// </summary>
    private static Dictionary<string, object?> ParseInput(object? input)
    {
        return input switch
        {
            JsonElement json when json.ValueKind == JsonValueKind.Object => ParseJsonObject(json),
            string rawJson => TryParseJsonObject(rawJson),
            _ => new Dictionary<string, object?>()
        };
    }

    /// <summary>
    /// JSON 객체 파싱 시도
    /// </summary>
    private static Dictionary<string, object?> TryParseJsonObject(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                return ParseJsonObject(doc.RootElement);
            }
        }
        catch
        {
        }

        return new Dictionary<string, object?>();
    }

    /// <summary>
    /// JSON 객체 파싱
    /// </summary>
    private static Dictionary<string, object?> ParseJsonObject(JsonElement element)
    {
        var inputDict = new Dictionary<string, object?>();
        foreach (var prop in element.EnumerateObject())
        {
            inputDict[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number when prop.Value.TryGetInt32(out var intValue) => intValue,
                JsonValueKind.Number => prop.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Object => prop.Value.Clone(),
                JsonValueKind.Array => prop.Value.Clone(),
                _ => prop.Value.GetRawText()
            };
        }

        return inputDict;
    }
}

