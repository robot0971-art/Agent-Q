using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AgentQ.Desktop.Services;

public sealed class GitChangedFile : INotifyPropertyChanged
{
    private GitChangeReviewStatus _reviewStatus = GitChangeReviewStatus.Pending;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Status { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public string? OriginalPath { get; init; }

    public GitChangeReviewStatus ReviewStatus
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

    public string ReviewStatusText => ReviewStatus.ToString();

    public bool IsStaged => !string.IsNullOrWhiteSpace(Status) &&
                            Status[0] != ' ' &&
                            Status[0] != '?';

    public bool IsUnstaged => !string.IsNullOrWhiteSpace(Status) &&
                              Status.Length > 1 &&
                              Status[1] != ' ';

    public string DisplayName => string.IsNullOrWhiteSpace(Status)
        ? Path
        : $"{Status}  {Path}";

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
