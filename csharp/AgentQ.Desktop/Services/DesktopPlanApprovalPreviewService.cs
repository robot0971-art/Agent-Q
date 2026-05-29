using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

public sealed class DesktopPlanApprovalPreviewService(
    AgentPlanWorkerPlanAdapter adapter,
    WorkerExecutionPipeline pipeline)
{
    public WorkerExecutionContext BuildContext(MainViewModel viewModel)
    {
        var goal = string.IsNullOrWhiteSpace(viewModel.InputText)
            ? "Captured desktop plan"
            : viewModel.InputText.Trim();
        var plan = adapter.Convert(
            viewModel.PlanItems.ToList(),
            goal,
            viewModel.WorkspaceVerificationCommands.ToList());
        return pipeline.Begin(plan, viewModel.WorkspaceRoot, viewModel.WorkspaceVerificationCommands);
    }

    public void ApplyPreview(MainViewModel viewModel)
    {
        if (viewModel.PlanItems.Count == 0)
        {
            viewModel.ClearPlanApprovalPreview();
            return;
        }

        viewModel.SetWorkerExecutionContext(BuildContext(viewModel));
    }
}
