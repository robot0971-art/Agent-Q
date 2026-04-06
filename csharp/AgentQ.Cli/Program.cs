using System.Text.Json;
using System.Linq;
using Spectre.Console;
using AgentQ.Cli;
using AgentQ.Tools;
using AgentQ.Core.Models;
using AgentQ.Core.Providers;
using AgentQ.Providers.Anthropic;
using AgentQ.Providers.OpenAi;

// Parse configuration from args and environment
var initialConfig = ProviderConfiguration.FromArgs(args);

// Load persisted config if available and merge (Arguments/Env take precedence)
var persistedConfig = await ConfigStore.LoadAsync();
var config = initialConfig;

if (persistedConfig != null)
{
    // Use persisted values only if they weren't explicitly provided by user
    if (string.IsNullOrEmpty(initialConfig.Provider)) config.Provider = persistedConfig.Provider;
    if (string.IsNullOrEmpty(initialConfig.Model)) config.Model = persistedConfig.Model;
    if (string.IsNullOrEmpty(initialConfig.BaseUrl)) config.BaseUrl = persistedConfig.BaseUrl;
    if (string.IsNullOrEmpty(initialConfig.ApiKey)) config.ApiKey = persistedConfig.ApiKey;
    if (initialConfig.TimeoutSeconds == 60) config.TimeoutSeconds = persistedConfig.TimeoutSeconds;
}

// Create provider factory and register providers
var providerFactory = new ProviderFactory();
providerFactory.Register("anthropic", (baseUrl, apiKey) => new AnthropicProvider(baseUrl, apiKey));
providerFactory.Register("openai", (baseUrl, apiKey) => new OpenAiCompatibleProvider(baseUrl, apiKey));

// Get or create provider
ILlmProvider? provider = null;
if (string.IsNullOrEmpty(config.BaseUrl))
{
    // Default to localhost mock service if no URL is provided
    config.BaseUrl = "http://localhost:18080";
}

// Validation: Check if required settings are missing
if (string.IsNullOrEmpty(config.ApiKey) || string.IsNullOrEmpty(config.Model))
{
    AnsiConsole.Write(new Panel(
        "[yellow]Warning: Model name or API Key is missing.[/]\n\n" +
        "Please set environment variables or use commands:\n" +
        "  - [cyan]set AGENTQ_MODEL=qwen-plus[/]\n" +
        "  - [cyan]set AGENTQ_API_KEY=your-key[/]\n\n" +
        "Or use [bold]/model[/], [bold]/base-url[/] commands inside the CLI."
    )
    {
        Header = new PanelHeader("[yellow]Missing Configuration[/]"),
        Border = BoxBorder.Rounded
    });
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
                                Header = new PanelHeader("[red]Provider Error[/]")
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

                case "/config":
                    if (parts.Length > 1 && parts[1].Trim().ToLowerInvariant() == "save")
                    {
                        try
                        {
                            await ConfigStore.SaveAsync(config);
                            AnsiConsole.MarkupLine("[green]Current configuration saved successfully![/]");
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[red]Failed to save configuration:[/] {ex.Message}");
                        }
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[dim]Usage: /config save (Saves current provider, model, url, and timeout to config.json)[/]");
                    }
                    break;

                case "/save":
                    if (parts.Length > 1)
                    {
                        var filePath = parts[1].Trim();
                        try
                        {
                            await SessionStore.SaveAsync(filePath, history.Messages);
                            AnsiConsole.MarkupLine($"[green]Session saved to: [cyan]{filePath}[/][/]");
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[red]Failed to save session:[/] {ex.Message}");
                        }
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[red]Usage: /save <file_path>[/]");
                    }
                    break;

                case "/load":
                    if (parts.Length > 1)
                    {
                        var filePath = parts[1].Trim();
                        try
                        {
                            var messages = await SessionStore.LoadAsync(filePath);
                            history.Clear();
                            history.AddRange(messages);
                            AnsiConsole.MarkupLine($"[green]Session loaded from: [cyan]{filePath}[/] ({messages.Count} messages)[/]");
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[red]Failed to load session:[/] {ex.Message}");
                        }
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[red]Usage: /load <file_path>[/]");
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
            Header = new PanelHeader("[bold red]Critical Error[/]")
        });
    }
}

AnsiConsole.MarkupLine("\n[dim]Goodbye![/]");

/// <summary>
/// 도구 레지스트리 생성
/// </summary>
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

/// <summary>
/// 메시지 전송 및 표시
/// </summary>
static async Task SendAndDisplay(ILlmProvider provider, string model, ChatConversationHistory history, ToolRegistry registry, IPermissionEnforcer enforcer, CliToolLoopRunner loopRunner, CancellationToken ct = default)
{
    AnsiConsole.Write(new Rule { Title = "Assistant", Style = Style.Parse("blue") });
    
    try
    {
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Thinking...", async ctx => 
            {
                await loopRunner.ExecuteConversationTurnAsync(
                    provider,
                    model,
                    history,
                    registry,
                    enforcer,
                    onTextDelta: text => 
                    {
                        // Update status or stop it when text starts streaming
                        ctx.Status("Receiving response...");
                        AnsiConsole.MarkupInterpolated($"[white]{text}[/]");
                    },
                    onToolExecution: toolName => 
                    {
                        ctx.Status($"Executing tool: [cyan]{toolName}[/]...");
                        AnsiConsole.MarkupLine($"[dim]Executing tool: [cyan]{toolName}[/][/dim]");
                    },
                    onToolOutput: output =>
                    {
                        var preview = output.Length > 100 ? output.Substring(0, 100) : output;
                        AnsiConsole.MarkupLine($"[green]Tool output:[/] [dim]{preview}{(output.Length > 100 ? "..." : "")}[/]");
                        ctx.Status("Processing tool output...");
                    },
                    onToolError: error => AnsiConsole.MarkupLine($"[red]Tool error: {error}[/]"),
                    onPermissionDenied: toolName => AnsiConsole.MarkupLine($"[yellow]Permission denied for {toolName}[/]"),
                    ct: ct);
            });

        // After the streaming is done, re-render the final response as Markdown for syntax highlighting
        var lastMessage = history.Messages.LastOrDefault();
        if (lastMessage != null && lastMessage.Role == ChatRole.Assistant)
        {
            // Clear the streaming output by writing a newline and re-rendering properly if needed
            AnsiConsole.WriteLine();
            var textContent = string.Join("\n", lastMessage.Content
                .Where(c => c.Type == ContentType.Text)
                .Select(c => c.Text));
            
            if (!string.IsNullOrWhiteSpace(textContent))
            {
                // Fallback to regular markup if Markdown class is missing for some reason
                AnsiConsole.MarkupLine($"\n[white]{textContent}[/]");
            }
        }
    }
    catch (OperationCanceledException)
    {
        AnsiConsole.Write(new Panel("[yellow]The operation was timed out or cancelled.[/]")
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader("[yellow]Timeout[/]")
        });
    }
    catch (Exception ex)
    {
        AnsiConsole.Write(new Panel($"[red]Error during conversation:[/] {ex.Message}")
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader("[red]API Error[/]")
        });
    }
    AnsiConsole.WriteLine();
}

/// <summary>
/// 도구 직접 실행
/// </summary>
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
            Header = new PanelHeader("[red]JSON Syntax Error[/]")
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

/// <summary>
/// 도움말 표시
/// </summary>
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

/// <summary>
/// 도구 목록 표시
/// </summary>
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
