using Spectre.Console;
using AgentQ.Tools;
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
    private readonly IConfigStore _configStore;
    private readonly IInputFileReader _inputFileReader;
    private readonly ICliAutomationOutput _automationOutput;
    private readonly CliNonInteractiveRunner _nonInteractiveRunner;
    private readonly CliInteractivePersistenceCommands _persistenceCommands;
    private readonly CliInteractiveSettingsCommands _settingsCommands;
    private readonly CliInteractiveToolCommands _toolCommands;
    private readonly CliInteractiveSessionCommands _sessionCommands;
    private readonly CliInteractivePresenter _presenter;
    private readonly CliInteractiveConversationRunner _conversationRunner;

    public CliApplication(
        string[] args,
        CliConfigurationLoader configurationLoader,
        CliProviderResolver providerResolver,
        CliPermissionEnforcerFactory permissionEnforcerFactory,
        ToolRegistry toolRegistry,
        ChatConversationHistory history,
        CliToolLoopRunner loopRunner,
        IConfigStore configStore,
        IInputFileReader inputFileReader,
        ICliAutomationOutput automationOutput,
        CliNonInteractiveRunner nonInteractiveRunner,
        CliInteractivePersistenceCommands persistenceCommands,
        CliInteractiveSettingsCommands settingsCommands,
        CliInteractiveToolCommands toolCommands,
        CliInteractiveSessionCommands sessionCommands,
        CliInteractivePresenter presenter,
        CliInteractiveConversationRunner conversationRunner)
    {
        _args = args;
        _configurationLoader = configurationLoader;
        _providerResolver = providerResolver;
        _permissionEnforcerFactory = permissionEnforcerFactory;
        _toolRegistry = toolRegistry;
        _history = history;
        _loopRunner = loopRunner;
        _configStore = configStore;
        _inputFileReader = inputFileReader;
        _automationOutput = automationOutput;
        _nonInteractiveRunner = nonInteractiveRunner;
        _persistenceCommands = persistenceCommands;
        _settingsCommands = settingsCommands;
        _toolCommands = toolCommands;
        _sessionCommands = sessionCommands;
        _presenter = presenter;
        _conversationRunner = conversationRunner;
    }

    public async Task RunAsync()
    {
        var config = await _configurationLoader.LoadAsync(_args);

        var invocation = await AutomationSupport.ResolveInvocationAsync(
            config,
            Console.IsInputRedirected,
            Console.In,
            _inputFileReader.ReadAllTextAsync);
        if (invocation.ErrorMessage != null)
        {
            Environment.ExitCode = (int)invocation.ErrorExitCode;
            _automationOutput.WriteError(config, invocation.ErrorMessage, invocation.ErrorExitCode);
            return;
        }

        if (string.IsNullOrWhiteSpace(config.ApiKey) || string.IsNullOrWhiteSpace(config.Model))
        {
            if (invocation.IsNonInteractive)
            {
                Environment.ExitCode = (int)ProcessExitCode.ConfigurationError;
                _automationOutput.WriteError(
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

        if (invocation.IsNonInteractive)
        {
            var result = await _nonInteractiveRunner.RunAsync(provider, config, history, toolRegistry, enforcer, loopRunner, invocation.Prompt!);
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

        _presenter.ShowWelcome(provider, config, toolRegistry);

        var commandDispatcher = new InteractiveCommandDispatcher(new InteractiveCommandCallbacks
        {
            Clear = () =>
            {
                history.Clear();
                AnsiConsole.MarkupLine("[dim]Conversation cleared.[/]");
            },
            Help = () => _presenter.ShowHelp(toolRegistry, _providerResolver.AvailableProviders),
            Setup = async () => provider = await RunSetupAsync(config, _providerResolver, toolRegistry, _configStore, _presenter),
            History = () => AnsiConsole.MarkupLine($"[dim]Messages in history:[/] [cyan]{history.MessageCount}[/]"),
            Compact = () => _sessionCommands.CompactAsync(provider, config, history),
            Tools = () => _presenter.ShowTools(toolRegistry),
            Permissions = argument => _sessionCommands.ShowOrUpdatePermissions(consolePermissionEnforcer, argument),
            Status = () => _presenter.ShowStatus(provider, config, toolRegistry),
            RunTool = HandleRunToolCommandAsync,
            Provider = async argument => provider = await _settingsCommands.HandleProviderAsync(provider, config, toolRegistry, argument),
            Model = argument => _settingsCommands.HandleModel(provider, config, toolRegistry, argument),
            BaseUrl = argument => provider = _settingsCommands.HandleBaseUrl(provider, config, toolRegistry, argument),
            ApiKey = argument => provider = _settingsCommands.HandleApiKey(provider, config, argument),
            Timeout = argument => _settingsCommands.HandleTimeout(config, argument),
            MaxTokens = argument => _settingsCommands.HandleMaxTokens(config, argument),
            Config = argument => _persistenceCommands.HandleConfigAsync(config, argument),
            Save = argument => _persistenceCommands.HandleSaveAsync(history, argument),
            Load = argument => _persistenceCommands.HandleLoadAsync(history, argument),
            Unknown = _presenter.ShowUnknownCommand
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
                    await _conversationRunner.SendAndDisplayAsync(provider, config.Model, config.MaxTokens, history, toolRegistry, enforcer, loopRunner, cts?.Token ?? CancellationToken.None);
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

        async Task HandleRunToolCommandAsync(string argument)
        {
            if (!string.IsNullOrWhiteSpace(argument))
            {
                await _toolCommands.RunToolAsync(toolRegistry, enforcer, loopRunner, argument);
                return;
            }

            AnsiConsole.MarkupLine("[red]Usage:[/] /run <tool_name> {\"param\":\"value\"}");
        }

        AnsiConsole.MarkupLine("\n[dim]Goodbye![/]");

    }

    private static async Task<ILlmProvider> RunSetupAsync(
        ProviderConfiguration config,
        CliProviderResolver providerResolver,
        ToolRegistry registry,
        IConfigStore configStore,
        CliInteractivePresenter presenter)
    {
        AnsiConsole.Write(new Rule { Title = "Setup", Style = Style.Parse("green") });

        var providers = providerResolver.AvailableProviders.OrderBy(name => name).ToArray();
        var providerName = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select provider")
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
            new TextPrompt<string>("Model")
                .DefaultValue(defaultModel));

        config.BaseUrl = AnsiConsole.Prompt(
            new TextPrompt<string>("Base URL")
                .DefaultValue(defaultBaseUrl));

        var currentKeyHint = string.IsNullOrWhiteSpace(config.ApiKey)
            ? "not set"
            : MaskSecret(config.ApiKey);
        var apiKey = AnsiConsole.Prompt(
            new TextPrompt<string>($"API key ([dim]{currentKeyHint}[/], leave empty to keep current)")
                .Secret()
                .AllowEmpty());

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            config.ApiKey = apiKey;
        }

        var save = AnsiConsole.Confirm("Save this configuration?", defaultValue: true);
        if (save)
        {
            await configStore.SaveAsync(config);
            AnsiConsole.MarkupLine($"[green]Configuration saved:[/] [cyan]{configStore.PathValue.EscapeMarkup()}[/]");
        }

        var provider = providerResolver.CreateOrFallback(config);
        presenter.ShowStatus(provider, config, registry);
        return provider;
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


}
