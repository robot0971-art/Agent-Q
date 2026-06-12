using AgentQ.Tools;

namespace AgentQ.Cli;

/// <summary>
/// Resolves tool permission decisions for non-interactive CLI runs.
/// </summary>
public sealed class NonInteractivePermissionEnforcer : IPermissionEnforcer
{
    private readonly bool _allowToolsWithoutPrompt;
    private readonly HashSet<string> _allowedToolNames;
    private readonly HashSet<string> _deniedToolNames;

    public NonInteractivePermissionEnforcer(
        bool allowToolsWithoutPrompt = false,
        IEnumerable<string>? allowedToolNames = null,
        IEnumerable<string>? deniedToolNames = null)
    {
        _allowToolsWithoutPrompt = allowToolsWithoutPrompt;
        _allowedToolNames = new HashSet<string>(
            allowedToolNames ?? [],
            StringComparer.OrdinalIgnoreCase);
        _deniedToolNames = new HashSet<string>(
            deniedToolNames ?? [],
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns whether a tool is allowed under the non-interactive permission policy.
    /// </summary>
    public Task<bool> RequestPermissionAsync(string toolName, string description, string inputJson)
    {
        if (_deniedToolNames.Contains(toolName))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(_allowToolsWithoutPrompt || _allowedToolNames.Contains(toolName));
    }
}
