using System.Windows;
using AgentQ.Desktop.Services;

namespace AgentQ.Desktop.Views;

public partial class MemoryPanel : System.Windows.Controls.UserControl
{
    public MemoryPanel()
    {
        InitializeComponent();
    }

    public event EventHandler? SaveSessionSummaryRequested;
    public event EventHandler? LoadSessionSummaryRequested;
    public event EventHandler? ResumeSessionSummaryRequested;
    public event EventHandler<ProjectMemoryLesson?>? SaveSelectedLessonRequested;
    public event EventHandler<ProjectMemoryLesson?>? DismissSelectedLessonRequested;
    public event EventHandler? RefreshSavedMemoryRequested;
    public event EventHandler? PreviewMemoryCleanupRequested;
    public event EventHandler? CompactMemoryRequested;
    public event EventHandler<ProjectMemoryLesson?>? DisableSavedMemoryRequested;
    public event EventHandler<ProjectMemoryLesson?>? DeleteSavedMemoryRequested;

    private void SaveSessionSummary_OnClick(object sender, RoutedEventArgs e)
    {
        SaveSessionSummaryRequested?.Invoke(this, EventArgs.Empty);
    }

    private void LoadSessionSummary_OnClick(object sender, RoutedEventArgs e)
    {
        LoadSessionSummaryRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ResumeSessionSummary_OnClick(object sender, RoutedEventArgs e)
    {
        ResumeSessionSummaryRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SaveSelectedLesson_OnClick(object sender, RoutedEventArgs e)
    {
        SaveSelectedLessonRequested?.Invoke(this, DataContext is ViewModels.MainViewModel viewModel
            ? viewModel.SelectedPendingMemoryLesson
            : null);
    }

    private void DismissSelectedLesson_OnClick(object sender, RoutedEventArgs e)
    {
        DismissSelectedLessonRequested?.Invoke(this, DataContext is ViewModels.MainViewModel viewModel
            ? viewModel.SelectedPendingMemoryLesson
            : null);
    }

    private void RefreshSavedMemory_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshSavedMemoryRequested?.Invoke(this, EventArgs.Empty);
    }

    private void PreviewMemoryCleanup_OnClick(object sender, RoutedEventArgs e)
    {
        PreviewMemoryCleanupRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CompactMemory_OnClick(object sender, RoutedEventArgs e)
    {
        CompactMemoryRequested?.Invoke(this, EventArgs.Empty);
    }

    private void DisableSavedMemory_OnClick(object sender, RoutedEventArgs e)
    {
        DisableSavedMemoryRequested?.Invoke(this, DataContext is ViewModels.MainViewModel viewModel
            ? viewModel.SelectedSavedMemoryLesson
            : null);
    }

    private void DeleteSavedMemory_OnClick(object sender, RoutedEventArgs e)
    {
        DeleteSavedMemoryRequested?.Invoke(this, DataContext is ViewModels.MainViewModel viewModel
            ? viewModel.SelectedSavedMemoryLesson
            : null);
    }
}
