namespace AgentQ.Runtime.Model;

public sealed record ModelToolCall(string ToolName, string Input);

public sealed record ModelToolLoopTurn(string? FinalText, IReadOnlyList<ModelToolCall> ToolCalls);

public sealed record ModelToolLoopRequest(string RunId, int MaximumSteps, string Context);

public sealed record ModelToolLoopResult(string? FinalText, int StepsExecuted, bool HitStepLimit, IReadOnlyList<string> ToolResults);

public interface IModelToolLoopPort
{
    Task<ModelToolLoopTurn> GenerateAsync(ModelToolLoopRequest request, int step, IReadOnlyList<string> priorToolResults, CancellationToken cancellationToken);

    Task<string> ExecuteToolAsync(ModelToolCall toolCall, CancellationToken cancellationToken);
}

public interface IModelToolLoop
{
    Task<ModelToolLoopResult> RunAsync(ModelToolLoopRequest request, IModelToolLoopPort port, CancellationToken cancellationToken = default);
}

/// <summary>Bounded orchestration only. Tool policy, approval, evidence, and completion remain separate ports.</summary>
public sealed class ModelToolLoop : IModelToolLoop
{
    public async Task<ModelToolLoopResult> RunAsync(ModelToolLoopRequest request, IModelToolLoopPort port, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(port);
        if (request.MaximumSteps < 1) throw new ArgumentOutOfRangeException(nameof(request.MaximumSteps));

        var results = new List<string>();
        for (var step = 1; step <= request.MaximumSteps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var turn = await port.GenerateAsync(request, step, results, cancellationToken);
            if (turn.ToolCalls.Count == 0)
            {
                return new ModelToolLoopResult(turn.FinalText, step, false, results);
            }

            foreach (var toolCall in turn.ToolCalls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(await port.ExecuteToolAsync(toolCall, cancellationToken));
            }
        }

        return new ModelToolLoopResult(null, request.MaximumSteps, true, results);
    }
}
