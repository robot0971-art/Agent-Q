using System.Text.Json;
using AgentQ.Core.Providers;
using Spectre.Console;

namespace AgentQ.Cli;

public interface ICliAutomationOutput
{
    void WriteError(ProviderConfiguration config, string message, ProcessExitCode exitCode);

    void WriteResult(ProviderConfiguration config, NonInteractiveRunResult result);
}

public sealed class CliAutomationOutput : ICliAutomationOutput
{
    public void WriteError(ProviderConfiguration config, string message, ProcessExitCode exitCode)
    {
        if (config.JsonOutput)
        {
            var payload = JsonSerializer.Serialize(new
            {
                success = false,
                exitCode = (int)exitCode,
                terminationReason = exitCode switch
                {
                    ProcessExitCode.ConfigurationError => "configuration_error",
                    ProcessExitCode.InvalidArguments => "invalid_arguments",
                    ProcessExitCode.PermissionDenied => "permission_denied",
                    ProcessExitCode.ToolFailure => "tool_error",
                    _ => "provider_error"
                },
                error = message
            }, AutomationSupport.JsonOutputOptions);
            Console.WriteLine(payload);
            return;
        }

        AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
    }

    public void WriteResult(ProviderConfiguration config, NonInteractiveRunResult result)
    {
        if (config.JsonOutput)
        {
            Console.WriteLine(AutomationSupport.SerializeJson(result));
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.FinalText))
        {
            Console.WriteLine(result.FinalText);
        }
    }
}
