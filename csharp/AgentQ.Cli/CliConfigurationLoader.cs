using AgentQ.Core.Providers;

namespace AgentQ.Cli;

public sealed class CliConfigurationLoader
{
    public async Task<ProviderConfiguration> LoadAsync(string[] args)
    {
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
            if (initialConfig.MaxTokens == 4096) config.MaxTokens = persistedConfig.MaxTokens;
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
}
