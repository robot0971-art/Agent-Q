namespace AgentQ.Tools;

/// <summary>
/// Permission prompt abstraction used before risky tool execution.
/// </summary>
public interface IPermissionEnforcer
{
    /// <summary>
    /// Requests approval to execute a tool with the supplied input.
    /// </summary>
    Task<bool> RequestPermissionAsync(string toolName, string description, string inputJson);
}

/// <summary>
/// Permission enforcer that approves every request.
/// </summary>
public class AlwaysAllowPermissionEnforcer : IPermissionEnforcer
{
    /// <summary>
    /// Always approves the request.
    /// </summary>
    public Task<bool> RequestPermissionAsync(string toolName, string description, string inputJson) => Task.FromResult(true);
}
