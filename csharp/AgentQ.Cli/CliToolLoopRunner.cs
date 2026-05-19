using System.Text;
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
        int? maxSteps = null,
        uint maxTokens = 4096,
        Action<string>? onTextDelta = null,
        Action<string>? onToolExecution = null,
        Action<string, string>? onToolOutput = null,
        Action<string, string>? onToolError = null,
        Action<string>? onPermissionDenied = null,
        CancellationToken ct = default)
    {
        var stepLimit = maxSteps.GetValueOrDefault(45);
        var stepCount = 0;

        while (true)
        {
            stepCount++;
            if (stepCount > stepLimit)
            {
                history.AddAssistantMessage([
                    ChatContent.CreateText($"Stopped after reaching the maximum tool steps ({stepLimit}).")
                ]);
                break;
            }

            var turnBuilder = new ConversationTurnBuilder();
            var turnRequest = turnBuilder.Build(model, history, registry, stepLimit, maxTokens);

            var streamingProcessor = new StreamingProcessor();
            var response = await streamingProcessor.ProcessAsync(
                provider.GenerateStreamAsync(turnRequest.Context, turnRequest.Tools, ct),
                onTextDelta,
                ct);

            if (response.AssistantContent.Any())
            {
                history.AddAssistantMessage(response.AssistantContent.ToList());
            }

            if (!response.ToolUses.Any())
            {
                break;
            }

            var toolExecutor = new ToolExecutor(
                registry,
                enforcer,
                new ToolExecutionCallbacks
                {
                    OnToolExecution = onToolExecution,
                    OnToolOutput = onToolOutput,
                    OnToolError = onToolError,
                    OnPermissionDenied = onPermissionDenied
                });
            var toolResults = await toolExecutor.ExecuteAsync(response.ToolUses, ct);

            if (toolResults.Any())
            {
                history.AddToolResults(toolResults);
            }
        }
    }

    /// <summary>
    /// JSON 인수 파싱
    /// </summary>
    /// <param name="jsonArgs">JSON 인수 문자열</param>
    /// <returns>파싱된 인수 딕셔너리</returns>
    public Dictionary<string, object?> ParseJsonArguments(string jsonArgs)
    {
        return JsonArgumentParser.ParseJsonArguments(jsonArgs);
    }
}

