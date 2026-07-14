using AgentQ.Runtime.Contracts;

namespace AgentQ.Runtime.Dispatch;

public sealed record DeterministicActionRequest(
    RuntimeTaskContract Contract,
    string ActionName,
    string RequiredCapability,
    string Target,
    DateTimeOffset RequestedAt);

public sealed record DeterministicActionResult(
    bool Succeeded,
    bool Dispatched,
    string ReasonCode,
    string Summary);

public interface IDeterministicActionHandler
{
    string ActionName { get; }

    Task<DeterministicActionResult> ExecuteAsync(DeterministicActionRequest request, CancellationToken cancellationToken);
}

public interface IDeterministicActionDispatcher
{
    Task<DeterministicActionResult> DispatchAsync(DeterministicActionRequest request, CancellationToken cancellationToken = default);
}

public sealed class DeterministicActionDispatcher(IEnumerable<IDeterministicActionHandler> handlers) : IDeterministicActionDispatcher
{
    private readonly IReadOnlyDictionary<string, IDeterministicActionHandler> _handlers = handlers
        .GroupBy(handler => handler.ActionName, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);

    public Task<DeterministicActionResult> DispatchAsync(DeterministicActionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Contract.ExpiresAt <= request.RequestedAt)
        {
            return Blocked("contract-expired", "The approved task contract has expired.");
        }

        if (!request.Contract.Capabilities.Contains(request.RequiredCapability, StringComparer.OrdinalIgnoreCase))
        {
            return Blocked("capability-not-approved", $"Capability '{request.RequiredCapability}' is outside the approved contract.");
        }

        if (!request.Contract.Targets.Contains(request.Target, StringComparer.OrdinalIgnoreCase))
        {
            return Blocked("target-not-approved", $"Target '{request.Target}' is outside the approved contract.");
        }

        if (!_handlers.TryGetValue(request.ActionName, out var handler))
        {
            return Blocked("handler-not-registered", $"No deterministic handler is registered for '{request.ActionName}'.");
        }

        return handler.ExecuteAsync(request, cancellationToken);
    }

    private static Task<DeterministicActionResult> Blocked(string reasonCode, string summary) =>
        Task.FromResult(new DeterministicActionResult(false, false, reasonCode, summary));
}
