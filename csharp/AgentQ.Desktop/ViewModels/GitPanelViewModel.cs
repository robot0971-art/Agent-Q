using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgentQ.Desktop.Services;

namespace AgentQ.Desktop.ViewModels;

public sealed class GitPanelViewModel : INotifyPropertyChanged
{
    private bool _canFixLastCodeReviewFindings;
    private string _statusText = "Click Status to inspect the current branch and changed files.";
    private string _diffText = "Click Diff to load the current workspace diff.";
    private string _selectedFileDiffText = "Select a changed file to view its diff.";
    private string _lastUpdatedText = "Git panel is waiting for refresh.";
    private string _commitMessage = string.Empty;
    private GitChangedFile? _selectedChangedFile;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<GitChangedFile> ChangedFiles { get; } = [];

    public bool CanFixLastCodeReviewFindings
    {
        get => _canFixLastCodeReviewFindings;
        set => SetField(ref _canFixLastCodeReviewFindings, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public string DiffText
    {
        get => _diffText;
        set => SetField(ref _diffText, value);
    }

    public string SelectedFileDiffText
    {
        get => _selectedFileDiffText;
        set => SetField(ref _selectedFileDiffText, value);
    }

    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        set => SetField(ref _lastUpdatedText, value);
    }

    public string CommitMessage
    {
        get => _commitMessage;
        set => SetField(ref _commitMessage, value);
    }

    public GitChangedFile? SelectedChangedFile
    {
        get => _selectedChangedFile;
        set => SetField(ref _selectedChangedFile, value);
    }

    public void Reset()
    {
        ChangedFiles.Clear();
        SelectedChangedFile = null;
        StatusText = "Click Status to inspect the current branch and changed files.";
        DiffText = "Click Diff to load the current workspace diff.";
        SelectedFileDiffText = "Select a changed file to view its diff.";
        LastUpdatedText = "Git panel is waiting for refresh.";
        CommitMessage = string.Empty;
        CanFixLastCodeReviewFindings = false;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
