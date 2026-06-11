using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AgentQ.Desktop.Services;

public sealed class FileChangeRecord : INotifyPropertyChanged
{
    private FileChangeReviewStatus _reviewStatus = FileChangeReviewStatus.Pending;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required string Path { get; init; }

    public string RelativePath { get; init; } = string.Empty;

    public string Before { get; init; } = string.Empty;

    public string After { get; init; } = string.Empty;

    public bool ExistedBefore { get; init; } = true;

    public bool ExistsAfter { get; init; } = true;

    public string SnapshotPath { get; init; } = string.Empty;

    public DateTime ChangedAt { get; init; } = DateTime.Now;

    public IReadOnlyList<DiffLine> DiffLines { get; init; } = [];

    public string ChangedAtText => ChangedAt.ToString("HH:mm:ss");

    public int AddedLines => DiffLines.Count(line => line.Kind == DiffLineKind.Added);

    public int RemovedLines => DiffLines.Count(line => line.Kind == DiffLineKind.Removed);

    public string Summary => $"+{AddedLines} -{RemovedLines}";

    public string SnapshotLabel => string.IsNullOrWhiteSpace(SnapshotPath)
        ? "No snapshot file"
        : $"Snapshot: {SnapshotPath}";

    public string SourcePreviewText
    {
        get
        {
            if (ExistedBefore && !ExistsAfter)
            {
                return "File was removed.";
            }

            var content = string.IsNullOrWhiteSpace(After) ? Before : After;
            if (string.IsNullOrWhiteSpace(content))
            {
                return "File is empty.";
            }

            if (string.Equals(content, DesktopAgentService.DirectorySnapshotMarker, StringComparison.Ordinal))
            {
                return "Directory change.";
            }

            return content;
        }
    }

    public FileChangeReviewStatus ReviewStatus
    {
        get => _reviewStatus;
        set
        {
            if (_reviewStatus == value)
            {
                return;
            }

            _reviewStatus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ReviewStatusText));
        }
    }

    public string ReviewStatusText => ReviewStatus switch
    {
        FileChangeReviewStatus.Pending => "Pending review",
        FileChangeReviewStatus.Approved => "Approved",
        FileChangeReviewStatus.NeedsEdit => "Needs edit",
        FileChangeReviewStatus.Reverted => "Reverted",
        _ => ReviewStatus.ToString()
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class DiffLine
{
    public required DiffLineKind Kind { get; init; }

    public required string Text { get; init; }

    public string Prefix => Kind switch
    {
        DiffLineKind.Added => "+",
        DiffLineKind.Removed => "-",
        _ => " "
    };

    public string Foreground => Kind switch
    {
        DiffLineKind.Added => "#34D399",
        DiffLineKind.Removed => "#F87171",
        _ => "#CBD5E1"
    };
}

public enum DiffLineKind
{
    Unchanged,
    Added,
    Removed
}
