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
        var hitMaxSteps = false;

        try
        {
            using var cts = CreateTimeoutCancellation(config.TimeoutSeconds);
            var loopResult = await ExecuteWithAutomationPromptAsync(
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
            hitMaxSteps = ApplyLoopResult(loopResult, toolErrors);

            var finalText = AutomationSupport.GetLatestAssistantText(history);
            if (!hitMaxSteps && ShouldRetryNoToolFallback(prompt, finalText, executedTools, config))
            {
                history.AddUserMessage(BuildNoToolRetryInstruction(finalText));
                loopResult = await ExecuteWithAutomationPromptAsync(
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
                hitMaxSteps = ApplyLoopResult(loopResult, toolErrors);
            }

            finalText = AutomationSupport.GetLatestAssistantText(history);
            if (!hitMaxSteps && ShouldRejectNoToolCompletion(prompt, finalText, executedTools, config))
            {
                toolErrors.Add("The model claimed the requested change was complete without using an allowed mutation tool. Treating the run as failed.");
            }

            var result = new NonInteractiveRunResult
            {
                FinalText = finalText,
                MessageCount = history.MessageCount,
                Provider = provider.Name,
                Model = config.Model,
                BaseUrl = config.BaseUrl,
                ForcedExitCode = hitMaxSteps ? ProcessExitCode.ToolFailure : null
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

    private static async Task<CliToolLoopRunResult> ExecuteWithAutomationPromptAsync(
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
        return await loopRunner.ExecuteConversationTurnAsync(
            provider,
            config.Model,
            history,
            registry,
            enforcer,
            maxTokens: config.MaxTokens,
            onToolExecution: toolName => executedTools.Add(toolName),
            onToolOutput: (toolName, toolOutput) =>
            {
                var failedShellCommand = IsFailedShellOutput(toolName, toolOutput, out var shellFailure);
                toolOutputs.Add(ToolExecutionRecord.Create(toolName, toolOutput, isError: failedShellCommand));
                if (failedShellCommand)
                {
                    toolErrors.Add(shellFailure);
                }
            },
            onToolError: (toolName, error) =>
            {
                toolOutputs.Add(ToolExecutionRecord.Create(toolName, error, isError: true));
                toolErrors.Add(error);
            },
            onPermissionDenied: toolName => deniedTools.Add(toolName),
            systemPromptAddendum: BuildAutomationPrompt(config, registry),
            ct: ct);
    }

    private static bool ApplyLoopResult(CliToolLoopRunResult loopResult, List<string> toolErrors)
    {
        if (!loopResult.HitMaxSteps)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(loopResult.StopMessage))
        {
            toolErrors.Add(loopResult.StopMessage);
        }

        return true;
    }

    private static bool IsFailedShellOutput(string toolName, string toolOutput, out string failure)
    {
        failure = string.Empty;
        if (!toolName.Equals("bash", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(toolOutput);
            if (!document.RootElement.TryGetProperty("exitCode", out var exitCodeElement) ||
                !exitCodeElement.TryGetInt32(out var exitCode) ||
                exitCode == 0)
            {
                return false;
            }

            var stderr = document.RootElement.TryGetProperty("stderr", out var stderrElement)
                ? stderrElement.GetString()
                : null;
            var stdout = document.RootElement.TryGetProperty("stdout", out var stdoutElement)
                ? stdoutElement.GetString()
                : null;
            var detail = !string.IsNullOrWhiteSpace(stderr)
                ? stderr
                : stdout;
            failure = string.IsNullOrWhiteSpace(detail)
                ? $"bash exited with code {exitCode}"
                : $"bash exited with code {exitCode}: {detail.Trim()}";
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
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
            - Keep edits inside the user's requested scope. Report additional unrelated bugs as optional follow-up findings instead of changing them in the same run.

            {capabilitySnapshot}
            """;
    }

    private static bool ShouldRetryNoToolFallback(
        string prompt,
        string finalText,
        IReadOnlySet<string> executedTools,
        ProviderConfiguration config)
    {
        if (!HasAllowedMutationTool(config) || HasExecutedMutationTool(executedTools))
        {
            return false;
        }

        return LooksLikeManualFallback(finalText) || LooksLikeNoToolCompletionForAction(prompt, finalText);
    }

    private static bool HasAllowedMutationTool(ProviderConfiguration config)
    {
        return config.AllowToolsWithoutPrompt ||
               config.AllowedToolNames.Any(tool =>
                   tool.Equals("edit_file", StringComparison.OrdinalIgnoreCase) ||
                   tool.Equals("write_file", StringComparison.OrdinalIgnoreCase) ||
                   tool.Equals("create_directory", StringComparison.OrdinalIgnoreCase) ||
                   tool.Equals("delete_path", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasExecutedMutationTool(IReadOnlySet<string> executedTools)
    {
        return executedTools.Overlaps([
            "edit_file",
            "write_file",
            "create_directory",
            "delete_path"
        ]);
    }

    private static bool ShouldRejectNoToolCompletion(
        string prompt,
        string finalText,
        IReadOnlySet<string> executedTools,
        ProviderConfiguration config)
    {
        return HasAllowedMutationTool(config) &&
               !HasExecutedMutationTool(executedTools) &&
               LooksLikeNoToolCompletionForAction(prompt, finalText);
    }

    private static bool LooksLikeNoToolCompletionForAction(string prompt, string finalText)
    {
        return LooksLikeActionRequest(prompt) && LooksLikeCompletionClaim(finalText);
    }

    private static bool LooksLikeActionRequest(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("fix", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("update", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("edit", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("create", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("delete", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("write", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("implement", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("수정", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("고쳐", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("만들", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("생성", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("삭제", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("작성", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("구현", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeCompletionClaim(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("done", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("completed", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("fixed", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("updated", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("created", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("implemented", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("완료", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("수정했", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("고쳤", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("생성했", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("만들었", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("구현했", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildNoToolRetryInstruction(string finalText)
    {
        if (LooksLikeManualFallback(finalText))
        {
            return "Your previous answer gave manual copy/paste or permission instructions without using the allowed tools. " +
                   "This is non-interactive automation mode. Use the allowed tools now to make the requested change yourself, then verify it when a shell tool is allowed.";
        }

        return "Your previous answer claimed the requested change was complete without using an allowed mutation tool. " +
               "This is non-interactive automation mode. Use the allowed tools now to perform the requested change, then report only what the tools actually did.";
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
