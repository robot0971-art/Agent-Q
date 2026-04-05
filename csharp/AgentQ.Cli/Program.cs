using System.Text.Json;
using Spectre.Console;
using AgentQ.Cli;
using AgentQ.Tools;
using AgentQ.Core.Models;
using AgentQ.Core.Providers;
using AgentQ.Providers.Anthropic;
using AgentQ.Providers.OpenAi;

// Parse configuration from args and environment
var config = ProviderConfiguration.FromArgs(args);

// Create provider factory and register providers
var providerFactory = new ProviderFactory();
providerFactory.Register("anthropic", (baseUrl, apiKey) => new AnthropicProvider(baseUrl, apiKey));
providerFactory.Register("openai", (baseUrl, apiKey) => new OpenAiCompatibleProvider(baseUrl, apiKey));

// Get or create provider
ILlmProvider? provider = null;
if (string.IsNullOrEmpty(config.BaseUrl))
{
    // Default to localhost mock service
    config.BaseUrl = "http://localhost:18080";
}

if (!providerFactory.TryGetProvider(config.Provider, config.BaseUrl, config.ApiKey, out provider) || provider == null)
{
    // Fall back to anthropic provider
    provider = new AnthropicProvider(config.BaseUrl, config.ApiKey);
}

var history = new ChatConversationHistory();
var toolRegistry = CreateToolRegistry();
var enforcer = new ConsolePermissionEnforcer();
var loopRunner = new CliToolLoopRunner();
var running = true;

AnsiConsole.MarkupLine("[bold blue]AgentQ CLI[/]");
AnsiConsole.MarkupLine($"Provider: [cyan]{provider.Name}[/]");
AnsiConsole.MarkupLine($"Model: [cyan]{config.Model}[/]");
AnsiConsole.MarkupLine($"Base URL: [cyan]{config.BaseUrl}[/]");
AnsiConsole.MarkupLine($"Tools registered: [cyan]{toolRegistry.All.Count}[/]");
AnsiConsole.MarkupLine("Type [yellow]/help[/] for commands.\n");

while (running)
{
    try
    {
        var input = AnsiConsole.Ask<string>("[bold green]>[/]");

        if (input.StartsWith("/"))
        {
            var parts = input.Split(' ', 2);
            var command = parts[0].ToLowerInvariant();

            switch (command)
            {
                case "/exit":
                case "/quit":
                    running = false;
                    break;

                case "/clear":
                    history.Clear();
                    AnsiConsole.MarkupLine("[dim]Conversation cleared.[/]");
                    break;

                case "/help":
                    ShowHelp(toolRegistry);
                    break;

                case "/history":
                    AnsiConsole.MarkupLine($"[dim]{history.MessageCount} messages in history.[/]");
                    break;

                case "/tools":
                    ShowTools(toolRegistry);
                    break;

                case "/run":
                    if (parts.Length > 1)
                    {
                        await RunTool(toolRegistry, enforcer, parts[1].Trim());
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[red]Usage: /run <tool_name> {\"param\": \"value\"}[/]");
                    }
                    break;

                case "/provider":
                    if (parts.Length > 1)
                    {
                        var providerName = parts[1].Trim().ToLowerInvariant();
                        if (providerFactory.TryGetProvider(providerName, config.BaseUrl, config.ApiKey, out var newProvider) && newProvider != null)
                        {
                            provider = newProvider;
                            config.Provider = providerName;
                            AnsiConsole.MarkupLine($"[green]Provider switched to: {providerName}[/]");
                        }
                        else
                        {
                            AnsiConsole.Write(new Panel($"[red]Unknown or invalid provider: {providerName}[/]\n[dim]Available: {string.Join(", ", providerFactory.AvailableProviders)}[/]")
                            {
                                Border = BoxBorder.Rounded,
                                Title = "[red]Provider Error[/]"
                            });
                        }
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[dim]Current provider: [cyan]{provider.Name}[/][/]");
                    }
                    break;

                case "/model":
                    if (parts.Length > 1)
                    {
                        config.Model = parts[1].Trim();
                        AnsiConsole.MarkupLine($"[green]Model set to: [cyan]{config.Model}[/][/]");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[dim]Current model: [cyan]{config.Model}[/][/]");
                    }
                    break;

                case "/base-url":
                    if (parts.Length > 1)
                    {
                        config.BaseUrl = parts[1].Trim();
                        AnsiConsole.MarkupLine($"[green]Base URL set to: [cyan]{config.BaseUrl}[/][/]");
                        AnsiConsole.MarkupLine("[yellow]Note: You might need to re-select the provider to apply the new Base URL.[/]");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[dim]Current Base URL: [cyan]{config.BaseUrl}[/][/]");
                    }
                    break;

                case "/timeout":
                    if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out var timeout))
                    {
                        config.TimeoutSeconds = timeout;
                        AnsiConsole.MarkupLine($"[green]Timeout set to: [cyan]{config.TimeoutSeconds}[/] seconds[/]");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[dim]Current Timeout: [cyan]{config.TimeoutSeconds}[/] seconds[/]");
                    }
                    break;

                default:
                    AnsiConsole.MarkupLine($"[red]Unknown command: {command}[/]");
                    break;
            }
        }
        else if (!string.IsNullOrWhiteSpace(input))
        {
            history.AddUserMessage(input);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(config.TimeoutSeconds));
            await SendAndDisplay(provider, config.Model, history, toolRegistry, enforcer, loopRunner, cts.Token);
        }
    }
    catch (Exception ex)
    {
        AnsiConsole.Write(new Panel($"[red]An unexpected error occurred:[/] {ex.Message}\n[dim]{ex.StackTrace?.Split('\n').FirstOrDefault()}[/]")
        {
            Border = BoxBorder.Double,
            Title = "[bold red]Critical Error[/]"
        });
    }
}

AnsiConsole.MarkupLine("\n[dim]Goodbye![/]");

static ToolRegistry CreateToolRegistry()
{
    var registry = new ToolRegistry();
    registry.Register(new BashTool());
    registry.Register(new ReadFileTool());
    registry.Register(new WriteFileTool());
    registry.Register(new EditFileTool());
    registry.Register(new GrepTool());
    registry.Register(new GlobTool());
    registry.Register(new PluginEchoTool());
    return registry;
}

static async Task SendAndDisplay(ILlmProvider provider, string model, ChatConversationHistory history, ToolRegistry registry, IPermissionEnforcer enforcer, CliToolLoopRunner loopRunner, CancellationToken ct = default)
{
    AnsiConsole.Write(new Rule { Title = "Assistant", Style = Style.Parse("blue") });
    try
    {
        await loopRunner.ExecuteConversationTurnAsync(
            provider,
            model,
            history,
            registry,
            enforcer,
            onTextDelta: text => AnsiConsole.MarkupInterpolated($"[white]{text}[/]"),
            onToolExecution: toolName => AnsiConsole.MarkupLine($"[dim]Executing tool: [cyan]{toolName}[/][/dim]"),
            onToolOutput: output =>
            {
                var preview = output.Length > 100 ? output.Substring(0, 100) : output;
                AnsiConsole.MarkupLine($"[green]Tool output:[/] [dim]{preview}{(output.Length > 100 ? "..." : "")}[/]");
            },
            onToolError: error => AnsiConsole.MarkupLine($"[red]Tool error: {error}[/]"),
            onPermissionDenied: toolName => AnsiConsole.MarkupLine($"[yellow]Permission denied for {toolName}[/]"),
            ct: ct);
    }
    catch (OperationCanceledException)
    {
        AnsiConsole.Write(new Panel("[yellow]The operation was timed out or cancelled.[/]")
        {
            Border = BoxBorder.Rounded,
            Title = "[yellow]Timeout[/]"
        });
    }
    catch (Exception ex)
    {
        AnsiConsole.Write(new Panel($"[red]Error during conversation:[/] {ex.Message}")
        {
            Border = BoxBorder.Rounded,
            Title = "[red]API Error[/]"
        });
    }
    AnsiConsole.WriteLine();
}

static async Task RunTool(ToolRegistry registry, IPermissionEnforcer enforcer, string args)
{
    var loopRunner = new CliToolLoopRunner();
    var spaceIndex = args.IndexOf(' ');
    var toolName = spaceIndex >= 0 ? args.Substring(0, spaceIndex) : args;
    var jsonArgs = spaceIndex >= 0 ? args.Substring(spaceIndex + 1) : "{}";

    var tool = registry.Get(toolName);
    if (tool == null)
    {
        AnsiConsole.MarkupLine($"[red]Tool not found: {toolName}[/]");
        return;
    }

    if (tool.RequiresPermission)
    {
        if (!await enforcer.RequestPermissionAsync(tool.Name, tool.Description, jsonArgs))
        {
            AnsiConsole.MarkupLine("[yellow]Execution cancelled by user.[/]");
            return;
        }
    }

    Dictionary<string, object?> inputDict;
    try
    {
        inputDict = loopRunner.ParseJsonArguments(jsonArgs);
    }
    catch (JsonException ex)
    {
        AnsiConsole.Write(new Panel($"[red]Invalid JSON arguments:[/] {ex.Message}\n[dim]Example: /run {toolName} {{\"param\": \"value\"}}[/]")
        {
            Border = BoxBorder.Rounded,
            Title = "[red]JSON Syntax Error[/]"
        });
        return;
    }

    AnsiConsole.MarkupLine($"[dim]Running tool: [cyan]{toolName}[/][/dim]");

    try
    {
        var result = await tool.ExecuteAsync(inputDict);

        if (result.IsError)
        {
            AnsiConsole.MarkupLine($"[red]Tool execution failed: {result.Content}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]{result.Content}[/]");
        }
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[bold red]Unexpected tool error:[/] {ex.Message}");
    }
}

static void ShowHelp(ToolRegistry registry)
{
    AnsiConsole.MarkupLine("[bold]Commands:[/]");
    AnsiConsole.MarkupLine("  [yellow]/help[/]       - Show this help");
    AnsiConsole.MarkupLine("  [yellow]/clear[/]      - Clear conversation history");
    AnsiConsole.MarkupLine("  [yellow]/exit[/]       - Exit the CLI");
    AnsiConsole.MarkupLine("  [yellow]/history[/]    - Show message count");
    AnsiConsole.MarkupLine("  [yellow]/tools[/]      - List available tools");
    AnsiConsole.MarkupLine("  [yellow]/run[/]        - Run a tool directly");
    AnsiConsole.MarkupLine("  [yellow]/provider[/]   - Show or switch provider (anthropic, openai)");
    AnsiConsole.MarkupLine("  [yellow]/model[/]      - Show or set model name");
    AnsiConsole.MarkupLine("  [yellow]/base-url[/]   - Show or set API base URL");
    AnsiConsole.MarkupLine("  [yellow]/timeout[/]    - Show or set API timeout in seconds");
}

static void ShowTools(ToolRegistry registry)
{
    AnsiConsole.MarkupLine("[bold]Available Tools:[/]");
    var table = new Table();
    table.AddColumn("Name");
    table.AddColumn("Description");

    foreach (var tool in registry.All)
    {
        table.AddRow($"[cyan]{tool.Name}[/]", tool.Description);
    }

    AnsiConsole.Write(table);
}

