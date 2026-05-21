using System.Diagnostics;
using System.IO;
using System.Windows;
using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

public sealed class DesktopWorkspaceCommandService(
    DesktopConfigService configService,
    DesktopWorkspaceContextWorkflowService workspaceContextWorkflowService,
    EmbeddingIndexBuilder embeddingIndexBuilder,
    DesktopEmbeddingClientFactory embeddingClientFactory,
    DesktopAttachmentSelectionService attachmentSelectionService,
    DesktopClipboardService clipboardService)
{
    public async Task SaveSettingsAsync(MainViewModel viewModel)
    {
        try
        {
            await configService.SaveAsync(viewModel.ToConfiguration());
            viewModel.StatusText = "Settings saved";
            viewModel.AddLog("Settings saved");
        }
        catch (Exception ex)
        {
            viewModel.StatusText = $"Settings save failed: {ex.Message}";
            viewModel.AddLog($"Settings save failed: {ex.Message}");
        }
    }

    public async Task BrowseWorkspaceAsync(
        Window owner,
        MainViewModel viewModel,
        Func<string, string> trimForLog)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select a project folder.",
            UseDescriptionForTitle = true,
            SelectedPath = string.IsNullOrWhiteSpace(viewModel.WorkspaceRoot)
                ? Environment.CurrentDirectory
                : viewModel.WorkspaceRoot
        };

        if (dialog.ShowDialog(owner.AsWin32Window()) != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        viewModel.WorkspaceRoot = dialog.SelectedPath;
        await workspaceContextWorkflowService.LoadWorkspaceContextAsync(viewModel, trimForLog);
        viewModel.StatusText = "Project folder selected";
        viewModel.AddLog($"Project folder selected: {dialog.SelectedPath}");
    }

    public async Task RefreshWorkspaceAnalysisAsync(MainViewModel viewModel, Func<string, string> trimForLog)
    {
        await workspaceContextWorkflowService.RefreshWorkspaceAnalysisAsync(viewModel, trimForLog);
    }

    public async Task BuildEmbeddingIndexAsync(MainViewModel viewModel, Func<string, string> trimForLog)
    {
        var config = viewModel.ToConfiguration();
        if (!DesktopEmbeddingClientFactory.SupportsProvider(config.EmbeddingProvider))
        {
            viewModel.StatusText = config.EmbeddingProvider.Equals("none", StringComparison.OrdinalIgnoreCase)
                ? "Embedding provider is disabled."
                : $"Embedding provider not supported: {config.EmbeddingProvider}";
            viewModel.AddLog($"Embedding index skipped: unsupported provider {config.EmbeddingProvider}");
            return;
        }

        if (string.IsNullOrWhiteSpace(config.EmbeddingApiKey))
        {
            viewModel.StatusText = "Embedding index needs an API key.";
            viewModel.AddLog("Embedding index skipped: missing API key.");
            return;
        }

        try
        {
            viewModel.IsBusy = true;
            viewModel.StatusText = "Building embedding index...";
            viewModel.AddLog("Embedding index build started");
            var client = embeddingClientFactory.Create(config);
            var result = await embeddingIndexBuilder.BuildVectorIndexAsync(
                viewModel.WorkspaceRoot,
                client,
                provider: config.EmbeddingProvider,
                model: string.IsNullOrWhiteSpace(config.EmbeddingModel)
                    ? DesktopEmbeddingClientFactory.ResolveEmbeddingModel(config.EmbeddingProvider)
                    : config.EmbeddingModel,
                maximumEmbeddedChunks: 200);

            viewModel.StatusText = $"Embedding index built: {result.Manifest.ChunkCount} chunks";
            viewModel.AddLog($"Embedding index built: {result.Manifest.FileCount} files, {result.Manifest.ChunkCount} chunks -> {result.Paths.ChunksPath}");
        }
        catch (Exception ex)
        {
            viewModel.StatusText = $"Embedding index failed: {ex.Message}";
            viewModel.AddLog($"Embedding index failed: {trimForLog(ex.Message)}");
        }
        finally
        {
            viewModel.IsBusy = false;
        }
    }

    public async Task SaveProjectConfigAsync(MainViewModel viewModel, Func<string, string> trimForLog)
    {
        try
        {
            await workspaceContextWorkflowService.SaveProjectConfigAsync(viewModel);
        }
        catch (Exception ex)
        {
            viewModel.StatusText = $"Project config save failed: {ex.Message}";
            viewModel.AddLog($"Project config save failed: {trimForLog(ex.Message)}");
        }
    }

    public async Task LoadProjectConfigAsync(MainViewModel viewModel)
    {
        await workspaceContextWorkflowService.LoadProjectConfigAsync(viewModel);
        viewModel.StatusText = workspaceContextWorkflowService.ProjectConfig == null
            ? "No project config found"
            : "Project config loaded";
    }

    public void OpenWorkspace(MainViewModel viewModel)
    {
        if (!Directory.Exists(viewModel.WorkspaceRoot))
        {
            viewModel.StatusText = "No valid project folder to open.";
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = viewModel.WorkspaceRoot,
            UseShellExecute = true
        });
    }

    public void SelectAttachments(Window owner, MainViewModel viewModel, ICollection<DesktopAttachment> attachments)
    {
        attachmentSelectionService.SelectAttachments(owner, viewModel, attachments);
    }

    public void ClearAttachments(MainViewModel viewModel, ICollection<DesktopAttachment> attachments)
    {
        attachmentSelectionService.ClearAttachments(viewModel, attachments);
    }

    public void CopyMessage(MainViewModel viewModel, ChatMessageViewModel? message)
    {
        clipboardService.CopyMessage(viewModel, message);
    }

    public void CopyLastAssistantMessage(MainViewModel viewModel)
    {
        clipboardService.CopyLastAssistantMessage(viewModel);
    }

    public void CopyConversation(MainViewModel viewModel)
    {
        clipboardService.CopyConversation(viewModel);
    }
}

file static class WindowFormsInterop
{
    public static System.Windows.Forms.IWin32Window AsWin32Window(this Window window)
    {
        return new Win32WindowHandle(new System.Windows.Interop.WindowInteropHelper(window).Handle);
    }

    private sealed class Win32WindowHandle(nint handle) : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; } = handle;
    }
}
