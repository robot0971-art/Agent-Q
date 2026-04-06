using System.Runtime.CompilerServices;
using AgentQ.Core.Models;

namespace AgentQ.Core.Providers;

/// <summary>
/// 재시도 로직이 포함된 LLM 제공자 래퍼입니다.
/// 지수 백오프(Exponential Backoff) 전략을 사용하여 일시적인 네트워크 오류나 API 속도 제한에 대응합니다.
/// </summary>
public class ResilientLlmProvider : ILlmProvider
{
    private readonly ILlmProvider _inner;
    private readonly int _maxRetries;
    private readonly TimeSpan _initialDelay;

    /// <summary>
    /// 지정된 내부 제공자를 감싸는 ResilientLlmProvider의 새 인스턴스를 초기화합니다.
    /// </summary>
    /// <param name="inner">실제 요청을 수행할 내부 LLM 제공자</param>
    /// <param name="maxRetries">최대 재시도 횟수 (기본값: 3)</param>
    /// <param name="initialDelay">첫 번째 재시도 전 대기 시간 (기본값: 1초)</param>
    public ResilientLlmProvider(ILlmProvider inner, int maxRetries = 3, TimeSpan? initialDelay = null)
    {
        _inner = inner;
        _maxRetries = maxRetries;
        _initialDelay = initialDelay ?? TimeSpan.FromSeconds(1);
    }

    /// <summary>
    /// 제공자 이름
    /// </summary>
    public string Name => _inner.Name;

    /// <summary>
    /// 기본 모델 이름
    /// </summary>
    public string DefaultModel => _inner.DefaultModel;

    /// <summary>
    /// 재시도 로직을 포함하여 응답을 생성합니다.
    /// </summary>
    public async Task<ChatResponse> GenerateResponseAsync(
        ChatContext context,
        IEnumerable<ToolDefinition> tools,
        CancellationToken ct = default)
    {
        int attempt = 0;
        while (true)
        {
            try
            {
                attempt++;
                return await _inner.GenerateResponseAsync(context, tools, ct);
            }
            catch (Exception ex) when (IsRetryable(ex) && attempt <= _maxRetries)
            {
                var delay = GetDelay(attempt);
                await Task.Delay(delay, ct);
            }
        }
    }

    /// <summary>
    /// 재시도 로직을 포함하여 스트리밍 응답을 생성합니다.
    /// </summary>
    public async IAsyncEnumerable<StreamChunk> GenerateStreamAsync(
        ChatContext context,
        IEnumerable<ToolDefinition> tools,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        int attempt = 0;
        while (true)
        {
            attempt++;
            Exception? error = null;
            bool producedAny = false;

            var stream = _inner.GenerateStreamAsync(context, tools, ct);
            var enumerator = stream.GetAsyncEnumerator(ct);
            
            try
            {
                while (true)
                {
                    StreamChunk current;
                    try
                    {
                        if (!await enumerator.MoveNextAsync()) break;
                        current = enumerator.Current;
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                        break;
                    }

                    producedAny = true;
                    yield return current;
                }
            }
            finally
            {
                await enumerator.DisposeAsync();
            }

            if (error != null)
            {
                if (IsRetryable(error) && attempt <= _maxRetries && !producedAny)
                {
                    var delay = GetDelay(attempt);
                    await Task.Delay(delay, ct);
                    continue; // Retry from the beginning
                }
                throw error;
            }

            yield break; // Success
        }
    }

    /// <summary>
    /// 예외가 재시도 가능한지 여부를 확인합니다.
    /// </summary>
    private bool IsRetryable(Exception ex)
    {
        return ex is HttpRequestException || ex is TaskCanceledException || ex is TimeoutException;
    }

    /// <summary>
    /// 시도 횟수에 따른 대기 시간을 계산합니다 (지수 백오프).
    /// </summary>
    private TimeSpan GetDelay(int attempt)
    {
        return TimeSpan.FromTicks(_initialDelay.Ticks * (long)Math.Pow(2, attempt - 1));
    }
}
