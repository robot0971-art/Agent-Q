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
            await ExecuteWithAutomationPromptAsync(
                provider,
                config,
                history,
                registry,
                enforcer,
                loopRunner,
                toolOutputs,
                toolErrors,
                deniedTools,
                executedTools,
                cts?.Token ?? CancellationToken.None);

            var finalText = AutomationSupport.GetLatestAssistantText(history);
            if (ShouldRetryManualFallback(finalText, executedTools, config))
            {
                history.AddUserMessage(
                    "Your previous answer gave manual copy/paste or permission instructions without using the allowed tools. " +
                    "This is non-interactive automation mode. Use the allowed tools now to make the requested change yourself, then verify it when a shell tool is allowed.");
                await ExecuteWithAutomationPromptAsync(
                    provider,
                    config,
                    history,
                    registry,
                    enforcer,
                    loopRunner,
                    toolOutputs,
                    toolErrors,
                    deniedTools,
                    executedTools,
                    cts?.Token ?? CancellationToken.None);
            }

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

    private static async Task ExecuteWithAutomationPromptAsync(
        ILlmProvider provider,
        ProviderConfiguration config,
        ChatConversationHistory history,
        ToolRegistry registry,
        IPermissionEnforcer enforcer,
        CliToolLoopRunner loopRunner,
        List<ToolExecutionRecord> toolOutputs,
        List<string> toolErrors,
        List<string> deniedTools,
        HashSet<string> executedTools,
        CancellationToken ct)
    {
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
                systemPromptAddendum: BuildAutomationPrompt(config, registry),
                ct: ct);
    }

    private static CancellationTokenSource? CreateTimeoutCancellation(int timeoutSeconds)
    {
        return timeoutSeconds <= 0
            ? null
            : new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
    }

    private static string BuildAutomationPrompt(ProviderConfiguration config, ToolRegistry registry)
    {
        var allowedTools = config.AllowToolsWithoutPrompt
            ? "all registered tools"
            : config.AllowedToolNames.Count == 0
                ? "none"
                : string.Join(", ", config.AllowedToolNames);
        var deniedTools = config.DeniedToolNames.Count == 0
            ? "none"
            : string.Join(", ", config.DeniedToolNames);
        var capabilitySnapshot = ToolCapabilitySnapshot.Create(config, registry).ToPromptBlock();

        return
            $"""
            Non-interactive automation mode:
            - Allowed tools for this run: {allowedTools}.
            - Denied tools for this run: {deniedTools}.
            - If a needed tool is listed as allowed, call it directly; do not ask the user to grant permission first.
            - If a tool call is denied or fails, report that exact tool result and choose the next safest available action.
            - For code-fix requests, prefer read_file plus edit_file/write_file and then bash verification when those tools are allowed.
            - Avoid final answers that mainly contain manual copy/paste instructions when an allowed editing tool could perform the change.

            {capabilitySnapshot}
            """;
    }

    private static bool ShouldRetryManualFallback(
        string finalText,
        IReadOnlySet<string> executedTools,
        ProviderConfiguration config)
    {
        if (!HasAllowedMutationTool(config) || executedTools.Overlaps(["edit_file", "write_file"]))
        {
            return false;
        }

        return LooksLikeManualFallback(finalText);
    }

    private static bool HasAllowedMutationTool(ProviderConfiguration config)
    {
        return config.AllowToolsWithoutPrompt ||
               config.AllowedToolNames.Any(tool =>
                   tool.Equals("edit_file", StringComparison.OrdinalIgnoreCase) ||
                   tool.Equals("write_file", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeManualFallback(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("copy", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("paste", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("manual", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("directly edit", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("권한", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("수동", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("복사", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("붙여", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("직접 수정", StringComparison.OrdinalIgnoreCase);
    }
}
