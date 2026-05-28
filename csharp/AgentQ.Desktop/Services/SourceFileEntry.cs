namespace AgentQ.Desktop.Services;

using System.IO;
using System.Collections.ObjectModel;

public sealed class SourceFileEntry
{
    public required string RelativePath { get; init; }

    public required string FullPath { get; init; }

    public long SizeBytes { get; init; }

    public bool IsDirectory { get; init; }

    public int Depth { get; init; }

    public ObservableCollection<SourceFileEntry> Children { get; } = [];

    public string DisplayName => Path.GetFileName(RelativePath.TrimEnd('/')) switch
    {
        "" => RelativePath,
        var name => name
    };

    public string TreeDisplayName => $"{new string(' ', Depth * 2)}{(IsDirectory ? "\u25B8" : "\u2022")} {DisplayName}";

    public string DetailText => IsDirectory
        ? RelativePath
        : $"{RelativePath}  ({SizeBytes:N0} bytes)";

    public int FileCount => IsDirectory
        ? Children.Sum(child => child.IsDirectory ? child.FileCount : 1)
        : 1;
}
