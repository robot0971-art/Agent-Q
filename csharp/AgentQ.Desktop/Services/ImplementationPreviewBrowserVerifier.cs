using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AgentQ.Desktop.Services;

public sealed record ImplementationBrowserPreviewResult
{
    public required bool Succeeded { get; init; }

    public string DomHtml { get; init; } = string.Empty;

    public string ScreenshotDirectory { get; init; } = string.Empty;

    public IReadOnlyList<string> ScreenshotArtifacts { get; init; } = [];

    public IReadOnlyList<string> ConsoleErrors { get; init; } = [];

    public IReadOnlyList<string> VisualFindings { get; init; } = [];

    public string Summary
    {
        get
        {
            if (Succeeded)
            {
                return ScreenshotArtifacts.Count == 0
                    ? "Browser preview completed without screenshot artifacts."
                    : $"Browser preview captured {ScreenshotArtifacts.Count} screenshot artifact(s).";
            }

            var parts = new List<string>();
            if (ConsoleErrors.Count > 0)
            {
                parts.Add("console errors: " + string.Join("; ", ConsoleErrors));
            }

            if (VisualFindings.Count > 0)
            {
                parts.Add("visual findings: " + string.Join("; ", VisualFindings));
            }

            return parts.Count == 0
                ? "Browser preview verification did not produce required screenshot evidence."
                : string.Join(" | ", parts);
        }
    }
}

public interface IImplementationPreviewBrowserVerifier
{
    Task<ImplementationBrowserPreviewResult> VerifyAsync(
        string workspaceRoot,
        string url,
        CancellationToken ct);
}

public sealed class PlaywrightImplementationPreviewBrowserVerifier : IImplementationPreviewBrowserVerifier
{
    private const int TimeoutMilliseconds = 25000;

    public async Task<ImplementationBrowserPreviewResult> VerifyAsync(
        string workspaceRoot,
        string url,
        CancellationToken ct)
    {
        var root = Path.GetFullPath(workspaceRoot);
        if (string.IsNullOrWhiteSpace(url))
        {
            return Failed("No localhost preview URL was available for browser verification.");
        }

        if (!HasLocalPlaywright(root))
        {
            return Failed("Playwright is not installed in this workspace, so browser screenshot verification could not run.");
        }

        var previewDirectory = Path.Combine(root, ".agentq", "preview");
        if (!WorkspacePathResolver.IsResolvedInsideWorkspace(root, previewDirectory))
        {
            return Failed("Preview artifact directory resolves outside the workspace.");
        }

        Directory.CreateDirectory(previewDirectory);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        var scriptPath = Path.Combine(previewDirectory, $"{stamp}-playwright-preview.cjs");
        var desktopPath = Path.Combine(previewDirectory, $"{stamp}-desktop.png");
        var mobilePath = Path.Combine(previewDirectory, $"{stamp}-mobile.png");
        await File.WriteAllTextAsync(scriptPath, BuildPlaywrightScript(), ct);

        var output = await RunNodeAsync(root, scriptPath, url, desktopPath, mobilePath, ct);
        if (!output.Succeeded)
        {
            return Failed(output.ErrorMessage);
        }

        BrowserPreviewJson? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<BrowserPreviewJson>(output.StdOut, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true
            });
        }
        catch (JsonException ex)
        {
            return Failed("Playwright browser verification returned invalid JSON: " + ex.Message);
        }

        if (parsed == null)
        {
            return Failed("Playwright browser verification returned no result.");
        }

        var screenshotArtifacts = parsed.Screenshots
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => WorkspacePathResolver.IsResolvedInsideWorkspace(root, Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var visualFindings = ReviewScreenshots(root, screenshotArtifacts);
        var consoleErrors = parsed.ConsoleErrors
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        var succeeded = screenshotArtifacts.Count >= 2 && consoleErrors.Count == 0 && visualFindings.Count == 0;
        return new ImplementationBrowserPreviewResult
        {
            Succeeded = succeeded,
            DomHtml = parsed.DomHtml ?? string.Empty,
            ScreenshotDirectory = Path.GetRelativePath(root, previewDirectory).Replace('\\', '/'),
            ScreenshotArtifacts = screenshotArtifacts,
            ConsoleErrors = consoleErrors,
            VisualFindings = visualFindings
        };
    }

    private static ImplementationBrowserPreviewResult Failed(string message) =>
        new()
        {
            Succeeded = false,
            VisualFindings = [message]
        };

    private static bool HasLocalPlaywright(string workspaceRoot) =>
        Directory.Exists(Path.Combine(workspaceRoot, "node_modules", "playwright")) ||
        Directory.Exists(Path.Combine(workspaceRoot, "node_modules", "@playwright", "test"));

    private static IReadOnlyList<string> ReviewScreenshots(string workspaceRoot, IReadOnlyList<string> screenshotArtifacts)
    {
        var artifacts = screenshotArtifacts
            .Select(path => new VerificationArtifact
            {
                Kind = "screenshot",
                Path = path,
                Description = "Runtime preview screenshot evidence."
            })
            .ToList();
        var reviews = new ScreenshotVisualReviewService().Review(artifacts, workspaceRoot);
        return reviews
            .Where(review => review.Status != ScreenshotVisualReviewStatus.Pass)
            .Select(review => $"{review.RelativePath}: {review.Message}")
            .ToList();
    }

    private static async Task<BrowserProcessOutput> RunNodeAsync(
        string workspaceRoot,
        string scriptPath,
        string url,
        string desktopPath,
        string mobilePath,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(TimeoutMilliseconds));
        var startInfo = new ProcessStartInfo
        {
            FileName = "node",
            WorkingDirectory = workspaceRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add(url);
        startInfo.ArgumentList.Add(desktopPath);
        startInfo.ArgumentList.Add(mobilePath);

        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
            if (process == null)
            {
                return BrowserProcessOutput.Failed("Failed to start node for Playwright browser verification.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return process.ExitCode == 0
                ? BrowserProcessOutput.Success(stdout)
                : BrowserProcessOutput.Failed(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException or IOException)
        {
            OwnedProcessCleanup.TryKillTree(process);
            return BrowserProcessOutput.Failed("Playwright browser verification failed: " + ex.Message);
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static string BuildPlaywrightScript() =>
        """
        function loadChromium() {
          try {
            return require('playwright').chromium;
          } catch {
            return require('@playwright/test').chromium;
          }
        }

        const chromium = loadChromium();

        const [url, desktopPath, mobilePath] = process.argv.slice(2);
        const consoleErrors = [];
        const screenshots = [];

        async function capture(page, viewport, path) {
          await page.setViewportSize(viewport);
          await page.goto(url, { waitUntil: 'networkidle', timeout: 15000 });
          await page.screenshot({ path, fullPage: true });
          screenshots.push(path);
        }

        (async () => {
          const browser = await chromium.launch({ headless: true });
          const page = await browser.newPage();
          page.on('console', message => {
            if (message.type() === 'error') {
              consoleErrors.push(message.text());
            }
          });
          page.on('pageerror', error => consoleErrors.push(error.message));
          await capture(page, { width: 1366, height: 900 }, desktopPath);
          await capture(page, { width: 390, height: 844 }, mobilePath);
          const domHtml = await page.content();
          await browser.close();
          console.log(JSON.stringify({ domHtml, screenshots, consoleErrors }));
        })().catch(error => {
          console.error(error && error.stack ? error.stack : String(error));
          process.exit(1);
        });
        """;

    private sealed class BrowserPreviewJson
    {
        public string? DomHtml { get; set; }

        public List<string> Screenshots { get; set; } = [];

        public List<string> ConsoleErrors { get; set; } = [];
    }

    private sealed record BrowserProcessOutput(bool Succeeded, string StdOut, string ErrorMessage)
    {
        public static BrowserProcessOutput Success(string stdout) => new(true, stdout, string.Empty);

        public static BrowserProcessOutput Failed(string message) => new(false, string.Empty, message);
    }
}
