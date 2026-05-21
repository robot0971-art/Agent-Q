namespace AgentQ.Desktop.Services;

public sealed class WorkspaceSymbolIndex
{
    public List<CodeSymbol> Symbols { get; set; } = [];

    public int FilesIndexed { get; set; }

    public int SymbolCount => Symbols.Count;
}
