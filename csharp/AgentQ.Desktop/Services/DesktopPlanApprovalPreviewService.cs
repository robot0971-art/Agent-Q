using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

public sealed class DesktopPlanApprovalPreviewService(
    AgentPlanWorkerPlanAdapter adapter,
    WorkerExecutionPipeline pipeline,
    WorkerPlanCandidateBuilder? candidateBuilder = null,
    DesktopScaffoldIntentRouter? scaffoldIntentRouter = null)
{
    private readonly WorkerPlanCandidateBuilder _candidateBuilder = candidateBuilder ?? new WorkerPlanCandidateBuilder();
    private readonly DesktopScaffoldIntentRouter _scaffoldIntentRouter = scaffoldIntentRouter ?? new DesktopScaffoldIntentRouter();

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

    public WorkerExecutionContext BuildScaffoldRecommendationContext(
        MainViewModel viewModel,
        WorkerScaffoldRecommendation recommendation)
    {
        var goal = string.IsNullOrWhiteSpace(viewModel.InputText)
            ? recommendation.Name
            : viewModel.InputText.Trim();
        var plan = _candidateBuilder.BuildCandidate(
            goal,
            InferLanguage(recommendation),
            InferFramework(recommendation),
            recommendation);
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

    public bool ApplyScaffoldRecommendationPreview(MainViewModel viewModel, string? userRequest = null)
    {
        if (viewModel.PlanItems.Count > 0 || viewModel.WorkspaceScaffoldRecommendations.Count == 0)
        {
            return false;
        }

        var intent = _scaffoldIntentRouter.Analyze(userRequest ?? string.Empty, viewModel.WorkspaceRoot);
        if (intent.Kind == DesktopScaffoldIntentKind.None)
        {
            return false;
        }

        viewModel.SetWorkerExecutionContext(BuildScaffoldRecommendationContext(
            viewModel,
            _scaffoldIntentRouter.SelectRecommendation(
                viewModel.WorkspaceScaffoldRecommendations,
                userRequest,
                viewModel.WorkspaceRoot)));
        return true;
    }

    private static string InferLanguage(WorkerScaffoldRecommendation recommendation)
    {
        var files = recommendation.Files;
        if (files.Any(file => file.EndsWith(".py", StringComparison.OrdinalIgnoreCase)))
        {
            return "python";
        }

        if (files.Any(file => file.EndsWith(".rs", StringComparison.OrdinalIgnoreCase)))
        {
            return "rust";
        }

        if (files.Any(file => file.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                              file.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase)))
        {
            return "javascript";
        }

        return "typescript";
    }

    private static string InferFramework(WorkerScaffoldRecommendation recommendation)
    {
        var text = $"{recommendation.Name} {recommendation.Description}";
        if (text.Contains("Vite", StringComparison.OrdinalIgnoreCase))
        {
            return "Vite React";
        }

        if (text.Contains("React", StringComparison.OrdinalIgnoreCase))
        {
            return "React";
        }

        if (text.Contains("FastAPI", StringComparison.OrdinalIgnoreCase))
        {
            return "FastAPI";
        }

        if (text.Contains("Rust", StringComparison.OrdinalIgnoreCase))
        {
            return "Cargo";
        }

        return string.Empty;
    }
}
