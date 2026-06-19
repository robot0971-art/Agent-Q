using System.Text.Json;
using AgentQ.Api;
using AgentQ.Tools;
using Spectre.Console;
using Spectre.Console.Json;

namespace AgentQ.Cli;

public sealed class CliInteractiveToolCommands
{
    public async Task RunToolAsync(
        ToolRegistry registry,
        IPermissionEnforcer enforcer,
        CliToolLoopRunner loopRunner,
        string args)
    {
        var spaceIndex = args.IndexOf(' ');
        var toolName = spaceIndex >= 0 ? args[..spaceIndex] : args;
        var jsonArgs = spaceIndex >= 0 ? args[(spaceIndex + 1)..] : "{}";

        var tool = registry.Get(toolName);
        if (tool == null)
        {
            AnsiConsole.MarkupLine($"[red]Tool not found:[/] {toolName.EscapeMarkup()}");
            AnsiConsole.MarkupLine("[dim]Use /tools to list available tools.[/]");
            return;
        }

        Dictionary<string, object?> inputDict;
        try
        {
            inputDict = loopRunner.ParseJsonArguments(jsonArgs);
        }
        catch (JsonException ex)
        {
            AnsiConsole.Write(new Panel($"[red]Invalid JSON arguments:[/] {ex.Message}\n[dim]Example:[/] /run {toolName} {{\"param\":\"value\"}}")
            {
                Border = BoxBorder.Rounded,
                Header = new PanelHeader("[red]JSON Syntax Error[/]")
            });
            return;
        }

        var normalizedJsonArgs = JsonSerializer.Serialize(inputDict, AgentQJsonOptions.Indented);
        if (tool.RequiresPermission)
        {
            if (!await enforcer.RequestPermissionAsync(tool.Name, tool.Description, normalizedJsonArgs))
            {
                AnsiConsole.MarkupLine("[yellow]Execution cancelled by user.[/]");
                return;
            }
        }

        AnsiConsole.MarkupLine($"[dim]Running tool:[/] [cyan]{toolName}[/]");

        try
        {
            var result = await tool.ExecuteAsync(inputDict);

            if (result.IsError)
            {
                AnsiConsole.MarkupLine($"[red]Tool execution failed:[/] {result.Content.EscapeMarkup()}");
            }
            else
            {
                if (TryFormatJson(result.Content, out var prettyJson))
                {
                    AnsiConsole.Write(new Panel(new JsonText(prettyJson))
                    {
                        Header = new PanelHeader("[green]Tool Result[/]"),
                        Border = BoxBorder.Rounded
                    });
                }
                else
                {
                    AnsiConsole.MarkupLine($"[green]{result.Content.EscapeMarkup()}[/]");
                }
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[bold red]Unexpected tool error:[/] {ex.Message.EscapeMarkup()}");
        }
    }

    private static bool TryFormatJson(string value, out string prettyJson)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            prettyJson = JsonSerializer.Serialize(document.RootElement, AgentQJsonOptions.Indented);
            return true;
        }
        catch (JsonException)
        {
            prettyJson = string.Empty;
            return false;
        }
    }
}
