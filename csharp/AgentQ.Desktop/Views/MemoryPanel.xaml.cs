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
}
