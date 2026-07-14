using AgentQ.Runtime.Contracts;
using AgentQ.Runtime.Intent;

namespace AgentQ.Desktop.Services;

public interface IDesktopRuntimeTaskContractAdapter
{
    RuntimeTaskContract Create(AgentTurnState turnState, string contractId, DateTimeOffset now);
}

/// <summary>
/// Converts the safety-tested Desktop turn state into an immutable Runtime
/// contract. This adapter records existing authority; it cannot broaden it.
/// </summary>
public sealed class DesktopRuntimeTaskContractAdapter(ITaskContractFactory? factory = null) : IDesktopRuntimeTaskContractAdapter
{
    private readonly ITaskContractFactory _factory = factory ?? new TaskContractFactory();

    public RuntimeTaskContract Create(AgentTurnState turnState, string contractId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(turnState);
        return _factory.Create(new RuntimeTaskContractRequest(
            turnState.WorkspaceRoot,
            ToRuntimeIntent(turnState.EffectiveIntent.Type),
            string.IsNullOrWhiteSpace(turnState.TaskContract.Goal) ? turnState.RoutingText : turnState.TaskContract.Goal,
            [],
            GetCapabilities(turnState),
            turnState.TaskContract.RequiredActions.ToArray(),
            turnState.TaskContract.DoneWhen.ToArray(),
            turnState.TaskContract.DoneWhen.ToArray(),
            now.AddHours(1)), contractId);
    }

    private static AgentTurnIntent ToRuntimeIntent(TurnIntentType intent) => intent switch
    {
        TurnIntentType.Action => AgentTurnIntent.Action,
        TurnIntentType.Hybrid => AgentTurnIntent.Hybrid,
        TurnIntentType.Ambiguous => AgentTurnIntent.Ambiguous,
        _ => AgentTurnIntent.Conversation
    };

    private static IReadOnlyList<string> GetCapabilities(AgentTurnState state)
    {
        var capabilities = new List<string>();
        if (state.EffectiveIntent.RequiresWrite) capabilities.Add("workspace-write");
        if (state.EffectiveIntent.RequiresShell) capabilities.Add("shell");
        if (state.EffectiveIntent.RequiresNetwork) capabilities.Add("network");
        if (state.TaskContract.Intent == TaskContractIntent.CreateProject) capabilities.Add("scaffold");
        if (state.TaskContract.Intent == TaskContractIntent.RunVerification) capabilities.Add("verification");
        return capabilities;
    }
}
