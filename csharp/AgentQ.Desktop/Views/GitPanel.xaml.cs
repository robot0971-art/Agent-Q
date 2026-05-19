using System.Windows;
using System.Windows.Controls;

namespace AgentQ.Desktop.Views;

public partial class GitPanel : System.Windows.Controls.UserControl
{
    public GitPanel()
    {
        InitializeComponent();
    }

    public event EventHandler? StatusRequested;

    public event EventHandler? DiffRequested;

    public event EventHandler? ReviewRequested;

    public event EventHandler? FixReviewRequested;

    public event EventHandler? CommitSummaryRequested;

    public event EventHandler? SelectedFileChanged;

    public event EventHandler? ApproveRequested;

    public event EventHandler? RejectRequested;

    public event EventHandler? NeedsEditRequested;

    private void Status_OnClick(object sender, RoutedEventArgs e)
    {
        StatusRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Diff_OnClick(object sender, RoutedEventArgs e)
    {
        DiffRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Review_OnClick(object sender, RoutedEventArgs e)
    {
        ReviewRequested?.Invoke(this, EventArgs.Empty);
    }

    private void FixReview_OnClick(object sender, RoutedEventArgs e)
    {
        FixReviewRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CommitSummary_OnClick(object sender, RoutedEventArgs e)
    {
        CommitSummaryRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ChangedFiles_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedFileChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Approve_OnClick(object sender, RoutedEventArgs e)
    {
        ApproveRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Reject_OnClick(object sender, RoutedEventArgs e)
    {
        RejectRequested?.Invoke(this, EventArgs.Empty);
    }

    private void NeedsEdit_OnClick(object sender, RoutedEventArgs e)
    {
        NeedsEditRequested?.Invoke(this, EventArgs.Empty);
    }
}
