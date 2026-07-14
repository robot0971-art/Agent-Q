using AgentQ.Core.Providers;
using AgentQ.Tools;
using Spectre.Console;

namespace AgentQ.Cli;

public sealed class CliInteractivePresenter(IConfigStore configStore)
{
    public void ShowWelcome(ILlmProvider provider, ProviderConfiguration config, ToolRegistry registry)
    {
        var accentColor = new Color(214, 106, 34);
        var accentHighlight = new Color(247, 178, 103);
        var qIcon = new Rows(
        [
            new Markup("[#D66A22]   ___      [/][#F7B267]  AgentQ[/]"),
            new Markup("[#D66A22]  / _ \\     [/][dim]interactive coding cli[/]"),
            new Markup("[#D66A22] | | | |    [/][dim]tool-use assistant[/]"),
            new Markup("[#D66A22] | |_| |__  [/][dim]ready for work[/]"),
            new Markup("[#D66A22]  \\__\\_\\_/  [/][dim]type /help[/]")
        ]);

        AnsiConsole.Write(
            Align.Center(
                new Panel(qIcon)
                {
                    Border = BoxBorder.Rounded,
                    BorderStyle = new Style(accentHighlight),
                    Padding = new Padding(1, 0, 1, 0)
                }));
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Panel(
            $"[bold #D66A22]AgentQ CLI[/]\n" +
            $"[yellow]INTERNAL / EXPERIMENTAL / UNSUPPORTED[/]\n" +
            $"[dim]Use AgentQ Desktop for supported product workflows.[/]\n\n" +
            $"[dim]Provider:[/] [cyan]{provider.Name}[/]\n" +
            $"[dim]Model:[/] [cyan]{config.Model}[/]\n" +
            $"[dim]Base URL:[/] [cyan]{config.BaseUrl}[/]\n" +
            $"[dim]Tools:[/] [cyan]{registry.All.Count}[/]\n\n" +
            $"[dim]Type[/] [yellow]/help[/] [dim]for commands or[/] [yellow]/status[/] [dim]for current settings.[/]")
        {
            Header = new PanelHeader("[bold]Ready[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(accentColor)
        });
    }

    public void ShowStatus(ILlmProvider provider, ProviderConfiguration config, ToolRegistry registry)
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

    public void ShowHelp(ToolRegistry registry, IEnumerable<string> providers)
    {
        AnsiConsole.MarkupLine("[bold]Commands[/]");
        AnsiConsole.MarkupLine("  [yellow]/help[/]       Show help and examples");
        AnsiConsole.MarkupLine("  [yellow]/setup[/]      Configure provider, model, URL, and API key");
        AnsiConsole.MarkupLine("  [yellow]/status[/]     Show current provider, model, URL, timeout");
        AnsiConsole.MarkupLine("  [yellow]/clear[/]      Clear conversation history");
        AnsiConsole.MarkupLine("  [yellow]/history[/]    Show message count");
        AnsiConsole.MarkupLine("  [yellow]/compact[/]    Summarize older messages and keep recent context");
        AnsiConsole.MarkupLine("  [yellow]/tools[/]      List available tools");
        AnsiConsole.MarkupLine("  [yellow]/permissions[/] Show or clear session tool permissions");
        AnsiConsole.MarkupLine("  [yellow]/run[/]        Run a tool directly");
        AnsiConsole.MarkupLine("  [yellow]/provider[/]   Show or switch provider");
        AnsiConsole.MarkupLine("  [yellow]/model[/]      Show or set model");
        AnsiConsole.MarkupLine("  [yellow]/base-url[/]   Show or set base URL");
        AnsiConsole.MarkupLine("  [yellow]/api-key[/]    Show or set API key");
        AnsiConsole.MarkupLine("  [yellow]/timeout[/]    Show or set timeout in seconds");
        AnsiConsole.MarkupLine("  [yellow]/max-tokens[/] Show or set maximum output tokens");
        AnsiConsole.MarkupLine("  [yellow]/config[/]     Save, show, locate, or clear saved config");
        AnsiConsole.MarkupLine("  [yellow]/save[/]       Save current session");
        AnsiConsole.MarkupLine("  [yellow]/load[/]       Load a saved session");
        AnsiConsole.MarkupLine("  [yellow]/exit[/]       Exit the CLI");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold]Automation[/]");
        AnsiConsole.MarkupLine("  [cyan]agentq --prompt \"Summarize README\"[/]");
        AnsiConsole.MarkupLine("  [cyan]Get-Content prompt.txt | agentq --stdin[/]");
        AnsiConsole.MarkupLine("  [cyan]agentq --input prompt.txt[/]");
        AnsiConsole.MarkupLine("  [cyan]agentq --prompt \"Summarize README\" --json[/]");
        AnsiConsole.MarkupLine("  [cyan]agentq --prompt \"List files\" --yes[/]");
        AnsiConsole.MarkupLine("  [cyan]agentq --prompt \"Read README\" --allow-tool read_file[/]");
        AnsiConsole.MarkupLine("  [cyan]agentq --prompt \"Read README\" --allow-tool read_file --deny-tool bash[/]");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold]Examples[/]");
        AnsiConsole.MarkupLine("  [cyan]/provider openai[/]");
        AnsiConsole.MarkupLine("  [cyan]/provider opencode-go[/]");
        AnsiConsole.MarkupLine("  [cyan]/model gpt-5[/]");
        AnsiConsole.MarkupLine("  [cyan]/api-key sk-...[/]");
        AnsiConsole.MarkupLine("  [cyan]/base-url http://localhost:18080[/]");
        AnsiConsole.MarkupLine("  [cyan]/timeout 90[/]");
        AnsiConsole.MarkupLine("  [cyan]/max-tokens 8192[/]");
        AnsiConsole.MarkupLine("  [cyan]/config save[/]");
        AnsiConsole.MarkupLine("  [cyan]/config show[/]");
        AnsiConsole.MarkupLine("  [cyan]/permissions[/]");
        AnsiConsole.MarkupLine("  [cyan]/permissions clear[/]");
        AnsiConsole.MarkupLine("  [cyan]/compact[/]");
        AnsiConsole.MarkupLine("  [cyan]/run read_file {\"path\":\"README.md\",\"offset\":1,\"limit\":20}[/]");
        AnsiConsole.MarkupLine("  [cyan]/save session.json[/]");
        AnsiConsole.MarkupLine("  [cyan]/load session.json[/]");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine($"[dim]Available providers:[/] {string.Join(", ", providers)}");
        AnsiConsole.MarkupLine($"[dim]Registered tools:[/] {registry.All.Count}");
    }

    public void ShowTools(ToolRegistry registry)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Name");
        table.AddColumn("Permission");
        table.AddColumn("Description");

        foreach (var tool in registry.All.OrderBy(tool => tool.Name))
        {
            table.AddRow(
                $"[cyan]{tool.Name}[/]",
                tool.RequiresPermission ? "[yellow]yes[/]" : "[green]no[/]",
                tool.Description);
        }

        AnsiConsole.Write(table);
    }

    public void ShowUnknownCommand(string command)
    {
        var suggestions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["/tool"] = "/tools",
            ["/hist"] = "/history",
            ["/models"] = "/model",
            ["/providers"] = "/provider",
            ["/url"] = "/base-url",
            ["/stat"] = "/status"
        };

        if (suggestions.TryGetValue(command, out var suggested))
        {
            AnsiConsole.MarkupLine($"[red]Unknown command:[/] {command.EscapeMarkup()} [dim](did you mean {suggested}?)[/]");
            return;
        }

        AnsiConsole.MarkupLine($"[red]Unknown command:[/] {command.EscapeMarkup()}");
        AnsiConsole.MarkupLine("[dim]Type /help to see available commands.[/]");
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

    private static string FormatTimeout(int timeoutSeconds)
    {
        return timeoutSeconds <= 0
            ? "[cyan]disabled[/]"
            : $"[cyan]{timeoutSeconds} sec[/]";
    }
}
