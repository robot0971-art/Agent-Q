using System.Windows;

namespace AgentQ.Desktop.Views;

public partial class PlanPanel : System.Windows.Controls.UserControl
{
    public PlanPanel()
    {
        InitializeComponent();
    }

    public event EventHandler? CreatePlanRequested;
    public event EventHandler? ContinuePlanItemRequested;
    public event EventHandler? MarkPlanItemDoneRequested;
    public event EventHandler? SaveCheckpointRequested;
    public event EventHandler? LoadCheckpointRequested;
    public event EventHandler? ResumeCheckpointRequested;
    public event EventHandler? PlanAndRunRequested;
    public event EventHandler? MarkDoneAndContinueRequested;

    private void CreatePlan_OnClick(object sender, RoutedEventArgs e)
    {
        CreatePlanRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ContinuePlanItem_OnClick(object sender, RoutedEventArgs e)
    {
        ContinuePlanItemRequested?.Invoke(this, EventArgs.Empty);
    }

    private void MarkPlanItemDone_OnClick(object sender, RoutedEventArgs e)
    {
        MarkPlanItemDoneRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SaveCheckpoint_OnClick(object sender, RoutedEventArgs e)
    {
        SaveCheckpointRequested?.Invoke(this, EventArgs.Empty);
    }

    private void LoadCheckpoint_OnClick(object sender, RoutedEventArgs e)
    {
        LoadCheckpointRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ResumeCheckpoint_OnClick(object sender, RoutedEventArgs e)
    {
        ResumeCheckpointRequested?.Invoke(this, EventArgs.Empty);
    }

    private void PlanAndRun_OnClick(object sender, RoutedEventArgs e)
    {
        PlanAndRunRequested?.Invoke(this, EventArgs.Empty);
    }

    private void MarkDoneAndContinue_OnClick(object sender, RoutedEventArgs e)
    {
        MarkDoneAndContinueRequested?.Invoke(this, EventArgs.Empty);
    }
}
