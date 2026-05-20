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

    public event EventHandler? PullFastForwardRequested;

    public event EventHandler? SelectedFileChanged;

    public event EventHandler? ApproveRequested;

    public event EventHandler? RejectRequested;

    public event EventHandler? NeedsEditRequested;

    public event EventHandler? StageSelectedRequested;

    public event EventHandler? StageApprovedRequested;

    public event EventHandler? UnstageSelectedRequested;

    public event EventHandler? CommitStagedRequested;

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

    private void PullFastForward_OnClick(object sender, RoutedEventArgs e)
    {
        PullFastForwardRequested?.Invoke(this, EventArgs.Empty);
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

    private void StageSelected_OnClick(object sender, RoutedEventArgs e)
    {
        StageSelectedRequested?.Invoke(this, EventArgs.Empty);
    }

    private void StageApproved_OnClick(object sender, RoutedEventArgs e)
    {
        StageApprovedRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UnstageSelected_OnClick(object sender, RoutedEventArgs e)
    {
        UnstageSelectedRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CommitStaged_OnClick(object sender, RoutedEventArgs e)
    {
        CommitStagedRequested?.Invoke(this, EventArgs.Empty);
    }
}
