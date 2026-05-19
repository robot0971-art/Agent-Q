using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

public sealed class DesktopWorkspaceContextWorkflowService(
    WorkspaceAnalysisService workspaceAnalysisService,
    ProjectAgentConfigService projectConfigService,
    AgentSessionSummaryService sessionSummaryService,
    DesktopPlanCheckpointWorkflowService planCheckpointWorkflowService)
{
    private AgentSessionSummary? _lastSessionSummary;

    public ProjectAgentConfig? ProjectConfig { get; private set; }

    public bool HasSessionSummary => _lastSessionSummary != null;

    public async Task LoadWorkspaceContextAsync(MainViewModel viewModel, Func<string, string> trimForLog, CancellationToken ct = default)
    {
        await LoadProjectConfigAsync(viewModel, ct);
        await RefreshWorkspaceAnalysisAsync(viewModel, trimForLog, ct);
        await planCheckpointWorkflowService.LoadLatestCheckpointAsync(viewModel, ct);
        await LoadLatestSessionSummaryAsync(viewModel, ct);
    }

    public async Task LoadProjectConfigAsync(MainViewModel viewModel, CancellationToken ct = default)
    {
        ProjectConfig = await projectConfigService.LoadAsync(viewModel.WorkspaceRoot, ct);
        if (ProjectConfig == null)
        {
            viewModel.ProjectConfigText = $"No project config loaded.{Environment.NewLine}{ProjectAgentConfigService.GetConfigPath(viewModel.WorkspaceRoot)}";
            viewModel.HasProjectConfig = false;
            return;
        }

        ApplyProjectConfig(viewModel, ProjectConfig);
        viewModel.ProjectConfigText = DesktopProjectConfigBuilder.BuildDisplay(ProjectConfig);
        viewModel.HasProjectConfig = true;
        viewModel.AddLog("Project config loaded");
    }

    public async Task SaveProjectConfigAsync(MainViewModel viewModel, CancellationToken ct = default)
    {
        var config = DesktopProjectConfigBuilder.Build(
            viewModel.WorkMode,
            viewModel.WorkspaceVerificationCommands,
            viewModel.WorkspaceHints);
        await projectConfigService.SaveAsync(viewModel.WorkspaceRoot, config, ct);
        ProjectConfig = config;
        viewModel.ProjectConfigText = DesktopProjectConfigBuilder.BuildDisplay(config);
        viewModel.HasProjectConfig = true;
        viewModel.StatusText = "Project config saved";
        viewModel.AddLog("Project config saved");
    }

    public async Task RefreshWorkspaceAnalysisAsync(MainViewModel viewModel, Func<string, string> trimForLog, CancellationToken ct = default)
    {
        try
        {
            viewModel.StatusText = "Analyzing workspace";
            var workspaceRoot = viewModel.WorkspaceRoot;
            var analysis = await Task.Run(
                () => workspaceAnalysisService.AnalyzeAsync(workspaceRoot, ct),
                ct);
            viewModel.ApplyWorkspaceAnalysis(analysis);
            viewModel.StatusText = "Workspace analysis refreshed";
            viewModel.AddLog($"Workspace analyzed: {analysis.Summary}");
        }
        catch (Exception ex)
        {
            viewModel.StatusText = $"Workspace analysis failed: {ex.Message}";
            viewModel.AddLog($"Workspace analysis failed: {trimForLog(ex.Message)}");
        }
    }

    public async Task LoadLatestSessionSummaryAsync(MainViewModel viewModel, CancellationToken ct = default)
    {
        _lastSessionSummary = await sessionSummaryService.LoadLatestAsync(viewModel.WorkspaceRoot, ct);
        if (_lastSessionSummary == null)
        {
            viewModel.LatestSessionSummaryText = "No session summary saved.";
            viewModel.CanResumeSessionSummary = false;
            return;
        }

        viewModel.LatestSessionSummaryText = _lastSessionSummary.DisplayText;
        viewModel.CanResumeSessionSummary = true;
        viewModel.AddLog($"Session summary loaded: {_lastSessionSummary.CreatedAt:yyyy-MM-dd HH:mm:ss}");
    }

    public async Task SaveSessionSummaryAsync(MainViewModel viewModel, string successStatus, Func<string, string> trimForLog, CancellationToken ct = default)
    {
        try
        {
            var summary = DesktopSessionSummaryBuilder.Build(
                viewModel.WorkspaceRoot,
                viewModel.StatusText,
                viewModel.RunSteps,
                viewModel.FileChanges,
                viewModel.VerificationResults,
                viewModel.PlanItems,
                viewModel.Messages);
            await sessionSummaryService.SaveAsync(summary, ct);
            _lastSessionSummary = summary;
            viewModel.LatestSessionSummaryText = summary.DisplayText;
            viewModel.CanResumeSessionSummary = true;
            viewModel.StatusText = successStatus;
            viewModel.AddLog(successStatus);
        }
        catch (Exception ex)
        {
            viewModel.StatusText = $"Session summary save failed: {ex.Message}";
            viewModel.AddLog($"Session summary save failed: {trimForLog(ex.Message)}");
        }
    }

    public async Task<string?> BuildResumeSessionSummaryPromptAsync(MainViewModel viewModel, CancellationToken ct = default)
    {
        _lastSessionSummary ??= await sessionSummaryService.LoadLatestAsync(viewModel.WorkspaceRoot, ct);
        if (_lastSessionSummary == null)
        {
            viewModel.StatusText = "No session summary to resume";
            viewModel.CanResumeSessionSummary = false;
            return null;
        }

        viewModel.AddLog("Session resume prompt prepared");
        return DesktopPromptBuilder.BuildResumeFromSessionSummaryPrompt(_lastSessionSummary);
    }

    private static void ApplyProjectConfig(MainViewModel viewModel, ProjectAgentConfig config)
    {
        if (Enum.TryParse<AgentWorkMode>(config.WorkMode, ignoreCase: true, out var mode))
        {
            viewModel.WorkMode = mode;
        }
    }
}
