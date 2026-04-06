using AgentQ.Core.Models;

namespace AgentQ.Core.Providers;

/// <summary>
/// LLM 제공자 인터페이스
/// </summary>
public interface ILlmProvider
{
    /// <summary>
    /// 제공자 이름
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 기본 모델 이름
    /// </summary>
    string DefaultModel { get; }

    /// <summary>
    /// 응답 생성
    /// </summary>
    /// <param name="context">채팅 컨텍스트</param>
    /// <param name="tools">사용 가능한 도구 목록</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>채팅 응답</returns>
    Task<ChatResponse> GenerateResponseAsync(
        ChatContext context,
        IEnumerable<ToolDefinition> tools,
        CancellationToken ct = default);

    /// <summary>
    /// 스트리밍 응답 생성
    /// </summary>
    /// <param name="context">채팅 컨텍스트</param>
    /// <param name="tools">사용 가능한 도구 목록</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>스트리밍 청크 시퀀스</returns>
    IAsyncEnumerable<StreamChunk> GenerateStreamAsync(
        ChatContext context,
        IEnumerable<ToolDefinition> tools,
        CancellationToken ct = default);
}

/// <summary>
/// 스트리밍 LLM 제공자 인터페이스
/// </summary>
public interface IStreamingLlmProvider : ILlmProvider
{
    /// <summary>
    /// 스트리밍 응답 생성
    /// </summary>
    /// <param name="context">채팅 컨텍스트</param>
    /// <param name="tools">사용 가능한 도구 목록</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>스트리밍 청크 시퀀스</returns>
    new IAsyncEnumerable<StreamChunk> GenerateStreamAsync(
        ChatContext context,
        IEnumerable<ToolDefinition> tools,
        CancellationToken ct = default);
}

