using AgentQ.Core.Models;
using AgentQ.Core.Providers;
using Spectre.Console;

namespace AgentQ.Cli;

public sealed class CliInteractiveSessionCommands(ConversationCompactor compactor)
{
    public async Task CompactAsync(
        ILlmProvider provider,
        ProviderConfiguration config,
        ChatConversationHistory history)
    {
        try
        {
            using var compactCts = CreateTimeoutCancellation(config.TimeoutSeconds);
            var result = await compactor.CompactAsync(provider, config.Model, history, compactCts?.Token ?? CancellationToken.None);
            if (result.Applied)
            {
                AnsiConsole.MarkupLine($"[green]Compacted [cyan]{result.CompactedMessages}[/] messages into one summary.[/]");
                AnsiConsole.MarkupLine($"[dim]Messages now in history:[/] [cyan]{result.TotalMessagesAfter}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[yellow]{result.Reason?.EscapeMarkup() ?? "Nothing to compact."}[/]");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to compact conversation:[/] {ex.Message.EscapeMarkup()}");
        }
    }

    public void ShowOrUpdatePermissions(ConsolePermissionEnforcer? enforcer, string argument)
    {
        if (enforcer == null)
        {
            AnsiConsole.MarkupLine("[yellow]Permission history is only available in interactive mode.[/]");
            return;
        }

        if (argument.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            enforcer.ClearSessionAllowedTools();
            AnsiConsole.MarkupLine("[green]Cleared session tool permissions.[/]");
            return;
        }

        if (!string.IsNullOrWhiteSpace(argument))
        {
            AnsiConsole.MarkupLine("[dim]Usage:[/] [cyan]/permissions[/] [dim]or[/] [cyan]/permissions clear[/]");
            return;
        }

        var allowedTools = enforcer.SessionAllowedTools.ToArray();
        if (allowedTools.Length == 0)
        {
            AnsiConsole.MarkupLine("[dim]No tools are permanently allowed for this session.[/]");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Session-allowed tools");
        foreach (var tool in allowedTools)
        {
            table.AddRow($"[cyan]{tool}[/]");
        }

        AnsiConsole.Write(table);
    }

    private static CancellationTokenSource? CreateTimeoutCancellation(int timeoutSeconds)
    {
        return timeoutSeconds <= 0
            ? null
            : new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
    }
}
