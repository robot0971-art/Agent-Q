using System.IO;
using System.Net.Http;
using System.Text.Json;
using AgentQ.Tools;

namespace AgentQ.Desktop.Services;

public sealed record ImplementationRuntimePreviewResult
{
    public required bool Succeeded { get; init; }

    public required LocalServerStartResult LocalServer { get; init; }

    public required ImplementationPreviewVerificationResult Preview { get; init; }

    public required ImplementationBrowserPreviewResult Browser { get; init; }

    public string DomSnapshotPath { get; init; } = string.Empty;

    public string Summary => Succeeded
        ? $"Runtime preview verified at {LocalServer.Url}."
        : string.IsNullOrWhiteSpace(Preview.Summary)
            ? LocalServer.Message
            : Preview.Summary;
}

public sealed class ImplementationRuntimePreviewService
{
    private readonly DesktopLocalServerService _localServerService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IImplementationPreviewBrowserVerifier _browserVerifier;

    public ImplementationRuntimePreviewService(
        DesktopLocalServerService localServerService,
        IHttpClientFactory httpClientFactory,
        IImplementationPreviewBrowserVerifier? browserVerifier = null)
    {
        _localServerService = localServerService;
        _httpClientFactory = httpClientFactory;
        _browserVerifier = browserVerifier ?? new PlaywrightImplementationPreviewBrowserVerifier();
    }

    public async Task<ImplementationRuntimePreviewResult> VerifyAsync(
        string workspaceRoot,
        ImplementationContract contract,
        IPermissionEnforcer permissionEnforcer,
        DesktopToolCallbacks? callbacks,
        CancellationToken ct)
    {
        var localServer = await _localServerService.StartAsync(
            workspaceRoot,
            permissionEnforcer,
            callbacks,
            ct);
        if (!localServer.Succeeded)
        {
            return new ImplementationRuntimePreviewResult
            {
                Succeeded = false,
                LocalServer = localServer,
                Preview = new ImplementationPreviewVerificationResult
                {
                    Succeeded = false,
                    RequiresPreviewEvidence = true,
                    RootRendered = false,
                    MissingDomRequirements = ["runtime-preview: Localhost preview did not start or respond."],
                    ConsoleErrors = [],
                    VisualFindings = [],
                    Url = localServer.Url
                },
                Browser = new ImplementationBrowserPreviewResult
                {
                    Succeeded = false,
                    VisualFindings = ["Browser preview did not run because localhost preview did not start or respond."]
                }
            };
        }

        var html = await FetchHtmlAsync(localServer.Url, ct);
        var browser = await _browserVerifier.VerifyAsync(workspaceRoot, localServer.Url, ct);
        var evidenceHtml = string.IsNullOrWhiteSpace(browser.DomHtml) ? html : browser.DomHtml;
        var snapshotPath = await SaveDomSnapshotAsync(workspaceRoot, evidenceHtml, ct);
        var screenshotDirectory = string.IsNullOrWhiteSpace(browser.ScreenshotDirectory)
            ? Path.GetDirectoryName(snapshotPath) ?? string.Empty
            : browser.ScreenshotDirectory;
        var preview = ImplementationCompletionService.VerifyPreviewEvidence(
            evidenceHtml,
            contract,
            browser.ConsoleErrors,
            browser.VisualFindings,
            url: localServer.Url,
            screenshotDirectory: screenshotDirectory);
        callbacks?.OnRunStep?.Invoke(
            preview.Succeeded ? AgentRunState.Done : AgentRunState.Failed,
            preview.Succeeded ? "Runtime preview verified" : "Runtime preview failed",
            string.IsNullOrWhiteSpace(browser.Summary)
                ? preview.Summary
                : preview.Summary + " " + browser.Summary);

        return new ImplementationRuntimePreviewResult
        {
            Succeeded = preview.Succeeded,
            LocalServer = localServer,
            Preview = preview,
            Browser = browser,
            DomSnapshotPath = snapshotPath
        };
    }

    private async Task<string> FetchHtmlAsync(string url, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("implementation-preview");
            client.Timeout = TimeSpan.FromSeconds(5);
            return await client.GetStringAsync(url, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return $"Runtime preview fetch failed: {ex.Message}";
        }
    }

    private static async Task<string> SaveDomSnapshotAsync(string workspaceRoot, string html, CancellationToken ct)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var directory = Path.Combine(root, ".agentq", "preview");
        if (!WorkspacePathResolver.IsResolvedInsideWorkspace(root, directory))
        {
            return string.Empty;
        }

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-dom.html");
        await File.WriteAllTextAsync(path, html, ct);
        return Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    public static ToolReplayEntry CreateReplayEntry(ImplementationRuntimePreviewResult result)
    {
        var now = DateTime.Now;
        return new ToolReplayEntry
        {
            StartedAt = now,
            CompletedAt = now,
            ToolName = "implementation_runtime_preview",
            ToolUseId = "implementation_runtime_preview_" + Guid.NewGuid().ToString("N"),
            InputJson = JsonSerializer.Serialize(new
            {
                url = result.LocalServer.Url,
                command = result.LocalServer.Command
            }),
            ResultPreview = JsonSerializer.Serialize(new
            {
                result.Succeeded,
                result.LocalServer.Url,
                result.DomSnapshotPath,
                result.Preview.ScreenshotDirectory,
                result.Browser.ScreenshotArtifacts,
                result.Browser.ConsoleErrors,
                result.Browser.VisualFindings,
                result.Preview.Summary
            }),
            IsError = !result.Succeeded
        };
    }
}
