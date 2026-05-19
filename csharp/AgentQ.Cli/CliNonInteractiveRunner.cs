using AgentQ.Core.Models;
using AgentQ.Core.Providers;
using AgentQ.Tools;

namespace AgentQ.Cli;

public sealed class CliNonInteractiveRunner(ICliAutomationOutput output)
{
    public async Task<NonInteractiveRunResult> RunAsync(
        ILlmProvider provider,
        ProviderConfiguration config,
        ChatConversationHistory history,
        ToolRegistry registry,
        IPermissionEnforcer enforcer,
        CliToolLoopRunner loopRunner,
        string prompt)
    {
        history.AddUserMessage(prompt);
        var toolOutputs = new List<ToolExecutionRecord>();
        var toolErrors = new List<string>();
        var deniedTools = new List<string>();
        var executedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var cts = CreateTimeoutCancellation(config.TimeoutSeconds);
            await loopRunner.ExecuteConversationTurnAsync(
                provider,
                config.Model,
                history,
                registry,
                enforcer,
                maxTokens: config.MaxTokens,
                onToolExecution: toolName => executedTools.Add(toolName),
                onToolOutput: (toolName, toolOutput) => toolOutputs.Add(ToolExecutionRecord.Create(toolName, toolOutput, isError: false)),
                onToolError: (toolName, error) =>
                {
                    toolOutputs.Add(ToolExecutionRecord.Create(toolName, error, isError: true));
                    toolErrors.Add(error);
                },
                onPermissionDenied: toolName => deniedTools.Add(toolName),
                ct: cts?.Token ?? CancellationToken.None);

            var result = new NonInteractiveRunResult
            {
                FinalText = AutomationSupport.GetLatestAssistantText(history),
                MessageCount = history.MessageCount,
                Provider = provider.Name,
                Model = config.Model,
                BaseUrl = config.BaseUrl
            };
            result.AllowedTools.AddRange(config.AllowToolsWithoutPrompt ? ["*"] : config.AllowedToolNames);
            result.ConfiguredDeniedTools.AddRange(config.DeniedToolNames);
            result.ToolOutputs.AddRange(toolOutputs);
            result.ToolErrors.AddRange(toolErrors);
            result.DeniedTools.AddRange(deniedTools);
            result.ExecutedTools.AddRange(executedTools);
            output.WriteResult(config, result);
            return result;
        }
        catch (OperationCanceledException)
        {
            output.WriteError(config, "The non-interactive request timed out or was cancelled.", ProcessExitCode.ProviderFailure);
            return new NonInteractiveRunResult
            {
                FinalText = string.Empty,
                MessageCount = history.MessageCount,
                ForcedExitCode = ProcessExitCode.ProviderFailure
            };
        }
        catch (Exception ex)
        {
            output.WriteError(config, $"Conversation error: {ex.Message}", ProcessExitCode.ProviderFailure);
            return new NonInteractiveRunResult
            {
                FinalText = string.Empty,
                MessageCount = history.MessageCount,
                ForcedExitCode = ProcessExitCode.ProviderFailure
            };
        }
    }

    private static CancellationTokenSource? CreateTimeoutCancellation(int timeoutSeconds)
    {
        return timeoutSeconds <= 0
            ? null
            : new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
    }
}
