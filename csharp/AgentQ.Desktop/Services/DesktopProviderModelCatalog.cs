using AgentQ.Core.Providers;

namespace AgentQ.Desktop.Services;

public static class DesktopProviderModelCatalog
{
    private static readonly Dictionary<string, string[]> Catalog = new(StringComparer.OrdinalIgnoreCase)
    {
        ["opencode-go"] =
        [
            "kimi-k2.6",
            "kimi-k2.5",
            "qwen3.6-plus",
            "qwen3.5-plus",
            "deepseek-v4-pro",
            "deepseek-v4-flash",
            "glm-5.1",
            "glm-5",
            "mimo-v2.5-pro",
            "mimo-v2.5"
        ],
        ["openai"] =
        [
            "gpt-5.5",
            "gpt-5.4",
            "gpt-5.4-mini",
            "gpt-5.4-nano",
            "gpt-5.3-codex",
            "gpt-5.2",
            "gpt-5.2-chat-latest",
            "gpt-5.2-codex",
            "gpt-5.1",
            "gpt-5.1-codex",
            "gpt-5",
            "gpt-5-mini",
            "gpt-5-nano",
            "gpt-4.1",
            "gpt-4.1-mini",
            "gpt-4.1-nano",
            "gpt-4o",
            "gpt-4o-mini",
            "o3",
            "o4-mini"
        ],
        ["anthropic"] =
        [
            "claude-opus-4-7",
            "claude-sonnet-4-6",
            "claude-haiku-4-5",
            "claude-opus-4-1",
            "claude-opus-4",
            "claude-sonnet-4-5",
            "claude-sonnet-4",
            "claude-3-7-sonnet-latest",
            "claude-3-5-haiku-latest"
        ],
        ["google"] =
        [
            "gemini-2.5-pro",
            "gemini-2.5-flash",
            "gemini-2.0-flash"
        ],
        ["xai"] =
        [
            "grok-4",
            "grok-3",
            "grok-3-mini"
        ],
        ["deepseek"] =
        [
            "deepseek-chat",
            "deepseek-reasoner"
        ]
    };

    public static IReadOnlyCollection<string> Providers => Catalog.Keys.ToArray();

    public static IReadOnlyList<string> GetModels(string provider)
    {
        return Catalog.TryGetValue(provider, out var models)
            ? models
            : ["default"];
    }

    public static string GetDefaultModel(string provider)
    {
        return GetModels(provider)[0];
    }

    public static string GetDefaultBaseUrl(string provider, string currentBaseUrl)
    {
        return provider.ToLowerInvariant() switch
        {
            "opencode-go" => ProviderConfiguration.OpenCodeGoDefaultBaseUrl,
            "openai" => "https://api.openai.com/v1",
            "anthropic" => "https://api.anthropic.com",
            "google" => "https://generativelanguage.googleapis.com/v1beta/openai",
            "xai" => "https://api.x.ai/v1",
            "deepseek" => "https://api.deepseek.com",
            _ => currentBaseUrl
        };
    }
}
