using AgentQ.Core.Models;

namespace AgentQ.Core.Providers;

public interface ILlmProvider
{
    string Name { get; }
    string DefaultModel { get; }

    Task<ChatResponse> GenerateResponseAsync(
        ChatContext context,
        IEnumerable<ToolDefinition> tools,
        CancellationToken ct = default);

    IAsyncEnumerable<StreamChunk> GenerateStreamAsync(
        ChatContext context,
        IEnumerable<ToolDefinition> tools,
        CancellationToken ct = default);
}

public interface IStreamingLlmProvider : ILlmProvider
{
    new IAsyncEnumerable<StreamChunk> GenerateStreamAsync(
        ChatContext context,
        IEnumerable<ToolDefinition> tools,
        CancellationToken ct = default);
}

