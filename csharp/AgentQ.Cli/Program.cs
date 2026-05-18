using AgentQ.Cli;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    AnsiConsole.MarkupLine("[yellow]Ctrl+C is ignored. Type [cyan]/exit[/] to quit.[/]");
};

var services = new ServiceCollection();
services.AddAgentQCli(args);

await using var serviceProvider = services.BuildServiceProvider();
await serviceProvider.GetRequiredService<CliApplication>().RunAsync();
