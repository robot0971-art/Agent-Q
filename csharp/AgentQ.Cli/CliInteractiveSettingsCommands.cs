using AgentQ.Core.Providers;
using AgentQ.Tools;
using Spectre.Console;

namespace AgentQ.Cli;

public sealed class CliInteractiveSettingsCommands(
    CliProviderResolver providerResolver,
    IConfigStore configStore)
{
    public async Task<ILlmProvider> HandleProviderAsync(
        ILlmProvider provider,
        ProviderConfiguration config,
        ToolRegistry registry,
        string argument)
    {
        if (!string.IsNullOrWhiteSpace(argument))
        {
            var providerName = argument.ToLowerInvariant();
            if (providerResolver.TryCreate(providerName, config.BaseUrl, config.ApiKey, out var newProvider) && newProvider != null)
            {
                config.Provider = providerName;
                AnsiConsole.MarkupLine($"[green]Provider switched to [cyan]{providerName}[/].[/]");
                ShowStatus(newProvider, config, registry);
                await Task.CompletedTask;
                return newProvider;
            }

            var availableProviders = string.Join(", ", providerResolver.AvailableProviders);
            AnsiConsole.Write(new Panel(
                $"[red]Unknown or invalid provider:[/] {providerName}\n" +
                $"[dim]Available:[/] {availableProviders}")
            {
                Border = BoxBorder.Rounded,
                Header = new PanelHeader("[red]Provider Error[/]")
            });
            await Task.CompletedTask;
            return provider;
        }

        AnsiConsole.MarkupLine($"[dim]Current provider:[/] [cyan]{provider.Name}[/]");
        await Task.CompletedTask;
        return provider;
    }

    public void HandleModel(ILlmProvider provider, ProviderConfiguration config, ToolRegistry registry, string argument)
    {
        if (!string.IsNullOrWhiteSpace(argument))
        {
            config.Model = argument;
            AnsiConsole.MarkupLine($"[green]Model set to [cyan]{config.Model}[/].[/]");
            ShowStatus(provider, config, registry);
            return;
        }

        AnsiConsole.MarkupLine($"[dim]Current model:[/] [cyan]{config.Model}[/]");
    }

    public ILlmProvider HandleBaseUrl(ILlmProvider provider, ProviderConfiguration config, ToolRegistry registry, string argument)
    {
        if (!string.IsNullOrWhiteSpace(argument))
        {
            config.BaseUrl = argument;
            var refreshedProvider = providerResolver.CreateOrFallback(config);
            AnsiConsole.MarkupLine($"[green]Base URL set to [cyan]{config.BaseUrl}[/].[/]");
            AnsiConsole.MarkupLine("[dim]Provider instance refreshed with the new base URL.[/]");
            ShowStatus(refreshedProvider, config, registry);
            return refreshedProvider;
        }

        AnsiConsole.MarkupLine($"[dim]Current base URL:[/] [cyan]{config.BaseUrl}[/]");
        return provider;
    }

    public ILlmProvider HandleApiKey(ILlmProvider provider, ProviderConfiguration config, string argument)
    {
        if (!string.IsNullOrWhiteSpace(argument))
        {
            config.ApiKey = argument;
            var refreshedProvider = providerResolver.CreateOrFallback(config);
            AnsiConsole.MarkupLine($"[green]API key set to[/] [cyan]{MaskSecret(config.ApiKey)}[/]");
            return refreshedProvider;
        }

        AnsiConsole.MarkupLine($"[dim]Current API key:[/] [cyan]{MaskSecret(config.ApiKey)}[/]");
        return provider;
    }

    public void HandleTimeout(ProviderConfiguration config, string argument)
    {
        if (!string.IsNullOrWhiteSpace(argument) && int.TryParse(argument, out var timeout) && timeout >= 0)
        {
            config.TimeoutSeconds = timeout;
            AnsiConsole.MarkupLine(timeout == 0
                ? "[green]Timeout disabled for provider requests.[/]"
                : $"[green]Timeout set to [cyan]{config.TimeoutSeconds}[/] seconds.[/]");
            return;
        }

        if (string.IsNullOrWhiteSpace(argument))
        {
            AnsiConsole.MarkupLine(config.TimeoutSeconds == 0
                ? "[dim]Current timeout:[/] [cyan]disabled[/]"
                : $"[dim]Current timeout:[/] [cyan]{config.TimeoutSeconds}[/] seconds");
            return;
        }

        AnsiConsole.MarkupLine("[red]Timeout must be 0 or a positive integer in seconds.[/]");
    }

    public void HandleMaxTokens(ProviderConfiguration config, string argument)
    {
        if (!string.IsNullOrWhiteSpace(argument) && uint.TryParse(argument, out var maxTokens) && maxTokens > 0)
        {
            config.MaxTokens = maxTokens;
            AnsiConsole.MarkupLine($"[green]Max tokens set to [cyan]{config.MaxTokens}[/].[/]");
            return;
        }

        if (string.IsNullOrWhiteSpace(argument))
        {
            AnsiConsole.MarkupLine($"[dim]Current max tokens:[/] [cyan]{config.MaxTokens}[/]");
            return;
        }

        AnsiConsole.MarkupLine("[red]Max tokens must be a positive integer.[/]");
    }

    private void ShowStatus(ILlmProvider provider, ProviderConfiguration config, ToolRegistry registry)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Setting");
        table.AddColumn("Value");
        table.AddRow("Provider", $"[cyan]{provider.Name}[/]");
        table.AddRow("Model", $"[cyan]{config.Model}[/]");
        table.AddRow("Base URL", $"[cyan]{config.BaseUrl}[/]");
        table.AddRow("API Key", $"[cyan]{MaskSecret(config.ApiKey)}[/]");
        table.AddRow("Timeout", FormatTimeout(config.TimeoutSeconds));
        table.AddRow("Max Tokens", $"[cyan]{config.MaxTokens}[/]");
        table.AddRow("Saved Config", configStore.Exists ? "[green]yes[/]" : "[yellow]no[/]");
        table.AddRow("Tools", $"[cyan]{registry.All.Count}[/]");
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
