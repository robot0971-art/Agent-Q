using AgentQ.Tools;

namespace AgentQ.Cli;

/// <summary>
/// 비대화형 실행에서 도구 권한을 자동으로 판정합니다.
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
    /// 비대화형 정책에 따라 도구 실행 허용 여부를 반환합니다.
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
