using AgentQ.Core.Providers;
using AgentQ.Providers.Anthropic;
using AgentQ.Providers.OpenAi;
using AgentQ.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace AgentQ.Cli;

public static class AgentQCliServiceCollectionExtensions
{
    public static IServiceCollection AddAgentQCli(this IServiceCollection services, string[] args)
    {
        services.AddSingleton(args);
        services.AddSingleton<IProviderHttpClientFactory, ProviderHttpClientFactory>();
        services.AddSingleton<EnvironmentConfigurationLoader>();
        services.AddSingleton<CommandLineConfigurationParser>();
        services.AddSingleton(CreateProviderFactory);
        services.AddSingleton<ITool, BashTool>();
        services.AddSingleton<ITool, ListDirectoryTool>();
        services.AddSingleton<ITool, ReadFileTool>();
        services.AddSingleton<ITool, WriteFileTool>();
        services.AddSingleton<ITool, CreateDirectoryTool>();
        services.AddSingleton<ITool, DeletePathTool>();
        services.AddSingleton<ITool, EditFileTool>();
        services.AddSingleton<ITool, GrepTool>();
        services.AddSingleton<ITool, GlobTool>();
        services.AddSingleton<ITool, WebSearchTool>();
        services.AddSingleton<ITool, PluginEchoTool>();
        services.AddSingleton(CreateToolRegistry);
        services.AddSingleton<IConfigStore, FileConfigStore>();
        services.AddSingleton<ISessionStore, FileSessionStore>();
        services.AddSingleton<IInputFileReader, InputFileReader>();
        services.AddSingleton<ICliAutomationOutput, CliAutomationOutput>();
        services.AddSingleton<CliNonInteractiveRunner>();
        services.AddSingleton<CliInteractivePersistenceCommands>();
        services.AddSingleton<CliInteractiveSettingsCommands>();
        services.AddSingleton<CliInteractiveToolCommands>();
        services.AddSingleton<CliInteractiveSessionCommands>();
        services.AddSingleton<CliInteractivePresenter>();
        services.AddSingleton<CliInteractiveConversationRunner>();
        services.AddSingleton<CliConfigurationLoader>();
        services.AddSingleton<CliProviderResolver>();
        services.AddSingleton<CliPermissionEnforcerFactory>();
        services.AddSingleton<ChatConversationHistory>();
        services.AddSingleton<CliToolLoopRunner>();
        services.AddSingleton<ConversationCompactor>();
        services.AddSingleton<CliApplication>();

        return services;
    }

    private static ProviderFactory CreateProviderFactory(IServiceProvider services)
    {
        var httpClientFactory = services.GetRequiredService<IProviderHttpClientFactory>();
        var providerFactory = new ProviderFactory();
        providerFactory.Register("anthropic", (baseUrl, apiKey) => new AnthropicProvider(httpClientFactory, baseUrl, apiKey));
        providerFactory.Register("openai", (baseUrl, apiKey) => new OpenAiCompatibleProvider(httpClientFactory, baseUrl, apiKey));
        providerFactory.Register("opencode-go", (baseUrl, apiKey) => new OpenAiCompatibleProvider(httpClientFactory, baseUrl, apiKey, name: "opencode-go"));
        return providerFactory;
    }

    private static ToolRegistry CreateToolRegistry(IServiceProvider services)
    {
        var registry = new ToolRegistry();

        foreach (var tool in services.GetServices<ITool>())
        {
            registry.Register(tool);
        }

        return registry;
    }
}
