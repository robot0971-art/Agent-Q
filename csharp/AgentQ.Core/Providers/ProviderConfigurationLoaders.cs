namespace AgentQ.Core.Providers;

public sealed class EnvironmentConfigurationLoader
{
    public ProviderConfiguration Load()
    {
        var provider = Environment.GetEnvironmentVariable("AGENTQ_PROVIDER") ??
                       Environment.GetEnvironmentVariable("CLAW_PROVIDER");
        var opencodeGoApiKey = Environment.GetEnvironmentVariable("OPENCODE_GO_API_KEY");
        var opencodeGoBaseUrl = Environment.GetEnvironmentVariable("OPENCODE_GO_BASE_URL");
        var opencodeGoModel = Environment.GetEnvironmentVariable("OPENCODE_GO_MODEL");
        var hasOpenCodeGoConfig = !string.IsNullOrWhiteSpace(opencodeGoApiKey) ||
                                  !string.IsNullOrWhiteSpace(opencodeGoBaseUrl) ||
                                  !string.IsNullOrWhiteSpace(opencodeGoModel);
        var resolvedProvider = provider ?? (hasOpenCodeGoConfig ? "opencode-go" : string.Empty);
        var defaultBaseUrl = resolvedProvider.Equals("opencode-go", StringComparison.OrdinalIgnoreCase)
            ? ProviderConfiguration.OpenCodeGoDefaultBaseUrl
            : string.Empty;

        return new ProviderConfiguration
        {
            Provider = resolvedProvider,
            Model = Environment.GetEnvironmentVariable("AGENTQ_MODEL") ??
                    Environment.GetEnvironmentVariable("CLAW_MODEL") ??
                    opencodeGoModel ?? string.Empty,
            BaseUrl = Environment.GetEnvironmentVariable("AGENTQ_BASE_URL") ??
                      Environment.GetEnvironmentVariable("CLAW_BASE_URL") ??
                      opencodeGoBaseUrl ?? defaultBaseUrl,
            ApiKey = Environment.GetEnvironmentVariable("AGENTQ_API_KEY") ??
                     Environment.GetEnvironmentVariable("CLAW_API_KEY") ??
                     opencodeGoApiKey ??
                     Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? string.Empty,
            TimeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("AGENTQ_TIMEOUT"), out var t) ? t : 60,
            MaxTokens = uint.TryParse(Environment.GetEnvironmentVariable("AGENTQ_MAX_TOKENS"), out var maxTokens) && maxTokens > 0
                ? maxTokens
                : 4096
        };
    }
}

public sealed class CommandLineConfigurationParser(EnvironmentConfigurationLoader? environmentLoader = null)
{
    private readonly EnvironmentConfigurationLoader _environmentLoader = environmentLoader ?? new EnvironmentConfigurationLoader();

    public ProviderConfiguration Parse(string[] args)
    {
        var config = new ProviderConfiguration();

        for (var i = 0; i < args.Length; i++)
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
                case "--max-tokens":
                    if (i + 1 < args.Length && uint.TryParse(args[++i], out var maxTokens) && maxTokens > 0) config.MaxTokens = maxTokens;
                    break;
                case "--prompt":
                    if (i + 1 < args.Length) config.Prompt = args[++i];
                    break;
                case "--stdin":
                    config.ReadPromptFromStdin = true;
                    break;
                case "--input":
                    if (i + 1 < args.Length) config.InputFilePath = args[++i];
                    break;
                case "--json":
                    config.JsonOutput = true;
                    break;
                case "--yes":
                    config.AllowToolsWithoutPrompt = true;
                    break;
                case "--allow-tool":
                    if (i + 1 < args.Length)
                    {
                        config.AllowedToolNames.Add(args[++i]);
                    }

                    break;
                case "--deny-tool":
                    if (i + 1 < args.Length)
                    {
                        config.DeniedToolNames.Add(args[++i]);
                    }

                    break;
            }
        }

        return MergeEnvironmentFallback(config, _environmentLoader.Load());
    }

    private static ProviderConfiguration MergeEnvironmentFallback(
        ProviderConfiguration config,
        ProviderConfiguration envConfig)
    {
        if (string.IsNullOrWhiteSpace(config.Provider)) config.Provider = envConfig.Provider;
        if (string.IsNullOrEmpty(config.Model)) config.Model = envConfig.Model;
        if (string.IsNullOrEmpty(config.BaseUrl)) config.BaseUrl = envConfig.BaseUrl;
        if (string.IsNullOrEmpty(config.ApiKey)) config.ApiKey = envConfig.ApiKey;
        if (config.TimeoutSeconds == 60 && envConfig.TimeoutSeconds != 60) config.TimeoutSeconds = envConfig.TimeoutSeconds;
        if (config.MaxTokens == 4096 && envConfig.MaxTokens != 4096) config.MaxTokens = envConfig.MaxTokens;

        return config;
    }
}
