using System.Text.Json;

namespace AgentQ.Core.Providers;

/// <summary>
/// LLM 제공자 팩토리
/// </summary>
public class ProviderFactory
{
    private readonly Dictionary<string, Func<string, string, ILlmProvider>> _providers = new();

    /// <summary>
    /// 제공자 등록
    /// </summary>
    /// <param name="name">제공자 이름</param>
    /// <param name="factory">제공자 생성 함수</param>
    public void Register(string name, Func<string, string, ILlmProvider> factory)
    {
        _providers[name.ToLowerInvariant()] = factory;
    }

    /// <summary>
    /// 제공자 조회 시도
    /// </summary>
    /// <param name="name">제공자 이름</param>
    /// <param name="baseUrl">기본 URL</param>
    /// <param name="apiKey">API 키</param>
    /// <param name="provider">제공자 인터페이스 (out)</param>
    /// <returns>조회 성공 여부</returns>
    public bool TryGetProvider(string name, string baseUrl, string apiKey, out ILlmProvider? provider)
    {
        provider = null;

        if (_providers.TryGetValue(name.ToLowerInvariant(), out var factory))
        {
            try
            {
                var innerProvider = factory(baseUrl, apiKey);
                provider = new ResilientLlmProvider(innerProvider); // Wrap with retry logic
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// 사용 가능한 제공자 목록
    /// </summary>
    public IEnumerable<string> AvailableProviders => _providers.Keys;
}

/// <summary>
/// 제공자 설정
/// </summary>
public class ProviderConfiguration
{
    /// <summary>
    /// 제공자 이름
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// 모델 이름
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// 기본 URL
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// API 키
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// 타임아웃 (초)
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60; // Default timeout

    /// <summary>
    /// 환경 변수에서 설정 로드
    /// </summary>
    /// <returns>제공자 설정</returns>
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

    /// <summary>
    /// 명령행 인수에서 설정 로드
    /// </summary>
    /// <param name="args">명령행 인수</param>
    /// <returns>제공자 설정</returns>
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

