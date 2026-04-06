namespace AgentQ.Tools;

/// <summary>
/// 도구 레지스트리
/// </summary>
public class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new();

    /// <summary>
    /// 도구 등록
    /// </summary>
    /// <param name="tool">등록할 도구</param>
    public void Register(ITool tool)
    {
        _tools[tool.Name] = tool;
    }

    /// <summary>
    /// 도구 조회
    /// </summary>
    /// <param name="name">도구 이름</param>
    /// <returns>도구 인터페이스 (없으면 null)</returns>
    public ITool? Get(string name)
    {
        _tools.TryGetValue(name, out var tool);
        return tool;
    }

    /// <summary>
    /// 모든 등록된 도구 목록
    /// </summary>
    public IReadOnlyCollection<ITool> All => _tools.Values;

    /// <summary>
    /// 도구 정의 목록 생성
    /// </summary>
    /// <returns>도구 정의 항목 목록</returns>
    public List<ToolDefinitionEntry> GetToolDefinitions()
    {
        return _tools.Values.Select(t => new ToolDefinitionEntry
        {
            Name = t.Name,
            Description = t.Description,
            InputSchema = t.InputSchema
        }).ToList();
    }
}

/// <summary>
/// 도구 정의 항목
/// </summary>
public class ToolDefinitionEntry
{
    /// <summary>
    /// 도구 이름
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 도구 설명
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// 입력 스키마
    /// </summary>
    public object InputSchema { get; init; } = new();
}

