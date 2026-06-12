using AgentQ.Core.Providers;

namespace AgentQ.Cli;

public sealed class CliConfigurationLoader(
    IConfigStore configStore,
    CommandLineConfigurationParser commandLineParser)
{
    public async Task<ProviderConfiguration> LoadAsync(string[] args)
    {
        var initialConfig = commandLineParser.Parse(args);
        var persistedConfig = await configStore.LoadAsync();
        var config = initialConfig;
        var timeoutSet = HasOption(args, "--timeout");
        var maxTokensSet = HasOption(args, "--max-tokens");

        if (persistedConfig != null)
        {
            if (string.IsNullOrEmpty(initialConfig.Provider)) config.Provider = persistedConfig.Provider;
            if (string.IsNullOrEmpty(initialConfig.Model)) config.Model = persistedConfig.Model;
            if (string.IsNullOrEmpty(initialConfig.BaseUrl)) config.BaseUrl = persistedConfig.BaseUrl;
            if (string.IsNullOrEmpty(initialConfig.ApiKey)) config.ApiKey = persistedConfig.ApiKey;
            if (!timeoutSet && initialConfig.TimeoutSeconds == 60) config.TimeoutSeconds = persistedConfig.TimeoutSeconds;
            if (!maxTokensSet && initialConfig.MaxTokens == 4096) config.MaxTokens = persistedConfig.MaxTokens;
        }

        if (string.IsNullOrWhiteSpace(config.Provider))
        {
            config.Provider = "anthropic";
        }

        if (string.IsNullOrWhiteSpace(config.BaseUrl))
        {
            config.BaseUrl = "http://localhost:18080";
        }

        return config;
    }

    private static bool HasOption(string[] args, string option)
    {
        return args.Any(arg => string.Equals(arg, option, StringComparison.OrdinalIgnoreCase));
    }
}
