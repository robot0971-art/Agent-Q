using AgentQ.Tools;

namespace AgentQ.Cli;

/// <summary>
/// 비대화형 실행에서는 권한 요청을 자동으로 거부합니다.
/// </summary>
public sealed class NonInteractivePermissionEnforcer : IPermissionEnforcer
{
    private readonly bool _allowToolsWithoutPrompt;
    private readonly HashSet<string> _allowedToolNames;

    public NonInteractivePermissionEnforcer(bool allowToolsWithoutPrompt = false, IEnumerable<string>? allowedToolNames = null)
    {
        _allowToolsWithoutPrompt = allowToolsWithoutPrompt;
        _allowedToolNames = new HashSet<string>(
            allowedToolNames ?? [],
            StringComparer.OrdinalIgnoreCase);
    }

    public Task<bool> RequestPermissionAsync(string toolName, string description, string inputJson) =>
        Task.FromResult(_allowToolsWithoutPrompt || _allowedToolNames.Contains(toolName));
}
