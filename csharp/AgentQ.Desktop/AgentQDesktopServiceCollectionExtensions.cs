using AgentQ.Desktop.Services;
using AgentQ.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AgentQ.Desktop;

public static class AgentQDesktopServiceCollectionExtensions
{
    public static IServiceCollection AddAgentQDesktop(this IServiceCollection services)
    {
        services.AddHttpClient("anthropic");
        services.AddHttpClient("openai");
        services.AddHttpClient("desktop-links", client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddHttpClient("model-discovery", client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<LinkContentFetcher>();
        services.AddSingleton<DesktopProviderModelDiscoveryService>();
        services.AddSingleton<DesktopTelemetryService>();
        services.AddSingleton<ToolReplayService>();
        services.AddSingleton<EvalReplayDashboardService>();
        services.AddSingleton<ProjectMemoryService>();
        services.AddSingleton<DesktopLearningSuggestionService>();
        services.AddSingleton<WorkspaceIndexer>();
        services.AddSingleton<WorkspaceSymbolIndexService>();
        services.AddSingleton<WorkspaceAnalysisService>();
        services.AddSingleton<SystemSkillService>();
        services.AddSingleton<EmbeddingIndexStore>();
        services.AddSingleton<EmbeddingIndexBuilder>();
        services.AddSingleton<DesktopEmbeddingClientFactory>();
        services.AddSingleton<DesktopConfigService>();
        services.AddSingleton<DesktopStartupCommandService>();
        services.AddSingleton<DesktopLocalServerService>();
        services.AddSingleton<DesktopAgentService>();
        services.AddSingleton<IDesktopLlmProviderFactory>(provider => provider.GetRequiredService<DesktopAgentService>());
        services.AddSingleton<IVerificationArtifactCollector, PlaywrightVerificationArtifactCollector>();
        services.AddSingleton<ScreenshotEvidenceQualityChecker>();
        services.AddSingleton<ScreenshotVisualHeuristicEvaluator>();
        services.AddSingleton<ScreenshotVisualReviewService>();
        services.AddSingleton<ScreenshotLlmVisionEvidenceBuilder>();
        services.AddSingleton<DesktopScreenshotLlmVisionWorkflowService>();
        services.AddSingleton<VerificationArtifactEvidenceBuilder>();
        services.AddSingleton<DesktopVerificationRunner>();
        services.AddSingleton<VerificationFailureClassifier>();
        services.AddSingleton<DesktopVerificationWorkflowService>();
        services.AddSingleton<DesktopVerificationPanelWorkflowService>();
        services.AddSingleton<DesktopVerificationCommandService>();
        services.AddSingleton<DesktopGitService>();
        services.AddSingleton<DesktopGitPanelWorkflowService>();
        services.AddSingleton<DesktopGitCommandService>();
        services.AddSingleton<ProjectAgentConfigService>();
        services.AddSingleton<AgentCheckpointService>();
        services.AddSingleton<FileMutationSnapshotService>();
        services.AddSingleton<DesktopCheckpointWorkflowService>();
        services.AddSingleton<DesktopPlanWorkflowService>();
        services.AddSingleton<AgentPlanWorkerPlanAdapter>();
        services.AddSingleton<WorkerPlanApprovalSummaryBuilder>();
        services.AddSingleton<WorkerPlanValidator>();
        services.AddSingleton<WorkerPlanPreviewBuilder>();
        services.AddSingleton<WorkerExecutionPipeline>();
        services.AddSingleton<WorkerScaffoldExecutor>();
        services.AddSingleton<DesktopScaffoldIntentRouter>();
        services.AddSingleton<DesktopPlanApprovalPreviewService>();
        services.AddSingleton<DesktopPlanCheckpointWorkflowService>();
        services.AddSingleton<DesktopPlanCommandService>();
        services.AddSingleton<AgentSessionSummaryService>();
        services.AddSingleton<DesktopWorkspaceContextWorkflowService>();
        services.AddSingleton<DesktopWorkspaceCommandService>();
        services.AddSingleton<DesktopSourceBrowserService>();
        services.AddSingleton<DesktopCodePreviewWindowService>();
        services.AddSingleton<DesktopAgentRunWorkflowService>();
        services.AddSingleton<DesktopFileChangeReviewService>();
        services.AddSingleton<DesktopAttachmentSelectionService>();
        services.AddSingleton<DesktopClipboardService>();
        services.AddSingleton<AutoFixLoopGuard>();
        services.AddSingleton<DesktopAutoFixWorkflowService>();
        services.AddSingleton<DesktopWindowCommandService>();
        services.AddSingleton<DesktopPanelEventBinder>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
