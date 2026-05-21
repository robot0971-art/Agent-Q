namespace AgentQ.Desktop.Services;

public interface IEmbeddingClient
{
    Task<IReadOnlyList<float[]>> CreateEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        string model,
        CancellationToken ct = default);
}
