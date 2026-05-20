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
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<LinkContentFetcher>();
        services.AddSingleton<ProjectMemoryService>();
        services.AddSingleton<WorkspaceIndexer>();
        services.AddSingleton<DesktopConfigService>();
        services.AddSingleton<DesktopAgentService>();
        services.AddSingleton<DesktopVerificationRunner>();
        services.AddSingleton<VerificationFailureClassifier>();
        services.AddSingleton<DesktopVerificationWorkflowService>();
        services.AddSingleton<DesktopVerificationPanelWorkflowService>();
        services.AddSingleton<DesktopGitService>();
        services.AddSingleton<DesktopGitPanelWorkflowService>();
        services.AddSingleton<DesktopGitCommandService>();
        services.AddSingleton<WorkspaceAnalysisService>();
        services.AddSingleton<ProjectAgentConfigService>();
        services.AddSingleton<AgentCheckpointService>();
        services.AddSingleton<DesktopCheckpointWorkflowService>();
        services.AddSingleton<DesktopPlanWorkflowService>();
        services.AddSingleton<DesktopPlanCheckpointWorkflowService>();
        services.AddSingleton<DesktopPlanCommandService>();
        services.AddSingleton<AgentSessionSummaryService>();
        services.AddSingleton<DesktopWorkspaceContextWorkflowService>();
        services.AddSingleton<DesktopWorkspaceCommandService>();
        services.AddSingleton<DesktopAgentRunWorkflowService>();
        services.AddSingleton<DesktopFileChangeReviewService>();
        services.AddSingleton<DesktopAttachmentSelectionService>();
        services.AddSingleton<DesktopClipboardService>();
        services.AddSingleton<DesktopAutoFixWorkflowService>();
        services.AddSingleton<DesktopWindowCommandService>();
        services.AddSingleton<DesktopPanelEventBinder>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
