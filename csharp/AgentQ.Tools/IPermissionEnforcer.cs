namespace AgentQ.Tools;

public interface IPermissionEnforcer
{
    Task<bool> RequestPermissionAsync(string toolName, string description, string inputJson);
}

public class AlwaysAllowPermissionEnforcer : IPermissionEnforcer
{
    public Task<bool> RequestPermissionAsync(string toolName, string description, string inputJson) => Task.FromResult(true);
}

