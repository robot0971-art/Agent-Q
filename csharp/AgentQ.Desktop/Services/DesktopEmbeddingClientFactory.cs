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
            : NormalizeEmbeddingBaseUrl(config.EmbeddingBaseUrl);
        return new OpenAiEmbeddingClient(OpenAiEmbeddingClient.CreateHttpClient(baseUrl, config.EmbeddingApiKey));
    }

    private static string NormalizeEmbeddingBaseUrl(string baseUrl)
    {
        var url = baseUrl.Trim();

        // Remove trailing slash for consistent processing
        url = url.TrimEnd('/');

        // If URL ends with /embeddings, remove it (user mistakenly included the endpoint)
        if (url.EndsWith("/embeddings", StringComparison.OrdinalIgnoreCase))
        {
            url = url[..^"/embeddings".Length];
        }

        // If URL doesn't end with /v1, append it
        if (!url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            url += "/v1";
        }

        return url + "/";
    }

    public static bool SupportsProvider(string provider)
    {
        return provider.Equals("openai", StringComparison.OrdinalIgnoreCase) ||
               provider.Equals("custom", StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveEmbeddingModel(string provider)
    {
        return SupportsProvider(provider) ? DefaultEmbeddingModel : string.Empty;
    }
}
