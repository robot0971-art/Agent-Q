using AgentQ.Core.Providers;
using AgentQ.Providers.Anthropic;

namespace AgentQ.Cli;

public sealed class CliProviderResolver(
    ProviderFactory providerFactory,
    IProviderHttpClientFactory httpClientFactory)
{
    public IEnumerable<string> AvailableProviders => providerFactory.AvailableProviders;

    public bool TryCreate(string providerName, string baseUrl, string apiKey, out ILlmProvider? provider)
    {
        return providerFactory.TryGetProvider(providerName, baseUrl, apiKey, out provider);
    }

    public ILlmProvider CreateOrFallback(ProviderConfiguration config)
    {
        if (providerFactory.TryGetProvider(config.Provider, config.BaseUrl, config.ApiKey, out var provider) && provider != null)
        {
            return provider;
        }

        return new AnthropicProvider(httpClientFactory, config.BaseUrl, config.ApiKey);
    }
}
