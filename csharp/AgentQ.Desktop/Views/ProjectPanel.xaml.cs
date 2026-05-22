using System.Windows;

namespace AgentQ.Desktop.Views;

public partial class ProjectPanel : System.Windows.Controls.UserControl
{
    public ProjectPanel()
    {
        InitializeComponent();
    }

    public event EventHandler? BrowseWorkspaceRequested;

    public event EventHandler? OpenWorkspaceRequested;

    public event EventHandler? RefreshWorkspaceAnalysisRequested;

    public event EventHandler? BuildEmbeddingIndexRequested;

    public event EventHandler? CopyAnalysisReportRequested;

    public event EventHandler? SaveAnalysisReportRequested;

    public event EventHandler? SaveProjectConfigRequested;

    public event EventHandler? LoadProjectConfigRequested;

    private void BrowseWorkspace_OnClick(object sender, RoutedEventArgs e)
    {
        BrowseWorkspaceRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OpenWorkspace_OnClick(object sender, RoutedEventArgs e)
    {
        OpenWorkspaceRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshWorkspaceAnalysis_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshWorkspaceAnalysisRequested?.Invoke(this, EventArgs.Empty);
    }

    private void BuildEmbeddingIndex_OnClick(object sender, RoutedEventArgs e)
    {
        BuildEmbeddingIndexRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CopyAnalysisReport_OnClick(object sender, RoutedEventArgs e)
    {
        CopyAnalysisReportRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SaveAnalysisReport_OnClick(object sender, RoutedEventArgs e)
    {
        SaveAnalysisReportRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SaveProjectConfig_OnClick(object sender, RoutedEventArgs e)
    {
        SaveProjectConfigRequested?.Invoke(this, EventArgs.Empty);
    }

    private void LoadProjectConfig_OnClick(object sender, RoutedEventArgs e)
    {
        LoadProjectConfigRequested?.Invoke(this, EventArgs.Empty);
    }
}
