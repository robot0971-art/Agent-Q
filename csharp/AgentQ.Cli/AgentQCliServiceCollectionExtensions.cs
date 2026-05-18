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
        services.AddSingleton(CreateProviderFactory);
        services.AddSingleton<ITool, BashTool>();
        services.AddSingleton<ITool, ReadFileTool>();
        services.AddSingleton<ITool, WriteFileTool>();
        services.AddSingleton<ITool, EditFileTool>();
        services.AddSingleton<ITool, GrepTool>();
        services.AddSingleton<ITool, GlobTool>();
        services.AddSingleton<ITool, PluginEchoTool>();
        services.AddSingleton(CreateToolRegistry);
        services.AddSingleton<CliConfigurationLoader>();
        services.AddSingleton<CliProviderResolver>();
        services.AddSingleton<CliPermissionEnforcerFactory>();
        services.AddSingleton<ChatConversationHistory>();
        services.AddSingleton<CliToolLoopRunner>();
        services.AddSingleton<ConversationCompactor>();
        services.AddSingleton<CliApplication>();

        return services;
    }

    private static ProviderFactory CreateProviderFactory(IServiceProvider _)
    {
        var providerFactory = new ProviderFactory();
        providerFactory.Register("anthropic", (baseUrl, apiKey) => new AnthropicProvider(baseUrl, apiKey));
        providerFactory.Register("openai", (baseUrl, apiKey) => new OpenAiCompatibleProvider(baseUrl, apiKey));
        providerFactory.Register("opencode-go", (baseUrl, apiKey) => new OpenAiCompatibleProvider(baseUrl, apiKey, name: "opencode-go"));
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
