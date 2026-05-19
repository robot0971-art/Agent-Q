using AgentQ.Core.Models;
using AgentQ.Core.Providers;
using Spectre.Console;

namespace AgentQ.Cli;

public sealed class CliInteractivePersistenceCommands(
    IConfigStore configStore,
    ISessionStore sessionStore)
{
    public async Task HandleConfigAsync(ProviderConfiguration config, string argument)
    {
        if (argument.Equals("save", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await configStore.SaveAsync(config);
                AnsiConsole.MarkupLine($"[green]Configuration saved:[/] [cyan]{configStore.PathValue.EscapeMarkup()}[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed to save configuration:[/] {ex.Message}");
            }
        }
        else if (argument.Equals("show", StringComparison.OrdinalIgnoreCase))
        {
            ShowConfigDetails(config);
        }
        else if (argument.Equals("path", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine($"[dim]Config path:[/] [cyan]{configStore.PathValue.EscapeMarkup()}[/]");
        }
        else if (argument.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                configStore.Delete();
                AnsiConsole.MarkupLine($"[green]Saved configuration deleted:[/] [cyan]{configStore.PathValue.EscapeMarkup()}[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed to delete saved configuration:[/] {ex.Message}");
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[dim]Usage:[/] /config save | /config show | /config path | /config clear");
        }
    }

    public async Task HandleSaveAsync(ChatConversationHistory history, string argument)
    {
        if (!string.IsNullOrWhiteSpace(argument))
        {
            try
            {
                await sessionStore.SaveAsync(argument, history.Messages);
                AnsiConsole.MarkupLine($"[green]Session saved to [cyan]{argument}[/].[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed to save session:[/] {ex.Message}");
            }
            return;
        }

        AnsiConsole.MarkupLine("[red]Usage:[/] /save <file_path>");
    }

    public async Task HandleLoadAsync(ChatConversationHistory history, string argument)
    {
        if (!string.IsNullOrWhiteSpace(argument))
        {
            try
            {
                var messages = await sessionStore.LoadAsync(argument);
                history.Clear();
                history.AddRange(messages);
                AnsiConsole.MarkupLine($"[green]Session loaded from [cyan]{argument}[/].[/] [dim]({messages.Count} messages)[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed to load session:[/] {ex.Message}");
            }
            return;
        }

        AnsiConsole.MarkupLine("[red]Usage:[/] /load <file_path>");
    }

    private void ShowConfigDetails(ProviderConfiguration config)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Field");
        table.AddColumn("Value");
        table.AddRow("Config Path", $"[cyan]{configStore.PathValue.EscapeMarkup()}[/]");
        table.AddRow("Saved File", configStore.Exists ? "[green]present[/]" : "[yellow]missing[/]");
        table.AddRow("Provider", $"[cyan]{config.Provider}[/]");
        table.AddRow("Model", $"[cyan]{config.Model}[/]");
        table.AddRow("Base URL", $"[cyan]{config.BaseUrl}[/]");
        table.AddRow("API Key", $"[cyan]{MaskSecret(config.ApiKey)}[/]");
        table.AddRow("Timeout", FormatTimeout(config.TimeoutSeconds));
        table.AddRow("Max Tokens", $"[cyan]{config.MaxTokens}[/]");
        AnsiConsole.Write(table);
    }

    private static string FormatTimeout(int timeoutSeconds)
    {
        return timeoutSeconds <= 0
            ? "[cyan]disabled[/]"
            : $"[cyan]{timeoutSeconds}[/] seconds";
    }

    private static string MaskSecret(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(not set)";
        }

        if (value.Length <= 8)
        {
            return new string('*', value.Length);
        }

        return $"{value[..4]}...{value[^4..]}";
    }
}
