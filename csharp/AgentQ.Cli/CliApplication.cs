using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Json;
using AgentQ.Tools;
using AgentQ.Core.Models;
using AgentQ.Core.Providers;

namespace AgentQ.Cli;

public sealed class CliApplication
{
    private readonly string[] _args;
    private readonly CliConfigurationLoader _configurationLoader;
    private readonly CliProviderResolver _providerResolver;
    private readonly CliPermissionEnforcerFactory _permissionEnforcerFactory;
    private readonly ToolRegistry _toolRegistry;
    private readonly ChatConversationHistory _history;
    private readonly CliToolLoopRunner _loopRunner;
    private readonly ConversationCompactor _compactor;

    public CliApplication(
        string[] args,
        CliConfigurationLoader configurationLoader,
        CliProviderResolver providerResolver,
        CliPermissionEnforcerFactory permissionEnforcerFactory,
        ToolRegistry toolRegistry,
        ChatConversationHistory history,
        CliToolLoopRunner loopRunner,
        ConversationCompactor compactor)
    {
        _args = args;
        _configurationLoader = configurationLoader;
        _providerResolver = providerResolver;
        _permissionEnforcerFactory = permissionEnforcerFactory;
        _toolRegistry = toolRegistry;
        _history = history;
        _loopRunner = loopRunner;
        _compactor = compactor;
    }

    public async Task RunAsync()
    {
        var config = await _configurationLoader.LoadAsync(_args);

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
                    "  [cyan]set AGENTQ_API_KEY=your-key[/]\n" +
                    "  [cyan]set OPENCODE_GO_API_KEY=your-key[/]")
            {
                Header = new PanelHeader("[yellow]Missing Configuration[/]"),
                Border = BoxBorder.Rounded
            });
        }

        var provider = _providerResolver.CreateOrFallback(config);
        var history = _history;
        var toolRegistry = _toolRegistry;
        var permissionEnforcers = _permissionEnforcerFactory.Create(invocation, config);
        var enforcer = permissionEnforcers.Enforcer;
        var consolePermissionEnforcer = permissionEnforcers.ConsoleEnforcer;
        var loopRunner = _loopRunner;
        var compactor = _compactor;

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

        var commandDispatcher = new InteractiveCommandDispatcher(new InteractiveCommandCallbacks
        {
            Clear = () =>
            {
                history.Clear();
                AnsiConsole.MarkupLine("[dim]Conversation cleared.[/]");
            },
            Help = () => ShowHelp(toolRegistry, _providerResolver.AvailableProviders),
            Setup = async () => provider = await RunSetupAsync(config, _providerResolver, toolRegistry),
            History = () => AnsiConsole.MarkupLine($"[dim]Messages in history:[/] [cyan]{history.MessageCount}[/]"),
            Compact = HandleCompactCommandAsync,
            Tools = () => ShowTools(toolRegistry),
            Permissions = argument => ShowOrUpdatePermissions(consolePermissionEnforcer, argument),
            Status = () => ShowStatus(provider, config, toolRegistry),
            RunTool = HandleRunToolCommandAsync,
            Provider = HandleProviderCommandAsync,
            Model = HandleModelCommand,
            BaseUrl = HandleBaseUrlCommand,
            ApiKey = HandleApiKeyCommand,
            Timeout = HandleTimeoutCommand,
            MaxTokens = HandleMaxTokensCommand,
            Config = HandleConfigCommandAsync,
            Save = HandleSaveCommandAsync,
            Load = HandleLoadCommandAsync,
            Unknown = ShowUnknownCommand
        });

        while (running)
        {
            try
            {
                var input = AnsiConsole.Ask<string>("[bold green]>[/]");

                if (input.StartsWith("/"))
                {
                    running = await commandDispatcher.DispatchAsync(input);
                }
                else if (!string.IsNullOrWhiteSpace(input))
                {
                    history.AddUserMessage(input);
                    using var cts = CreateTimeoutCancellation(config.TimeoutSeconds);
                    await SendAndDisplay(provider, config.Model, config.MaxTokens, history, toolRegistry, enforcer, loopRunner, cts?.Token ?? CancellationToken.None);
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

        async Task HandleCompactCommandAsync()
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

        async Task HandleRunToolCommandAsync(string argument)
        {
            if (!string.IsNullOrWhiteSpace(argument))
            {
                await RunTool(toolRegistry, enforcer, loopRunner, argument);
                return;
            }

            AnsiConsole.MarkupLine("[red]Usage:[/] /run <tool_name> {\"param\":\"value\"}");
        }

        async Task HandleProviderCommandAsync(string argument)
        {
            if (!string.IsNullOrWhiteSpace(argument))
            {
                var providerName = argument.ToLowerInvariant();
                if (_providerResolver.TryCreate(providerName, config.BaseUrl, config.ApiKey, out var newProvider) && newProvider != null)
                {
                    provider = newProvider;
                    config.Provider = providerName;
                    AnsiConsole.MarkupLine($"[green]Provider switched to [cyan]{providerName}[/].[/]");
                    ShowStatus(provider, config, toolRegistry);
                }
                else
                {
                    var availableProviders = string.Join(", ", _providerResolver.AvailableProviders);
                    AnsiConsole.Write(new Panel(
                        $"[red]Unknown or invalid provider:[/] {providerName}\n" +
                        $"[dim]Available:[/] {availableProviders}")
                    {
                        Border = BoxBorder.Rounded,
                        Header = new PanelHeader("[red]Provider Error[/]")
                    });
                }

                await Task.CompletedTask;
                return;
            }

            AnsiConsole.MarkupLine($"[dim]Current provider:[/] [cyan]{provider.Name}[/]");
            await Task.CompletedTask;
        }

        void HandleModelCommand(string argument)
        {
            if (!string.IsNullOrWhiteSpace(argument))
            {
                config.Model = argument;
                AnsiConsole.MarkupLine($"[green]Model set to [cyan]{config.Model}[/].[/]");
                ShowStatus(provider, config, toolRegistry);
                return;
            }

            AnsiConsole.MarkupLine($"[dim]Current model:[/] [cyan]{config.Model}[/]");
        }

        void HandleBaseUrlCommand(string argument)
        {
            if (!string.IsNullOrWhiteSpace(argument))
            {
                config.BaseUrl = argument;
                provider = _providerResolver.CreateOrFallback(config);
                AnsiConsole.MarkupLine($"[green]Base URL set to [cyan]{config.BaseUrl}[/].[/]");
                AnsiConsole.MarkupLine("[dim]Provider instance refreshed with the new base URL.[/]");
                ShowStatus(provider, config, toolRegistry);
                return;
            }

            AnsiConsole.MarkupLine($"[dim]Current base URL:[/] [cyan]{config.BaseUrl}[/]");
        }

        void HandleApiKeyCommand(string argument)
        {
            if (!string.IsNullOrWhiteSpace(argument))
            {
                config.ApiKey = argument;
                provider = _providerResolver.CreateOrFallback(config);
                AnsiConsole.MarkupLine($"[green]API key set to[/] [cyan]{MaskSecret(config.ApiKey)}[/]");
                return;
            }

            AnsiConsole.MarkupLine($"[dim]Current API key:[/] [cyan]{MaskSecret(config.ApiKey)}[/]");
        }

        void HandleTimeoutCommand(string argument)
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

        void HandleMaxTokensCommand(string argument)
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

        async Task HandleConfigCommandAsync(string argument)
        {
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
        }

        async Task HandleSaveCommandAsync(string argument)
        {
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
                return;
            }

            AnsiConsole.MarkupLine("[red]Usage:[/] /save <file_path>");
        }

        async Task HandleLoadCommandAsync(string argument)
        {
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
                return;
            }

            AnsiConsole.MarkupLine("[red]Usage:[/] /load <file_path>");
        }

        AnsiConsole.MarkupLine("\n[dim]Goodbye![/]");

    }

    private static async Task<ILlmProvider> RunSetupAsync(ProviderConfiguration config, CliProviderResolver providerResolver, ToolRegistry registry)
    {
        AnsiConsole.Write(new Rule { Title = "Setup", Style = Style.Parse("green") });

        var providers = providerResolver.AvailableProviders.OrderBy(name => name).ToArray();
        var providerName = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("제공자를 선택하세요")
                .AddChoices(providers));

        config.Provider = providerName;

        var defaultModel = providerName switch
        {
            "opencode-go" => string.IsNullOrWhiteSpace(config.Model) || config.Model.StartsWith("claude", StringComparison.OrdinalIgnoreCase)
                ? "kimi-k2.6"
                : config.Model,
            "openai" => string.IsNullOrWhiteSpace(config.Model) ? "gpt-4o" : config.Model,
            "anthropic" => string.IsNullOrWhiteSpace(config.Model) || config.Model.StartsWith("kimi-", StringComparison.OrdinalIgnoreCase)
                ? "claude-3-5-sonnet-latest"
                : config.Model,
            _ => string.IsNullOrWhiteSpace(config.Model) ? "default" : config.Model
        };

        var defaultBaseUrl = providerName switch
        {
            "opencode-go" => ProviderConfiguration.OpenCodeGoDefaultBaseUrl,
            "openai" => string.IsNullOrWhiteSpace(config.BaseUrl) || config.BaseUrl.Contains("anthropic", StringComparison.OrdinalIgnoreCase)
                ? "https://api.openai.com/v1"
                : config.BaseUrl,
            "anthropic" => string.IsNullOrWhiteSpace(config.BaseUrl) || config.BaseUrl.Contains("opencode.ai", StringComparison.OrdinalIgnoreCase)
                ? "https://api.anthropic.com"
                : config.BaseUrl,
            _ => config.BaseUrl
        };

        config.Model = AnsiConsole.Prompt(
            new TextPrompt<string>("모델")
                .DefaultValue(defaultModel));

        config.BaseUrl = AnsiConsole.Prompt(
            new TextPrompt<string>("기본 URL")
                .DefaultValue(defaultBaseUrl));

        var currentKeyHint = string.IsNullOrWhiteSpace(config.ApiKey)
            ? "비어 있음"
            : MaskSecret(config.ApiKey);
        var apiKey = AnsiConsole.Prompt(
            new TextPrompt<string>($"API 키 ([dim]{currentKeyHint}[/], 기존 값을 유지하려면 비워두세요)")
                .Secret()
                .AllowEmpty());

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            config.ApiKey = apiKey;
        }

        var save = AnsiConsole.Confirm("이 설정을 저장할까요?", defaultValue: true);
        if (save)
        {
            await ConfigStore.SaveAsync(config);
            AnsiConsole.MarkupLine($"[green]Configuration saved:[/] [cyan]{ConfigStore.PathValue.EscapeMarkup()}[/]");
        }

        var provider = providerResolver.CreateOrFallback(config);
        ShowStatus(provider, config, registry);
        return provider;
    }

    private static void ShowWelcome(ILlmProvider provider, ProviderConfiguration config, ToolRegistry registry)
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

    private static void ShowStatus(ILlmProvider provider, ProviderConfiguration config, ToolRegistry registry)
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
        table.AddRow("Saved Config", ConfigStore.Exists ? "[green]yes[/]" : "[yellow]no[/]");
        table.AddRow("Tools", $"[cyan]{registry.All.Count}[/]");
        AnsiConsole.Write(table);
    }

    private static async Task SendAndDisplay(ILlmProvider provider, string model, uint maxTokens, ChatConversationHistory history, ToolRegistry registry, IPermissionEnforcer enforcer, CliToolLoopRunner loopRunner, CancellationToken ct = default)
    {
        AnsiConsole.Write(new Rule { Title = "Assistant", Style = Style.Parse("blue") });

        try
        {
            AnsiConsole.MarkupLine("[dim]Thinking...[/]");

            await loopRunner.ExecuteConversationTurnAsync(
                provider,
                model,
                history,
                registry,
                enforcer,
                maxTokens: maxTokens,
                onToolExecution: toolName =>
                {
                    AnsiConsole.MarkupLine($"[bold yellow]Tool:[/] [cyan]{toolName}[/]");
                },
                onToolOutput: (_, output) =>
                {
                    var preview = Shorten(output, 160);
                    AnsiConsole.MarkupLine($"[green]Result:[/] [dim]{preview.EscapeMarkup()}[/]");
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

    private static async Task<NonInteractiveRunResult> RunNonInteractiveAsync(
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
            using var cts = CreateTimeoutCancellation(config.TimeoutSeconds);
            await loopRunner.ExecuteConversationTurnAsync(
                provider,
                config.Model,
                history,
                registry,
                enforcer,
                maxTokens: config.MaxTokens,
                onToolExecution: toolName => executedTools.Add(toolName),
                onToolOutput: (toolName, output) => toolOutputs.Add(ToolExecutionRecord.Create(toolName, output, isError: false)),
                onToolError: (toolName, error) =>
                {
                    toolOutputs.Add(ToolExecutionRecord.Create(toolName, error, isError: true));
                    toolErrors.Add(error);
                },
                onPermissionDenied: toolName => deniedTools.Add(toolName),
                ct: cts?.Token ?? CancellationToken.None);

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

    private static async Task RunTool(ToolRegistry registry, IPermissionEnforcer enforcer, CliToolLoopRunner loopRunner, string args)
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

    private static void ShowHelp(ToolRegistry registry, IEnumerable<string> providers)
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

    private static void ShowConfigDetails(ProviderConfiguration config)
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
        table.AddRow("Timeout", FormatTimeout(config.TimeoutSeconds));
        table.AddRow("Max Tokens", $"[cyan]{config.MaxTokens}[/]");
        AnsiConsole.Write(table);
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

    private static CancellationTokenSource? CreateTimeoutCancellation(int timeoutSeconds)
    {
        return timeoutSeconds <= 0
            ? null
            : new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
    }

    private static string FormatTimeout(int timeoutSeconds)
    {
        return timeoutSeconds <= 0
            ? "[cyan]disabled[/]"
            : $"[cyan]{timeoutSeconds} sec[/]";
    }

    private static void ShowTools(ToolRegistry registry)
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

    private static void ShowOrUpdatePermissions(ConsolePermissionEnforcer? enforcer, string argument)
    {
        if (enforcer == null)
        {
            AnsiConsole.MarkupLine("[yellow]권한 목록은 대화형 모드에서만 사용할 수 있습니다.[/]");
            return;
        }

        if (argument.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            enforcer.ClearSessionAllowedTools();
            AnsiConsole.MarkupLine("[green]이번 세션의 도구 허용 목록을 초기화했습니다.[/]");
            return;
        }

        if (!string.IsNullOrWhiteSpace(argument))
        {
            AnsiConsole.MarkupLine("[dim]사용법:[/] [cyan]/permissions[/] [dim]또는[/] [cyan]/permissions clear[/]");
            return;
        }

        var allowedTools = enforcer.SessionAllowedTools.ToArray();
        if (allowedTools.Length == 0)
        {
            AnsiConsole.MarkupLine("[dim]이번 세션에서 항상 허용된 도구가 없습니다.[/]");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("이번 세션에서 항상 허용된 도구");
        foreach (var tool in allowedTools)
        {
            table.AddRow($"[cyan]{tool}[/]");
        }

        AnsiConsole.Write(table);
    }

    private static void ShowUnknownCommand(string command)
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

    private static bool TryFormatJson(string value, out string prettyJson)
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

    private static string Shorten(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "...";
    }

    private static void WriteAutomationError(ProviderConfiguration config, string message, ProcessExitCode exitCode)
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
            }, AutomationSupport.JsonOutputOptions);
            Console.WriteLine(payload);
            return;
        }

        AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
    }

    private static void WriteNonInteractiveResult(ProviderConfiguration config, NonInteractiveRunResult result)
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
}
