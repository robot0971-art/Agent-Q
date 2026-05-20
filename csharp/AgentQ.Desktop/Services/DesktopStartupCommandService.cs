using AgentQ.Core.Providers;
using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

public sealed class DesktopStartupCommandService(
    DesktopConfigService configService,
    DesktopWorkspaceContextWorkflowService workspaceContextWorkflowService)
{
    public async Task<DesktopStartupResult> InitializeAsync(
        MainViewModel viewModel,
        Func<string, string> trimForLog)
    {
        var saved = await configService.LoadAsync();
        if (saved != null)
        {
            viewModel.ApplyConfiguration(saved);
            viewModel.StatusText = "Settings loaded";
        }
        else
        {
            saved = new ProviderConfiguration
            {
                Provider = "opencode-go",
                Model = "kimi-k2.6",
                BaseUrl = ProviderConfiguration.OpenCodeGoDefaultBaseUrl,
                TimeoutSeconds = 30,
                MaxTokens = 4096
            };
            viewModel.ApplyConfiguration(saved);
            viewModel.StatusText = "First run: enter an API key, confirm provider/model, then save settings.";
            viewModel.AddLog("First run setup: enter an API key in Settings and click Save.");
        }

        viewModel.AddLog("AgentQ Desktop started");
        await workspaceContextWorkflowService.LoadWorkspaceContextAsync(viewModel, trimForLog);
        return new DesktopStartupResult(saved.ApiKey);
    }
}

public sealed record DesktopStartupResult(string ApiKey);
