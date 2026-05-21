using AgentQ.Core.Providers;

namespace AgentQ.Desktop.Services;

public sealed class DesktopEmbeddingClientFactory
{
    public const string DefaultEmbeddingModel = "text-embedding-3-small";

    public IEmbeddingClient Create(ProviderConfiguration config)
    {
        if (!SupportsProvider(config.EmbeddingProvider))
        {
            throw new NotSupportedException($"Embedding provider is not supported yet: {config.EmbeddingProvider}");
        }

        var baseUrl = string.IsNullOrWhiteSpace(config.EmbeddingBaseUrl)
            ? "https://api.openai.com/v1"
            : config.EmbeddingBaseUrl;
        return new OpenAiEmbeddingClient(OpenAiEmbeddingClient.CreateHttpClient(baseUrl, config.EmbeddingApiKey));
    }

    public static bool SupportsProvider(string provider)
    {
        return provider.Equals("openai", StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveEmbeddingModel(string provider)
    {
        return SupportsProvider(provider) ? DefaultEmbeddingModel : string.Empty;
    }
}
