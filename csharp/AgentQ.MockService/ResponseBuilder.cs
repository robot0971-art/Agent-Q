using AgentQ.Api;

namespace AgentQ.MockService;

/// <summary>
/// 응답 빌더
/// </summary>
public static class ResponseBuilder
{
    private const string DefaultModel = "claude-sonnet-4-6";

    /// <summary>
    /// 텍스트 메시지 생성
    /// </summary>
    /// <param name="id">메시지 ID</param>
    /// <param name="text">텍스트 내용</param>
    /// <returns>메시지 응답</returns>
    public static MessageResponse TextMessage(string id, string text)
    {
        return new MessageResponse
        {
            Id = id,
            Type = "message",
            Role = "assistant",
            Content = new List<OutputContentBlock>
            {
                new()
                {
                    Type = OutputContentBlockType.Text,
                    Text = text
                }
            },
            Model = DefaultModel,
            StopReason = "end_turn",
            Usage = new Usage
            {
                InputTokens = 10,
                OutputTokens = 6
            }
        };
    }

    /// <summary>
    /// 사용량 정보가 포함된 텍스트 메시지 생성
    /// </summary>
    /// <param name="id">메시지 ID</param>
    /// <param name="text">텍스트 내용</param>
    /// <param name="inputTokens">입력 토큰 수</param>
    /// <param name="outputTokens">출력 토큰 수</param>
    /// <returns>메시지 응답</returns>
    public static MessageResponse TextMessageWithUsage(string id, string text, uint inputTokens, uint outputTokens)
    {
        return new MessageResponse
        {
            Id = id,
            Type = "message",
            Role = "assistant",
            Content = new List<OutputContentBlock>
            {
                new()
                {
                    Type = OutputContentBlockType.Text,
                    Text = text
                }
            },
            Model = DefaultModel,
            StopReason = "end_turn",
            Usage = new Usage
            {
                InputTokens = inputTokens,
                OutputTokens = outputTokens
            }
        };
    }

    /// <summary>
    /// 도구 사용 메시지 생성
    /// </summary>
    /// <param name="id">메시지 ID</param>
    /// <param name="toolId">도구 ID</param>
    /// <param name="toolName">도구 이름</param>
    /// <param name="input">도구 입력</param>
    /// <returns>메시지 응답</returns>
    public static MessageResponse ToolMessage(string id, string toolId, string toolName, object input)
    {
        return ToolMessages(id, new[] { new ToolUseMessage(toolId, toolName, input) });
    }

    /// <summary>
    /// 다중 도구 사용 메시지 생성
    /// </summary>
    /// <param name="id">메시지 ID</param>
    /// <param name="toolUses">도구 사용 목록</param>
    /// <returns>메시지 응답</returns>
    public static MessageResponse ToolMessages(string id, ToolUseMessage[] toolUses)
    {
        var content = new List<OutputContentBlock>();
        foreach (var toolUse in toolUses)
        {
            content.Add(new OutputContentBlock
            {
                Type = OutputContentBlockType.ToolUse,
                Id = toolUse.ToolId,
                Name = toolUse.ToolName,
                Input = toolUse.Input
            });
        }

        return new MessageResponse
        {
            Id = id,
            Type = "message",
            Role = "assistant",
            Content = content,
            Model = DefaultModel,
            StopReason = "tool_use",
            Usage = new Usage
            {
                InputTokens = 10,
                OutputTokens = 3
            }
        };
    }
}

/// <summary>
/// 도구 사용 메시지
/// </summary>
public record ToolUseMessage(string ToolId, string ToolName, object Input);

