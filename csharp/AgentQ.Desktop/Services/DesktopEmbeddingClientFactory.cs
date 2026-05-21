using AgentQ.Core.Providers;

namespace AgentQ.Desktop.Services;

public sealed class DesktopEmbeddingClientFactory
{
    public const string DefaultEmbeddingModel = "text-embedding-3-small";

    public IEmbeddingClient Create(ProviderConfiguration config)
    {
        if (!SupportsProvider(config.Provider))
        {
            throw new NotSupportedException($"Embedding provider is not supported yet: {config.Provider}");
        }

        var baseUrl = string.IsNullOrWhiteSpace(config.BaseUrl)
            ? DesktopProviderModelCatalog.GetDefaultBaseUrl(config.Provider, string.Empty)
            : config.BaseUrl;
        return new OpenAiEmbeddingClient(OpenAiEmbeddingClient.CreateHttpClient(baseUrl, config.ApiKey));
    }

    public static bool SupportsProvider(string provider)
    {
        return provider.Equals("openai", StringComparison.OrdinalIgnoreCase) ||
               provider.Equals("opencode-go", StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveEmbeddingModel(string provider)
    {
        return SupportsProvider(provider) ? DefaultEmbeddingModel : string.Empty;
    }
}
