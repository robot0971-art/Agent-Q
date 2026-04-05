using Spectre.Console;
using Spectre.Console.Json;
using AgentQ.Tools;

namespace AgentQ.Cli;

public class ConsolePermissionEnforcer : IPermissionEnforcer
{
    public Task<bool> RequestPermissionAsync(string toolName, string description, string inputJson)
    {
        AnsiConsole.Write(new Rule { Title = $"Permission Required", Style = Style.Parse("yellow") });
        AnsiConsole.MarkupLine($"[bold yellow]Tool:[/] [cyan]{toolName}[/]");
        AnsiConsole.MarkupLine($"[bold yellow]Description:[/] {description}");
        AnsiConsole.MarkupLine($"[bold yellow]Arguments:[/]");
        AnsiConsole.Write(new JsonText(inputJson));
        AnsiConsole.WriteLine();

        var confirm = AnsiConsole.Confirm("Allow execution?", defaultValue: true);
        AnsiConsole.Write(new Rule { Style = Style.Parse("yellow") });
        
        return Task.FromResult(confirm);
    }
}

