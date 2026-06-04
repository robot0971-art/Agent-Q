namespace AgentQ.Desktop.Services;

public sealed class AgentQSystemSkill
{
    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public int Priority { get; init; }

    public IReadOnlyList<string> TaskKinds { get; init; } = [];

    public IReadOnlyList<string> Triggers { get; init; } = [];

    public IReadOnlyList<string> Excludes { get; init; } = [];

    public string Content { get; init; } = string.Empty;

    public string Source { get; init; } = AgentQSystemSkillSource.Builtin;
}

public static class AgentQSystemSkillSource
{
    public const string Builtin = "builtin";
    public const string Project = "project";
}
