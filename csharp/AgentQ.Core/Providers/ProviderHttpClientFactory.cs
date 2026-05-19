using System.Net.Http.Headers;

namespace AgentQ.Core.Providers;

public enum ProviderHttpAuthKind
{
    Bearer,
    Anthropic
}

public interface IProviderHttpClientFactory
{
    HttpClient CreateClient(string baseUrl, string apiKey, ProviderHttpAuthKind authKind);
}

public sealed class ProviderHttpClientFactory : IProviderHttpClientFactory, IDisposable
{
    public static ProviderHttpClientFactory Shared { get; } = new();

    private readonly SocketsHttpHandler _handler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5)
    };

    public HttpClient CreateClient(string baseUrl, string apiKey, ProviderHttpAuthKind authKind)
    {
        var client = new HttpClient(_handler, disposeHandler: false)
        {
            BaseAddress = new Uri(NormalizeBaseUrl(baseUrl))
        };

        ApplyAuthentication(client, apiKey, authKind);
        return client;
    }

    public void Dispose()
    {
        _handler.Dispose();
    }

    private static void ApplyAuthentication(HttpClient client, string apiKey, ProviderHttpAuthKind authKind)
    {
        switch (authKind)
        {
            case ProviderHttpAuthKind.Bearer:
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                }

                break;
            case ProviderHttpAuthKind.Anthropic:
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    client.DefaultRequestHeaders.Add("x-api-key", apiKey);
                }

                client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                break;
        }
    }

    private static string NormalizeBaseUrl(string baseUrl) =>
        baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : $"{baseUrl}/";
}
