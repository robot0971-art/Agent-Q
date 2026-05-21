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
    public const string OpenCodeGoDefaultBaseUrl = "https://opencode.ai/zen/go/v1";

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
    /// 임베딩 제공자 이름
    /// </summary>
    public string EmbeddingProvider { get; set; } = "openai";

    /// <summary>
    /// 임베딩 모델 이름
    /// </summary>
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";

    /// <summary>
    /// 임베딩 API 기본 URL
    /// </summary>
    public string EmbeddingBaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>
    /// 임베딩 API 키
    /// </summary>
    public string EmbeddingApiKey { get; set; } = string.Empty;

    /// <summary>
    /// 타임아웃 (초)
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60; // Default timeout

    /// <summary>
    /// 최대 출력 토큰 수
    /// </summary>
    public uint MaxTokens { get; set; } = 4096;

    /// <summary>
    /// 데스크톱 UI 글자 크기
    /// </summary>
    public double DesktopFontSize { get; set; } = 14;

    /// <summary>
    /// 데스크톱에서 프로젝트 컨텍스트를 자동 첨부할지 여부
    /// </summary>
    public bool DesktopAutoAttachWorkspaceContext { get; set; } = true;

    /// <summary>
    /// 데스크톱에서 메시지의 링크를 자동으로 읽을지 여부
    /// </summary>
    public bool DesktopAutoFetchLinks { get; set; } = true;

    /// <summary>
    /// 데스크톱 에이전트 작업 모드
    /// </summary>
    public string DesktopWorkMode { get; set; } = "Coding";

    /// <summary>
    /// 데스크톱 에이전트가 한 요청에서 실행할 최대 도구 단계 수
    /// </summary>
    public int DesktopMaxToolSteps { get; set; } = 0;

    /// <summary>
    /// 데스크톱 UI 표시 언어
    /// </summary>
    public string DesktopUiLanguage { get; set; } = "English";

    /// <summary>
    /// 단일 실행용 프롬프트
    /// </summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>
    /// 표준 입력에서 프롬프트를 읽을지 여부
    /// </summary>
    public bool ReadPromptFromStdin { get; set; }

    /// <summary>
    /// 프롬프트 입력 파일 경로
    /// </summary>
    public string InputFilePath { get; set; } = string.Empty;

    /// <summary>
    /// 비대화형 JSON 출력 여부
    /// </summary>
    public bool JsonOutput { get; set; }

    /// <summary>
    /// 비대화형 도구 실행 자동 허용 여부
    /// </summary>
    public bool AllowToolsWithoutPrompt { get; set; }

    /// <summary>
    /// 비대화형 실행에서 명시적으로 허용된 도구 목록
    /// </summary>
    public List<string> AllowedToolNames { get; } = [];

    /// <summary>
    /// 비대화형 실행에서 명시적으로 거부된 도구 목록
    /// </summary>
    public List<string> DeniedToolNames { get; } = [];

    public static ProviderConfiguration FromEnvironment() => new EnvironmentConfigurationLoader().Load();

    public static ProviderConfiguration FromArgs(string[] args) => new CommandLineConfigurationParser().Parse(args);
}
