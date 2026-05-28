using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgentQ.Desktop.Services;

namespace AgentQ.Desktop.ViewModels;

public sealed class GitPanelViewModel : INotifyPropertyChanged
{
    private bool _canFixLastCodeReviewFindings;
    private bool _useKoreanUi;
    private string _statusText = DesktopLocalizer.UiText(DesktopText.GitStatusEmpty, useKoreanUi: false);
    private string _diffText = DesktopLocalizer.UiText(DesktopText.GitDiffEmpty, useKoreanUi: false);
    private string _selectedFileDiffText = DesktopLocalizer.UiText(DesktopText.GitSelectedFileEmpty, useKoreanUi: false);
    private string _lastUpdatedText = DesktopLocalizer.UiText(DesktopText.GitWaitingForRefresh, useKoreanUi: false);
    private string _commitMessage = string.Empty;
    private GitChangedFile? _selectedChangedFile;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<GitChangedFile> ChangedFiles { get; } = [];

    public bool UseKoreanUi
    {
        get => _useKoreanUi;
        set
        {
            var previous = _useKoreanUi;
            if (!SetField(ref _useKoreanUi, value))
            {
                return;
            }

            RefreshDefaultText(previous);
        }
    }

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
        StatusText = DesktopLocalizer.UiText(DesktopText.GitStatusEmpty, UseKoreanUi);
        DiffText = DesktopLocalizer.UiText(DesktopText.GitDiffEmpty, UseKoreanUi);
        SelectedFileDiffText = DesktopLocalizer.UiText(DesktopText.GitSelectedFileEmpty, UseKoreanUi);
        LastUpdatedText = DesktopLocalizer.UiText(DesktopText.GitWaitingForRefresh, UseKoreanUi);
        CommitMessage = string.Empty;
        CanFixLastCodeReviewFindings = false;
    }

    private void RefreshDefaultText(bool previousUseKoreanUi)
    {
        ReplaceDefaultText(ref _statusText, DesktopText.GitStatusEmpty, previousUseKoreanUi, nameof(StatusText));
        ReplaceDefaultText(ref _diffText, DesktopText.GitDiffEmpty, previousUseKoreanUi, nameof(DiffText));
        ReplaceDefaultText(ref _selectedFileDiffText, DesktopText.GitSelectedFileEmpty, previousUseKoreanUi, nameof(SelectedFileDiffText));
        ReplaceDefaultText(ref _lastUpdatedText, DesktopText.GitWaitingForRefresh, previousUseKoreanUi, nameof(LastUpdatedText));
    }

    private void ReplaceDefaultText(ref string field, string key, bool previousUseKoreanUi, string propertyName)
    {
        if (!string.Equals(field, DesktopLocalizer.UiText(key, previousUseKoreanUi), StringComparison.Ordinal))
        {
            return;
        }

        field = DesktopLocalizer.UiText(key, UseKoreanUi);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
