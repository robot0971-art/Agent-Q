using System.Text.Json;
using AgentQ.Core.Models;
using AgentQ.Core.Providers;

namespace AgentQ.Cli;

public enum ProcessExitCode
{
    Success = 0,
    ConfigurationError = 2,
    InvalidArguments = 3,
    PermissionDenied = 4,
    ToolFailure = 5,
    ProviderFailure = 6
}

public sealed class AutomationInvocation
{
    public bool IsNonInteractive { get; init; }

    public string? Prompt { get; init; }

    public string? ErrorMessage { get; init; }

    public ProcessExitCode ErrorExitCode { get; init; } = ProcessExitCode.InvalidArguments;
}

public sealed class NonInteractiveRunResult
{
    public string FinalText { get; init; } = string.Empty;

    public List<string> ToolOutputs { get; } = [];

    public List<string> ToolErrors { get; } = [];

    public List<string> DeniedTools { get; } = [];

    public List<string> AllowedTools { get; } = [];

    public int MessageCount { get; init; }

    public ProcessExitCode? ForcedExitCode { get; init; }

    public ProcessExitCode ExitCode =>
        ForcedExitCode ?? (
        DeniedTools.Count > 0 ? ProcessExitCode.PermissionDenied :
        ToolErrors.Count > 0 ? ProcessExitCode.ToolFailure :
        ProcessExitCode.Success);

    public string TerminationReason =>
        ForcedExitCode switch
        {
            ProcessExitCode.ProviderFailure => "provider_error",
            ProcessExitCode.ConfigurationError => "configuration_error",
            ProcessExitCode.InvalidArguments => "invalid_arguments",
            ProcessExitCode.PermissionDenied => "permission_denied",
            ProcessExitCode.ToolFailure => "tool_error",
            _ when DeniedTools.Count > 0 => "permission_denied",
            _ when ToolErrors.Count > 0 => "tool_error",
            _ => "completed"
        };

    public object ToJsonEnvelope() => new
    {
        success = ExitCode == ProcessExitCode.Success,
        exitCode = (int)ExitCode,
        terminationReason = TerminationReason,
        finalText = FinalText,
        allowedTools = AllowedTools,
        deniedTools = DeniedTools,
        toolErrors = ToolErrors,
        toolOutputs = ToolOutputs,
        messageCount = MessageCount
    };
}

public static class AutomationSupport
{
    public static async Task<AutomationInvocation> ResolveInvocationAsync(
        ProviderConfiguration config,
        bool isInputRedirected,
        TextReader stdin,
        Func<string, Task<string>> readFileAsync)
    {
        var hasPrompt = !string.IsNullOrWhiteSpace(config.Prompt);
        var hasStdin = config.ReadPromptFromStdin;
        var hasInputFile = !string.IsNullOrWhiteSpace(config.InputFilePath);
        var sourceCount = (hasPrompt ? 1 : 0) + (hasStdin ? 1 : 0) + (hasInputFile ? 1 : 0);

        if (sourceCount > 1)
        {
            return new AutomationInvocation
            {
                IsNonInteractive = true,
                ErrorMessage = "Specify only one of --prompt, --stdin, or --input.",
                ErrorExitCode = ProcessExitCode.InvalidArguments
            };
        }

        if (sourceCount == 0)
        {
            return new AutomationInvocation { IsNonInteractive = false };
        }

        if (hasPrompt)
        {
            return new AutomationInvocation
            {
                IsNonInteractive = true,
                Prompt = config.Prompt
            };
        }

        if (hasInputFile)
        {
            try
            {
                var promptFromFile = await readFileAsync(config.InputFilePath);
                if (string.IsNullOrWhiteSpace(promptFromFile))
                {
                    return new AutomationInvocation
                    {
                        IsNonInteractive = true,
                        ErrorMessage = $"Input file '{config.InputFilePath}' was empty.",
                        ErrorExitCode = ProcessExitCode.InvalidArguments
                    };
                }

                return new AutomationInvocation
                {
                    IsNonInteractive = true,
                    Prompt = promptFromFile
                };
            }
            catch (Exception ex)
            {
                return new AutomationInvocation
                {
                    IsNonInteractive = true,
                    ErrorMessage = $"Failed to read input file '{config.InputFilePath}': {ex.Message}",
                    ErrorExitCode = ProcessExitCode.InvalidArguments
                };
            }
        }

        if (!isInputRedirected)
        {
            return new AutomationInvocation
            {
                IsNonInteractive = true,
                ErrorMessage = "Standard input is not redirected. Pipe content into agentq or use --prompt/--input.",
                ErrorExitCode = ProcessExitCode.InvalidArguments
            };
        }

        var promptFromStdin = await stdin.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(promptFromStdin))
        {
            return new AutomationInvocation
            {
                IsNonInteractive = true,
                ErrorMessage = "Standard input was empty.",
                ErrorExitCode = ProcessExitCode.InvalidArguments
            };
        }

        return new AutomationInvocation
        {
            IsNonInteractive = true,
            Prompt = promptFromStdin
        };
    }

    public static string GetLatestAssistantText(ChatConversationHistory history)
    {
        var assistantMessage = history.Messages.LastOrDefault(message => message.Role == ChatRole.Assistant);
        if (assistantMessage == null)
        {
            return string.Empty;
        }

        return string.Join(
            "\n",
            assistantMessage.Content
                .Where(content => content.Type == ContentType.Text)
                .Select(content => content.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    public static string SerializeJson(NonInteractiveRunResult result)
    {
        return JsonSerializer.Serialize(result.ToJsonEnvelope(), new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
