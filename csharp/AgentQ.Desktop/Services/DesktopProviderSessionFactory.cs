using System.Net.Http.Headers;
using System.Net.Http;
using AgentQ.Core.Models;
using AgentQ.Core.Providers;
using AgentQ.Providers.Anthropic;
using AgentQ.Providers.OpenAi;

namespace AgentQ.Desktop.Services;

public interface IDesktopProviderSessionFactory
{
    ILlmProvider Create(ProviderConfiguration config, DesktopToolCallbacks? callbacks = null);
}

public sealed class DesktopProviderSessionFactory(IHttpClientFactory httpClientFactory) : IDesktopProviderSessionFactory
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));

    public ILlmProvider Create(ProviderConfiguration config, DesktopToolCallbacks? callbacks = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ILlmProvider provider = config.Provider.ToLowerInvariant() switch
        {
            "openai" => new OpenAiCompatibleProvider(CreateOpenAiClient(config.BaseUrl, config.ApiKey), ResolveModel(config, "gpt-4o")),
            "opencode-go" => new OpenAiCompatibleProvider(CreateOpenAiClient(config.BaseUrl, config.ApiKey), ResolveModel(config, "gpt-4o"), name: "opencode-go"),
            "anthropic" => new AnthropicProvider(CreateAnthropicClient(config.BaseUrl), config.ApiKey),
            _ => new OpenAiCompatibleProvider(CreateOpenAiClient(config.BaseUrl, config.ApiKey), ResolveModel(config, "gpt-4o"), name: config.Provider)
        };

        return new ResilientLlmProvider(provider, onRetry: retry =>
            callbacks?.OnRunStep?.Invoke(
                AgentRunState.Generating,
                $"Provider retry {retry.Attempt}/{retry.MaxRetries}",
                FormatRetryDetail(retry)));
    }

    private HttpClient CreateAnthropicClient(string baseUrl)
    {
        var client = _httpClientFactory.CreateClient("anthropic");
        client.BaseAddress = new Uri(baseUrl);
        return client;
    }

    private HttpClient CreateOpenAiClient(string baseUrl, string apiKey)
    {
        var client = _httpClientFactory.CreateClient("openai");
        client.BaseAddress = new Uri(OpenAiCompatibleProvider.NormalizeBaseUrl(baseUrl));
        if (!string.IsNullOrEmpty(apiKey)) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    private static string ResolveModel(ProviderConfiguration config, string fallback) =>
        string.IsNullOrWhiteSpace(config.Model) ? fallback : config.Model;

    private static string FormatRetryDetail(LlmProviderRetryInfo retry)
    {
        var status = retry.StatusCode == null ? "network/timeout" : $"HTTP {(int)retry.StatusCode} {retry.StatusCode}";
        var delay = retry.Delay == TimeSpan.Zero ? "immediately" : $"after {retry.Delay.TotalSeconds:0.#}s";
        return $"{retry.ProviderName} retrying {delay} because {status}: {DesktopPromptBuilder.Truncate(retry.ErrorMessage, 160)}";
    }
}
