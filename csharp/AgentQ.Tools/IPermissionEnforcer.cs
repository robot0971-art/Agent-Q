namespace AgentQ.Tools;

/// <summary>
/// 권한 인포서 인터페이스
/// </summary>
public interface IPermissionEnforcer
{
    /// <summary>
    /// 권한 요청
    /// </summary>
    /// <param name="toolName">도구 이름</param>
    /// <param name="description">설명</param>
    /// <param name="inputJson">입력 JSON</param>
    /// <returns>권한 승인 여부</returns>
    Task<bool> RequestPermissionAsync(string toolName, string description, string inputJson);
}

/// <summary>
/// 항상 허용하는 권한 인포서
/// </summary>
public class AlwaysAllowPermissionEnforcer : IPermissionEnforcer
{
    /// <summary>
    /// 권한 요청 (항상 true 반환)
    /// </summary>
    public Task<bool> RequestPermissionAsync(string toolName, string description, string inputJson) => Task.FromResult(true);
}

