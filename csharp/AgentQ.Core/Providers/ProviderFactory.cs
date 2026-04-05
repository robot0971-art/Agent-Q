using System.Text.Json;

namespace AgentQ.Core.Providers;

public class ProviderFactory
{
    private readonly Dictionary<string, Func<string, string, ILlmProvider>> _providers = new();

    public void Register(string name, Func<string, string, ILlmProvider> factory)
    {
        _providers[name.ToLowerInvariant()] = factory;
    }

    public bool TryGetProvider(string name, string baseUrl, string apiKey, out ILlmProvider? provider)
    {
        provider = null;

        if (_providers.TryGetValue(name.ToLowerInvariant(), out var factory))
        {
            try
            {
                provider = factory(baseUrl, apiKey);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    public IEnumerable<string> AvailableProviders => _providers.Keys;
}

public class ProviderConfiguration
{
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 60; // Default timeout

    public static ProviderConfiguration FromEnvironment()
    {
        return new ProviderConfiguration
        {
            Provider = Environment.GetEnvironmentVariable("AGENTQ_PROVIDER") ?? 
                       Environment.GetEnvironmentVariable("CLAW_PROVIDER") ?? "anthropic",
            Model = Environment.GetEnvironmentVariable("AGENTQ_MODEL") ?? 
                    Environment.GetEnvironmentVariable("CLAW_MODEL") ?? string.Empty,
            BaseUrl = Environment.GetEnvironmentVariable("AGENTQ_BASE_URL") ?? 
                      Environment.GetEnvironmentVariable("CLAW_BASE_URL") ?? "https://api.anthropic.com",
            ApiKey = Environment.GetEnvironmentVariable("AGENTQ_API_KEY") ??
                     Environment.GetEnvironmentVariable("CLAW_API_KEY") ??
                     Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? string.Empty,
            TimeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("AGENTQ_TIMEOUT"), out var t) ? t : 60
        };
    }

    public static ProviderConfiguration FromArgs(string[] args)
    {
        var config = new ProviderConfiguration();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--provider":
                    if (i + 1 < args.Length) config.Provider = args[++i];
                    break;
                case "--model":
                    if (i + 1 < args.Length) config.Model = args[++i];
                    break;
                case "--base-url":
                    if (i + 1 < args.Length) config.BaseUrl = args[++i];
                    break;
                case "--api-key":
                    if (i + 1 < args.Length) config.ApiKey = args[++i];
                    break;
                case "--timeout":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var t)) config.TimeoutSeconds = t;
                    break;
            }
        }

        var envConfig = FromEnvironment();
        if (string.IsNullOrWhiteSpace(config.Provider)) config.Provider = envConfig.Provider;
        if (string.IsNullOrEmpty(config.Model)) config.Model = envConfig.Model;
        if (string.IsNullOrEmpty(config.BaseUrl)) config.BaseUrl = envConfig.BaseUrl;
        if (string.IsNullOrEmpty(config.ApiKey)) config.ApiKey = envConfig.ApiKey;
        if (config.TimeoutSeconds == 60 && envConfig.TimeoutSeconds != 60) config.TimeoutSeconds = envConfig.TimeoutSeconds;

        return config;
    }
}

