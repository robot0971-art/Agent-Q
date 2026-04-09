using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Json;
using AgentQ.Cli;
using AgentQ.Tools;
using AgentQ.Core.Models;
using AgentQ.Core.Providers;
using AgentQ.Providers.Anthropic;
using AgentQ.Providers.OpenAi;

var initialConfig = ProviderConfiguration.FromArgs(args);
var persistedConfig = await ConfigStore.LoadAsync();
var config = initialConfig;

if (persistedConfig != null)
{
    if (string.IsNullOrEmpty(initialConfig.Provider)) config.Provider = persistedConfig.Provider;
    if (string.IsNullOrEmpty(initialConfig.Model)) config.Model = persistedConfig.Model;
    if (string.IsNullOrEmpty(initialConfig.BaseUrl)) config.BaseUrl = persistedConfig.BaseUrl;
    if (string.IsNullOrEmpty(initialConfig.ApiKey)) config.ApiKey = persistedConfig.ApiKey;
    if (initialConfig.TimeoutSeconds == 60) config.TimeoutSeconds = persistedConfig.TimeoutSeconds;
}

var providerFactory = new ProviderFactory();
providerFactory.Register("anthropic", (baseUrl, apiKey) => new AnthropicProvider(baseUrl, apiKey));
providerFactory.Register("openai", (baseUrl, apiKey) => new OpenAiCompatibleProvider(baseUrl, apiKey));

if (string.IsNullOrWhiteSpace(config.Provider))
{
    config.Provider = "anthropic";
}

if (string.IsNullOrWhiteSpace(config.BaseUrl))
{
    config.BaseUrl = "http://localhost:18080";
}

var invocation = await AutomationSupport.ResolveInvocationAsync(
    config,
    Console.IsInputRedirected,
    Console.In,
    path => File.ReadAllTextAsync(path));
if (invocation.ErrorMessage != null)
{
    Environment.ExitCode = (int)invocation.ErrorExitCode;
    WriteAutomationError(config, invocation.ErrorMessage, invocation.ErrorExitCode);
    return;
}

if (string.IsNullOrWhiteSpace(config.ApiKey) || string.IsNullOrWhiteSpace(config.Model))
{
    if (invocation.IsNonInteractive)
    {
        Environment.ExitCode = (int)ProcessExitCode.ConfigurationError;
        WriteAutomationError(
            config,
            "Model name or API key is missing. Set AGENTQ_MODEL and AGENTQ_API_KEY before running non-interactively.",
            ProcessExitCode.ConfigurationError);
        return;
    }

    AnsiConsole.Write(new Panel(
        "[yellow]Model name or API key is missing.[/]\n\n" +
        "Set environment variables or use CLI commands.\n" +
        "Examples:\n" +
        "  [cyan]/model qwen-plus[/]\n" +
        "  [cyan]/base-url http://localhost:18080[/]\n" +
        "  [cyan]set AGENTQ_API_KEY=your-key[/]")
    {
        Header = new PanelHeader("[yellow]Missing Configuration[/]"),
        Border = BoxBorder.Rounded
    });
}

var provider = CreateProviderOrFallback(providerFactory, config);
var history = new ChatConversationHistory();
var toolRegistry = CreateToolRegistry();
IPermissionEnforcer enforcer = invocation.IsNonInteractive
    ? new NonInteractivePermissionEnforcer(config.AllowToolsWithoutPrompt, config.AllowedToolNames, config.DeniedToolNames)
    : new ConsolePermissionEnforcer();
var loopRunner = new CliToolLoopRunner();
var compactor = new ConversationCompactor();

if (invocation.IsNonInteractive)
{
    var result = await RunNonInteractiveAsync(provider, config, history, toolRegistry, enforcer, loopRunner, invocation.Prompt!);
    Environment.ExitCode = (int)result.ExitCode;
    return;
}

if (Console.IsInputRedirected)
{
    Environment.ExitCode = (int)ProcessExitCode.InvalidArguments;
    AnsiConsole.MarkupLine("[red]Input is redirected. Use --stdin to read from standard input or --prompt/--input for one-shot execution.[/]");
    return;
}

var running = true;

ShowWelcome(provider, config, toolRegistry);

while (running)
{
    try
    {
        var input = AnsiConsole.Ask<string>("[bold green]>[/]");

        if (input.StartsWith("/"))
        {
            var parts = input.Split(' ', 2, StringSplitOptions.TrimEntries);
            var command = parts[0].ToLowerInvariant();
            var argument = parts.Length > 1 ? parts[1].Trim() : string.Empty;

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
                    ShowHelp(toolRegistry, providerFactory.AvailableProviders);
                    break;

                case "/history":
                    AnsiConsole.MarkupLine($"[dim]Messages in history:[/] [cyan]{history.MessageCount}[/]");
                    break;

                case "/compact":
                    try
                    {
                        using var compactCts = new CancellationTokenSource(TimeSpan.FromSeconds(config.TimeoutSeconds));
                        var result = await compactor.CompactAsync(provider, config.Model, history, compactCts.Token);
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
                    break;

                case "/tools":
                    ShowTools(toolRegistry);
                    break;

                case "/status":
                    ShowStatus(provider, config, toolRegistry);
                    break;

                case "/run":
                    if (!string.IsNullOrWhiteSpace(argument))
                    {
                        await RunTool(toolRegistry, enforcer, argument);
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[red]Usage:[/] /run <tool_name> {\"param\":\"value\"}");
                    }
                    break;

                case "/provider":
                    if (!string.IsNullOrWhiteSpace(argument))
                    {
                        var providerName = argument.ToLowerInvariant();
                        if (providerFactory.TryGetProvider(providerName, config.BaseUrl, config.ApiKey, out var newProvider) && newProvider != null)
                        {
                            provider = newProvider;
                            config.Provider = providerName;
                            AnsiConsole.MarkupLine($"[green]Provider switched to [cyan]{providerName}[/].[/]");
                            ShowStatus(provider, config, toolRegistry);
                        }
                        else
                        {
                            AnsiConsole.Write(new Panel(
                                $"[red]Unknown or invalid provider:[/] {providerName}\n" +
                                $"[dim]Available:[/] {string.Join(", ", providerFactory.AvailableProviders)}")
                            {
                                Border = BoxBorder.Rounded,
                                Header = new PanelHeader("[red]Provider Error[/]")
                            });
                        }
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[dim]Current provider:[/] [cyan]{provider.Name}[/]");
                    }
                    break;

                case "/model":
                    if (!string.IsNullOrWhiteSpace(argument))
                    {
                        config.Model = argument;
                        AnsiConsole.MarkupLine($"[green]Model set to [cyan]{config.Model}[/].[/]");
                        ShowStatus(provider, config, toolRegistry);
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[dim]Current model:[/] [cyan]{config.Model}[/]");
                    }
                    break;

                case "/base-url":
                    if (!string.IsNullOrWhiteSpace(argument))
                    {
                        config.BaseUrl = argument;
                        provider = CreateProviderOrFallback(providerFactory, config);
                        AnsiConsole.MarkupLine($"[green]Base URL set to [cyan]{config.BaseUrl}[/].[/]");
                        AnsiConsole.MarkupLine("[dim]Provider instance refreshed with the new base URL.[/]");
                        ShowStatus(provider, config, toolRegistry);
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[dim]Current base URL:[/] [cyan]{config.BaseUrl}[/]");
                    }
                    break;

                case "/api-key":
                    if (!string.IsNullOrWhiteSpace(argument))
                    {
                        config.ApiKey = argument;
                        provider = CreateProviderOrFallback(providerFactory, config);
                        AnsiConsole.MarkupLine($"[green]API key set to[/] [cyan]{MaskSecret(config.ApiKey)}[/]");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[dim]Current API key:[/] [cyan]{MaskSecret(config.ApiKey)}[/]");
                    }
                    break;

                case "/timeout":
                    if (!string.IsNullOrWhiteSpace(argument) && int.TryParse(argument, out var timeout) && timeout > 0)
                    {
                        config.TimeoutSeconds = timeout;
                        AnsiConsole.MarkupLine($"[green]Timeout set to [cyan]{config.TimeoutSeconds}[/] seconds.[/]");
                    }
                    else if (string.IsNullOrWhiteSpace(argument))
                    {
                        AnsiConsole.MarkupLine($"[dim]Current timeout:[/] [cyan]{config.TimeoutSeconds}[/] seconds");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[red]Timeout must be a positive integer in seconds.[/]");
                    }
                    break;

                case "/config":
                    if (argument.Equals("save", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            await ConfigStore.SaveAsync(config);
                            AnsiConsole.MarkupLine($"[green]Configuration saved:[/] [cyan]{ConfigStore.PathValue.EscapeMarkup()}[/]");
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
                        AnsiConsole.MarkupLine($"[dim]Config path:[/] [cyan]{ConfigStore.PathValue.EscapeMarkup()}[/]");
                    }
                    else if (argument.Equals("clear", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            ConfigStore.Delete();
                            AnsiConsole.MarkupLine($"[green]Saved configuration deleted:[/] [cyan]{ConfigStore.PathValue.EscapeMarkup()}[/]");
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
                    break;

                case "/save":
                    if (!string.IsNullOrWhiteSpace(argument))
                    {
                        try
                        {
                            await SessionStore.SaveAsync(argument, history.Messages);
                            AnsiConsole.MarkupLine($"[green]Session saved to [cyan]{argument}[/].[/]");
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[red]Failed to save session:[/] {ex.Message}");
                        }
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[red]Usage:[/] /save <file_path>");
                    }
                    break;

                case "/load":
                    if (!string.IsNullOrWhiteSpace(argument))
                    {
                        try
                        {
                            var messages = await SessionStore.LoadAsync(argument);
                            history.Clear();
                            history.AddRange(messages);
                            AnsiConsole.MarkupLine($"[green]Session loaded from [cyan]{argument}[/].[/] [dim]({messages.Count} messages)[/]");
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[red]Failed to load session:[/] {ex.Message}");
                        }
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[red]Usage:[/] /load <file_path>");
                    }
                    break;

                default:
                    ShowUnknownCommand(command);
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
        AnsiConsole.Write(new Panel($"[red]Unexpected error:[/] {ex.Message}\n[dim]{ex.StackTrace?.Split('\n').FirstOrDefault()}[/]")
        {
            Border = BoxBorder.Double,
            Header = new PanelHeader("[bold red]Critical Error[/]")
        });
    }
}

AnsiConsole.MarkupLine("\n[dim]Goodbye![/]");

static ILlmProvider CreateProviderOrFallback(ProviderFactory providerFactory, ProviderConfiguration config)
{
    if (providerFactory.TryGetProvider(config.Provider, config.BaseUrl, config.ApiKey, out var provider) && provider != null)
    {
        return provider;
    }

    return new AnthropicProvider(config.BaseUrl, config.ApiKey);
}

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

static void ShowWelcome(ILlmProvider provider, ProviderConfiguration config, ToolRegistry registry)
{
    var accentColor = new Color(214, 106, 34);
    var accentHighlight = new Color(247, 178, 103);
    var qIcon = new Rows(
    [
        new Markup("[#D66A22]████████╗[/][#F7B267]  ██████╗[/]"),
        new Markup("[#D66A22]██╔═══██║[/][#F7B267] ██╔═══██╗[/]"),
        new Markup("[#D66A22]██║   ██║[/][#F7B267] ██║   ██║[/]"),
        new Markup("[#D66A22]██║▄▄ ██║[/][#F7B267] ██║▄▄ ██║[/]"),
        new Markup("[#D66A22]╚██████╔╝[/][#F7B267] ╚██████╔╝[/]"),
        new Markup("[#D66A22] ╚══▀▀═╝[/][#F7B267]   ╚══▀▀═╝[/]"),
        new Markup("[dim]agentq[/] [#F7B267]//[/] [dim]interactive coding cli[/]")
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

static void ShowStatus(ILlmProvider provider, ProviderConfiguration config, ToolRegistry registry)
{
    var table = new Table().Border(TableBorder.Rounded);
    table.AddColumn("Setting");
    table.AddColumn("Value");
    table.AddRow("Provider", $"[cyan]{provider.Name}[/]");
    table.AddRow("Model", $"[cyan]{config.Model}[/]");
    table.AddRow("Base URL", $"[cyan]{config.BaseUrl}[/]");
    table.AddRow("API Key", $"[cyan]{MaskSecret(config.ApiKey)}[/]");
    table.AddRow("Timeout", $"[cyan]{config.TimeoutSeconds} sec[/]");
    table.AddRow("Saved Config", ConfigStore.Exists ? "[green]yes[/]" : "[yellow]no[/]");
    table.AddRow("Tools", $"[cyan]{registry.All.Count}[/]");
    AnsiConsole.Write(table);
}

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
                        ctx.Status("Receiving response...");
                    },
                    onToolExecution: toolName =>
                    {
                        ctx.Status($"Executing tool: [cyan]{toolName}[/]...");
                        AnsiConsole.MarkupLine($"[bold yellow]Tool:[/] [cyan]{toolName}[/]");
                    },
                    onToolOutput: (_, output) =>
                    {
                        var preview = Shorten(output, 160);
                        AnsiConsole.MarkupLine($"[green]Result:[/] [dim]{preview.EscapeMarkup()}[/]");
                        ctx.Status("Processing tool output...");
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
            });

        var lastMessage = history.Messages.LastOrDefault();
        if (lastMessage != null && lastMessage.Role == ChatRole.Assistant)
        {
            // 스트리밍 중간 출력 대신 최종 응답을 한 번만 그려서 콘솔 리렌더링에 지워지지 않게 합니다.
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

static async Task<NonInteractiveRunResult> RunNonInteractiveAsync(
    ILlmProvider provider,
    ProviderConfiguration config,
    ChatConversationHistory history,
    ToolRegistry registry,
    IPermissionEnforcer enforcer,
    CliToolLoopRunner loopRunner,
    string prompt)
{
    history.AddUserMessage(prompt);
    var toolOutputs = new List<ToolExecutionRecord>();
    var toolErrors = new List<string>();
    var deniedTools = new List<string>();
    var executedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(config.TimeoutSeconds));
        await loopRunner.ExecuteConversationTurnAsync(
            provider,
            config.Model,
            history,
            registry,
            enforcer,
            onToolExecution: toolName => executedTools.Add(toolName),
            onToolOutput: (toolName, output) => toolOutputs.Add(ToolExecutionRecord.Create(toolName, output, isError: false)),
            onToolError: (toolName, error) =>
            {
                toolOutputs.Add(ToolExecutionRecord.Create(toolName, error, isError: true));
                toolErrors.Add(error);
            },
            onPermissionDenied: toolName => deniedTools.Add(toolName),
            ct: cts.Token);

        var result = new NonInteractiveRunResult
        {
            FinalText = AutomationSupport.GetLatestAssistantText(history),
            MessageCount = history.MessageCount,
            Provider = provider.Name,
            Model = config.Model,
            BaseUrl = config.BaseUrl
        };
        result.AllowedTools.AddRange(config.AllowToolsWithoutPrompt ? ["*"] : config.AllowedToolNames);
        result.ConfiguredDeniedTools.AddRange(config.DeniedToolNames);
        result.ToolOutputs.AddRange(toolOutputs);
        result.ToolErrors.AddRange(toolErrors);
        result.DeniedTools.AddRange(deniedTools);
        result.ExecutedTools.AddRange(executedTools);
        WriteNonInteractiveResult(config, result);
        return result;
    }
    catch (OperationCanceledException)
    {
        WriteAutomationError(config, "The non-interactive request timed out or was cancelled.", ProcessExitCode.ProviderFailure);
        return new NonInteractiveRunResult
        {
            FinalText = string.Empty,
            MessageCount = history.MessageCount,
            ForcedExitCode = ProcessExitCode.ProviderFailure
        };
    }
    catch (Exception ex)
    {
        WriteAutomationError(config, $"Conversation error: {ex.Message}", ProcessExitCode.ProviderFailure);
        return new NonInteractiveRunResult
        {
            FinalText = string.Empty,
            MessageCount = history.MessageCount,
            ForcedExitCode = ProcessExitCode.ProviderFailure
        };
    }
}

static async Task RunTool(ToolRegistry registry, IPermissionEnforcer enforcer, string args)
{
    var loopRunner = new CliToolLoopRunner();
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
        AnsiConsole.Write(new Panel($"[red]Invalid JSON arguments:[/] {ex.Message}\n[dim]Example:[/] /run {toolName} {{\"param\":\"value\"}}")
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader("[red]JSON Syntax Error[/]")
        });
        return;
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

static void ShowHelp(ToolRegistry registry, IEnumerable<string> providers)
{
    AnsiConsole.MarkupLine("[bold]Commands[/]");
    AnsiConsole.MarkupLine("  [yellow]/help[/]       Show help and examples");
    AnsiConsole.MarkupLine("  [yellow]/status[/]     Show current provider, model, URL, timeout");
    AnsiConsole.MarkupLine("  [yellow]/clear[/]      Clear conversation history");
    AnsiConsole.MarkupLine("  [yellow]/history[/]    Show message count");
    AnsiConsole.MarkupLine("  [yellow]/compact[/]    Summarize older messages and keep recent context");
    AnsiConsole.MarkupLine("  [yellow]/tools[/]      List available tools");
    AnsiConsole.MarkupLine("  [yellow]/run[/]        Run a tool directly");
    AnsiConsole.MarkupLine("  [yellow]/provider[/]   Show or switch provider");
    AnsiConsole.MarkupLine("  [yellow]/model[/]      Show or set model");
    AnsiConsole.MarkupLine("  [yellow]/base-url[/]   Show or set base URL");
    AnsiConsole.MarkupLine("  [yellow]/api-key[/]    Show or set API key");
    AnsiConsole.MarkupLine("  [yellow]/timeout[/]    Show or set timeout in seconds");
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
    AnsiConsole.MarkupLine("  [cyan]/model gpt-5[/]");
    AnsiConsole.MarkupLine("  [cyan]/api-key sk-...[/]");
    AnsiConsole.MarkupLine("  [cyan]/base-url http://localhost:18080[/]");
    AnsiConsole.MarkupLine("  [cyan]/timeout 90[/]");
    AnsiConsole.MarkupLine("  [cyan]/config save[/]");
    AnsiConsole.MarkupLine("  [cyan]/config show[/]");
    AnsiConsole.MarkupLine("  [cyan]/compact[/]");
    AnsiConsole.MarkupLine("  [cyan]/run read_file {\"path\":\"README.md\",\"offset\":1,\"limit\":20}[/]");
    AnsiConsole.MarkupLine("  [cyan]/save session.json[/]");
    AnsiConsole.MarkupLine("  [cyan]/load session.json[/]");
    AnsiConsole.WriteLine();

    AnsiConsole.MarkupLine($"[dim]Available providers:[/] {string.Join(", ", providers)}");
    AnsiConsole.MarkupLine($"[dim]Registered tools:[/] {registry.All.Count}");
}

static void ShowConfigDetails(ProviderConfiguration config)
{
    var table = new Table().Border(TableBorder.Rounded);
    table.AddColumn("Field");
    table.AddColumn("Value");
    table.AddRow("Config Path", $"[cyan]{ConfigStore.PathValue.EscapeMarkup()}[/]");
    table.AddRow("Saved File", ConfigStore.Exists ? "[green]present[/]" : "[yellow]missing[/]");
    table.AddRow("Provider", $"[cyan]{config.Provider}[/]");
    table.AddRow("Model", $"[cyan]{config.Model}[/]");
    table.AddRow("Base URL", $"[cyan]{config.BaseUrl}[/]");
    table.AddRow("API Key", $"[cyan]{MaskSecret(config.ApiKey)}[/]");
    table.AddRow("Timeout", $"[cyan]{config.TimeoutSeconds} sec[/]");
    AnsiConsole.Write(table);
}

static string MaskSecret(string value)
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

static void ShowTools(ToolRegistry registry)
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

static void ShowUnknownCommand(string command)
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

static bool TryFormatJson(string value, out string prettyJson)
{
    try
    {
        using var document = JsonDocument.Parse(value);
        prettyJson = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        return true;
    }
    catch (JsonException)
    {
        prettyJson = string.Empty;
        return false;
    }
}

static string Shorten(string value, int maxLength)
{
    if (value.Length <= maxLength)
    {
        return value;
    }

    return value[..maxLength] + "...";
}

static void WriteAutomationError(ProviderConfiguration config, string message, ProcessExitCode exitCode)
{
    if (config.JsonOutput)
    {
        var payload = JsonSerializer.Serialize(new
        {
            success = false,
            exitCode = (int)exitCode,
            terminationReason = exitCode switch
            {
                ProcessExitCode.ConfigurationError => "configuration_error",
                ProcessExitCode.InvalidArguments => "invalid_arguments",
                ProcessExitCode.PermissionDenied => "permission_denied",
                ProcessExitCode.ToolFailure => "tool_error",
                _ => "provider_error"
            },
            error = message
        }, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(payload);
        return;
    }

    AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
}

static void WriteNonInteractiveResult(ProviderConfiguration config, NonInteractiveRunResult result)
{
    if (config.JsonOutput)
    {
        Console.WriteLine(AutomationSupport.SerializeJson(result));
        return;
    }

    if (!string.IsNullOrWhiteSpace(result.FinalText))
    {
        Console.WriteLine(result.FinalText);
    }
}
