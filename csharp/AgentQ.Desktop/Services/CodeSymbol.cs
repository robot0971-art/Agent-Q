namespace AgentQ.Desktop.Services;

public sealed class CodeSymbol
{
    public string Name { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public int Line { get; set; }

    public string? Container { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Container)
        ? $"{Kind} {Name} ({RelativePath}:{Line})"
        : $"{Kind} {Container}.{Name} ({RelativePath}:{Line})";
}
