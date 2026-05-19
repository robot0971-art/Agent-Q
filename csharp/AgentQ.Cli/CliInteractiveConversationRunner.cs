using AgentQ.Core.Models;
using AgentQ.Core.Providers;
using AgentQ.Tools;
using Spectre.Console;

namespace AgentQ.Cli;

public sealed class CliInteractiveConversationRunner
{
    public async Task SendAndDisplayAsync(
        ILlmProvider provider,
        string model,
        uint maxTokens,
        ChatConversationHistory history,
        ToolRegistry registry,
        IPermissionEnforcer enforcer,
        CliToolLoopRunner loopRunner,
        CancellationToken ct = default)
    {
        AnsiConsole.Write(new Rule { Title = "Assistant", Style = Style.Parse("blue") });

        try
        {
            AnsiConsole.MarkupLine("[dim]Thinking...[/]");

            await loopRunner.ExecuteConversationTurnAsync(
                provider,
                model,
                history,
                registry,
                enforcer,
                maxTokens: maxTokens,
                onToolExecution: toolName =>
                {
                    AnsiConsole.MarkupLine($"[bold yellow]Tool:[/] [cyan]{toolName}[/]");
                },
                onToolOutput: (_, output) =>
                {
                    var preview = Shorten(output, 160);
                    AnsiConsole.MarkupLine($"[green]Result:[/] [dim]{preview.EscapeMarkup()}[/]");
                },
                onToolError: (_, error) =>
                {
                    AnsiConsole.MarkupLine($"[red]Tool error:[/] {error.EscapeMarkup()}");
                },
                onPermissionDenied: toolName =>
                {
                    AnsiConsole.MarkupLine($"[yellow]Permission denied:[/] {toolName.EscapeMarkup()}");
                },
                ct: ct);

            var lastMessage = history.Messages.LastOrDefault();
            if (lastMessage != null && lastMessage.Role == ChatRole.Assistant)
            {
                // Render the final assistant text from history so console redraws do not erase it.
                var textContent = string.Join("\n", lastMessage.Content
                    .Where(c => c.Type == ContentType.Text)
                    .Select(c => c.Text)
                    .Where(text => !string.IsNullOrWhiteSpace(text)));

                if (!string.IsNullOrWhiteSpace(textContent))
                {
                    AnsiConsole.MarkupLine($"[white]{textContent.EscapeMarkup()}[/]");
                }
            }
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.Write(new Panel("[yellow]The request timed out or was cancelled.[/]")
            {
                Border = BoxBorder.Rounded,
                Header = new PanelHeader("[yellow]Timeout[/]")
            });
        }
        catch (Exception ex)
        {
            AnsiConsole.Write(new Panel($"[red]Conversation error:[/] {ex.Message.EscapeMarkup()}")
            {
                Border = BoxBorder.Rounded,
                Header = new PanelHeader("[red]API Error[/]")
            });
        }

        AnsiConsole.WriteLine();
    }

    private static string Shorten(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "...";
    }
}
