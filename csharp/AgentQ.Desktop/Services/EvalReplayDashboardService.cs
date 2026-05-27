using System.IO;
using System.Text.Json;

namespace AgentQ.Desktop.Services;

public sealed class EvalReplayDashboardService(ToolReplayService replayService)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<EvalReplayDashboardReport> BuildAsync(
        string workspaceRoot,
        IReadOnlyCollection<VerificationResultCard> verificationResults,
        CancellationToken ct = default)
    {
        var report = new EvalReplayDashboardReport
        {
            UpdatedAt = DateTime.Now
        };

        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            report.Summary = "Workspace unavailable.";
            report.Findings.Add("No workspace folder was available for replay or telemetry analysis.");
            return report;
        }

        var replay = await replayService.LoadLatestAsync(workspaceRoot, ct);
        var telemetry = await LoadTelemetryAsync(workspaceRoot, ct);

        AddReplay(report, replay);
        AddTelemetry(report, telemetry);
        AddLatencyDiagnostics(report, replay, telemetry, verificationResults);
        AddToolRoutingDiagnostics(report, replay);
        AddUnsafeEditingSignals(report, replay, telemetry);
        AddVerification(report, verificationResults);
        AddFailureFingerprints(report, replay, telemetry, verificationResults);

        report.Summary = BuildSummary(replay, telemetry, verificationResults);
        if (report.Findings.Count == 0)
        {
            report.Findings.Add("No failed tools, failed verification results, or recurring failure fingerprints detected.");
        }

        return report;
    }

    private static void AddReplay(EvalReplayDashboardReport report, ToolReplaySession? replay)
    {
        if (replay == null)
        {
            report.Metrics.Add("Replay: no saved session");
            report.ReplayEntries.Add("No replay session found.");
            return;
        }

        var failed = replay.Entries.Count(entry => entry.IsError);
        var totalDuration = replay.Entries.Sum(entry => Math.Max(entry.DurationMs, 0));
        report.Metrics.Add($"Replay: {replay.Entries.Count:0} tools, {failed:0} failed, {totalDuration:0} ms total");
        report.Metrics.Add($"Latest run: {replay.Provider}/{replay.Model} at {replay.CreatedAt:yyyy-MM-dd HH:mm:ss}");

        foreach (var entry in replay.Entries.OrderByDescending(entry => entry.IsError).ThenByDescending(entry => entry.DurationMs).Take(12))
        {
            var status = entry.IsError ? "FAILED" : "OK";
            report.ReplayEntries.Add($"{status} {entry.ToolName} {entry.DurationMs:0} ms - {Trim(entry.ResultPreview, 120)}");
        }

        foreach (var group in replay.Entries.Where(entry => entry.IsError).GroupBy(entry => entry.ToolName).OrderByDescending(group => group.Count()).Take(5))
        {
            report.Findings.Add($"Tool failure: {group.Key} failed {group.Count():0} time(s) in the latest replay.");
        }
    }

    private static void AddTelemetry(EvalReplayDashboardReport report, IReadOnlyList<DesktopTelemetryEvent> telemetry)
    {
        if (telemetry.Count == 0)
        {
            report.Metrics.Add("Telemetry: no events");
            return;
        }

        var failed = telemetry.Count(item => item.IsError || !item.Succeeded);
        var toolEvents = telemetry.Count(item => item.EventType.StartsWith("tool_", StringComparison.OrdinalIgnoreCase));
        var searchRetries = telemetry.Count(item => item.EventType.Equals("search_retry", StringComparison.OrdinalIgnoreCase));
        var inputTokens = telemetry.Sum(item => item.InputTokens);
        var outputTokens = telemetry.Sum(item => item.OutputTokens);

        report.Metrics.Add($"Telemetry: {telemetry.Count:0} events, {toolEvents:0} tool events, {failed:0} failed");
        report.Metrics.Add($"Search retries: {searchRetries:0}");
        report.Metrics.Add($"Tokens: {inputTokens:0} in / {outputTokens:0} out");

        foreach (var group in telemetry.Where(item => item.IsError || !item.Succeeded)
                     .GroupBy(item => string.IsNullOrWhiteSpace(item.ToolName) ? item.EventType : item.ToolName)
                     .OrderByDescending(group => group.Count())
                     .Take(5))
        {
            report.Findings.Add($"Telemetry failure: {group.Key} reported {group.Count():0} failed event(s).");
        }
    }

    private static void AddVerification(
        EvalReplayDashboardReport report,
        IReadOnlyCollection<VerificationResultCard> verificationResults)
    {
        if (verificationResults.Count == 0)
        {
            report.Metrics.Add("Verification: no results in panel");
            return;
        }

        var passed = verificationResults.Count(item => item.Status.Equals("PASSED", StringComparison.OrdinalIgnoreCase));
        var failed = verificationResults.Count(item => item.Status.Equals("FAILED", StringComparison.OrdinalIgnoreCase));
        var warnings = verificationResults.Count(item => item.Status.Equals("WARNING", StringComparison.OrdinalIgnoreCase));
        report.Metrics.Add($"Verification: {passed:0} passed, {failed:0} failed, {warnings:0} warning");

        foreach (var result in verificationResults.Where(item => !item.Status.Equals("PASSED", StringComparison.OrdinalIgnoreCase)).Take(5))
        {
            report.Findings.Add($"Verification {result.Status}: {result.Title} - {Trim(result.Summary, 140)}");
        }
    }

    private static void AddLatencyDiagnostics(
        EvalReplayDashboardReport report,
        ToolReplaySession? replay,
        IReadOnlyList<DesktopTelemetryEvent> telemetry,
        IReadOnlyCollection<VerificationResultCard> verificationResults)
    {
        if (replay == null && telemetry.Count == 0 && verificationResults.Count == 0)
        {
            return;
        }

        var toolEntries = replay?.Entries ?? [];
        var toolDurationMs = toolEntries.Sum(entry => Math.Max(0, entry.DurationMs));
        var telemetryDurationMs = telemetry.Sum(item => Math.Max(0, item.DurationMs));
        var tokenEvents = telemetry.Where(item => item.InputTokens > 0 || item.OutputTokens > 0).ToList();
        var inputTokens = tokenEvents.Sum(item => item.InputTokens);
        var outputTokens = tokenEvents.Sum(item => item.OutputTokens);
        var retryCount = telemetry.Count(item => item.EventType.Equals("search_retry", StringComparison.OrdinalIgnoreCase));
        var verificationCount = verificationResults.Count;

        report.Metrics.Add(
            $"Latency: tools {toolDurationMs:0} ms, telemetry-measured {telemetryDurationMs:0} ms, retries {retryCount:0}, verification cards {verificationCount:0}");

        if (inputTokens > 0 || outputTokens > 0)
        {
            report.Metrics.Add($"LLM usage: {inputTokens:0} input tokens / {outputTokens:0} output tokens across {tokenEvents.Count:0} event(s)");
        }

        var slowestTool = toolEntries
            .OrderByDescending(entry => Math.Max(0, entry.DurationMs))
            .FirstOrDefault();
        if (slowestTool != null)
        {
            report.Metrics.Add($"Slowest tool: {slowestTool.ToolName} {Math.Max(0, slowestTool.DurationMs):0} ms");
        }

        var repeatedTools = toolEntries
            .GroupBy(entry => entry.ToolName)
            .Where(group => group.Count() > 1)
            .OrderByDescending(group => group.Count())
            .Take(3)
            .Select(group => $"{group.Key} x{group.Count():0}");
        var repeatedText = string.Join(", ", repeatedTools);
        if (!string.IsNullOrWhiteSpace(repeatedText))
        {
            report.Metrics.Add($"Repeated tools: {repeatedText}");
        }
    }

    private static void AddToolRoutingDiagnostics(EvalReplayDashboardReport report, ToolReplaySession? replay)
    {
        if (replay?.Entries.Count is not > 0)
        {
            return;
        }

        var routedEntries = replay.Entries
            .Where(entry => ClassifyToolRoute(entry.ToolName) != "other")
            .ToList();
        if (routedEntries.Count == 0)
        {
            return;
        }

        foreach (var group in routedEntries
                     .GroupBy(entry => ClassifyToolRoute(entry.ToolName))
                     .OrderByDescending(group => group.Count())
                     .ThenBy(group => group.Key)
                     .Take(6))
        {
            var failed = group.Count(entry => entry.IsError);
            report.Metrics.Add($"Tool routing: {group.Key} {group.Count():0} call(s), {failed:0} failed");
        }
    }

    private static string ClassifyToolRoute(string toolName)
    {
        return toolName switch
        {
            "read_file" => "file-read",
            "grep_search" or "glob_search" => "keyword-search",
            "symbol_search" => "symbol-search",
            "hybrid_search" => "hybrid-search",
            "semantic_search" => "semantic-search",
            _ when toolName.StartsWith("mcp_", StringComparison.OrdinalIgnoreCase) => "mcp-bridge",
            _ => "other"
        };
    }

    private static void AddUnsafeEditingSignals(
        EvalReplayDashboardReport report,
        ToolReplaySession? replay,
        IReadOnlyList<DesktopTelemetryEvent> telemetry)
    {
        var replaySignals = replay?.Entries
            .Where(entry => entry.IsError && IsUnsafeEditingSignal(entry.ResultPreview))
            .ToList() ?? [];
        var telemetrySignals = telemetry
            .Where(item => (item.IsError || !item.Succeeded) && IsUnsafeEditingSignal(item.Detail))
            .ToList();

        var total = replaySignals.Count + telemetrySignals.Count;
        if (total == 0)
        {
            return;
        }

        report.Findings.Add($"Unsafe editing signal: {total:0} edit recovery or high-risk edit warning(s) detected.");

        foreach (var entry in replaySignals.Take(3))
        {
            report.Findings.Add($"Unsafe edit replay: {entry.ToolName} - {Trim(entry.ResultPreview, 140)}");
        }

        foreach (var item in telemetrySignals.Take(3))
        {
            var label = string.IsNullOrWhiteSpace(item.ToolName) ? item.EventType : item.ToolName;
            report.Findings.Add($"Unsafe edit telemetry: {label} - {Trim(item.Detail, 140)}");
        }
    }

    private static bool IsUnsafeEditingSignal(string value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               (value.Contains("Repeated edit failure", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("high-risk", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("whole-file rewrite", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("manual copy-paste", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("git restore", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("git checkout", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("destructive restore", StringComparison.OrdinalIgnoreCase));
    }

    private static void AddFailureFingerprints(
        EvalReplayDashboardReport report,
        ToolReplaySession? replay,
        IReadOnlyList<DesktopTelemetryEvent> telemetry,
        IReadOnlyCollection<VerificationResultCard> verificationResults)
    {
        var fingerprints = new List<FailureFingerprintSignal>();
        if (replay != null)
        {
            fingerprints.AddRange(replay.Entries
                .Where(entry => entry.IsError)
                .Select(entry => CreateFailureSignal(entry.ToolName, entry.ResultPreview, $"replay:{entry.ToolName}"))
                .Where(signal => !string.IsNullOrWhiteSpace(signal.Fingerprint)));
        }

        fingerprints.AddRange(telemetry
            .Where(item => item.IsError || !item.Succeeded)
            .Select(item =>
            {
                var label = string.IsNullOrWhiteSpace(item.ToolName) ? item.EventType : item.ToolName;
                return CreateFailureSignal(label, item.Detail, $"telemetry:{label}");
            })
            .Where(signal => !string.IsNullOrWhiteSpace(signal.Fingerprint)));

        fingerprints.AddRange(verificationResults
            .Where(item => !item.Status.Equals("PASSED", StringComparison.OrdinalIgnoreCase))
            .Select(item => CreateFailureSignal(item.Title, $"{item.Detail}\n{item.OutputPreview}", $"verification:{item.Title}"))
            .Where(signal => !string.IsNullOrWhiteSpace(signal.Fingerprint)));

        foreach (var group in fingerprints.GroupBy(value => value.Fingerprint).Where(group => group.Count() > 1).OrderByDescending(group => group.Count()).Take(8))
        {
            var sources = string.Join(", ", group.Select(item => item.Source).Distinct().Take(3));
            var summary = $"{group.Key} x{group.Count():0} ({sources})";
            report.FailureFingerprints.Add(summary);
            report.Findings.Add($"Recurring failure: {summary}");
        }

        if (report.FailureFingerprints.Count == 0)
        {
            report.FailureFingerprints.Add("No recurring failure fingerprint detected.");
        }
    }

    private static FailureFingerprintSignal CreateFailureSignal(string title, string detail, string source)
    {
        return new FailureFingerprintSignal(
            FailureFingerprintService.Create(title, detail),
            source);
    }

    private static async Task<IReadOnlyList<DesktopTelemetryEvent>> LoadTelemetryAsync(string workspaceRoot, CancellationToken ct)
    {
        var path = DesktopTelemetryService.GetTelemetryPath(workspaceRoot);
        if (!File.Exists(path))
        {
            return [];
        }

        var events = new List<DesktopTelemetryEvent>();
        foreach (var line in File.ReadLines(path).Reverse().Take(500).Reverse())
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var item = JsonSerializer.Deserialize<DesktopTelemetryEvent>(line, Options);
                if (item != null)
                {
                    events.Add(item);
                }
            }
            catch (JsonException)
            {
                // A partial telemetry line should not break the dashboard.
            }
        }

        return events;
    }

    private static string BuildSummary(
        ToolReplaySession? replay,
        IReadOnlyList<DesktopTelemetryEvent> telemetry,
        IReadOnlyCollection<VerificationResultCard> verificationResults)
    {
        var replayText = replay == null
            ? "no replay"
            : $"{replay.Entries.Count:0} replay tools";
        var telemetryText = telemetry.Count == 0
            ? "no telemetry"
            : $"{telemetry.Count:0} telemetry events";
        var verificationText = verificationResults.Count == 0
            ? "no verification cards"
            : $"{verificationResults.Count:0} verification cards";
        return $"{replayText}, {telemetryText}, {verificationText}";
    }

    private static string Trim(string value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "No detail.";
        }

        value = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return value.Length <= max ? value : value[..max] + "...";
    }
}

public sealed class EvalReplayDashboardReport
{
    public DateTime UpdatedAt { get; set; }

    public string Summary { get; set; } = string.Empty;

    public List<string> Metrics { get; set; } = [];

    public List<string> Findings { get; set; } = [];

    public List<string> ReplayEntries { get; set; } = [];

    public List<string> FailureFingerprints { get; set; } = [];
}

internal sealed record FailureFingerprintSignal(string Fingerprint, string Source);
