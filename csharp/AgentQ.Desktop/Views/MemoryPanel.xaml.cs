using System.Windows;

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
}
