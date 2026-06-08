using System.Text;
using System.Windows;
using System.Windows.Threading;
using AgentQ.Core.Providers;
using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

public sealed class DesktopAgentRunWorkflowService(
    DesktopAgentService agentService,
    DesktopWorkspaceContextWorkflowService workspaceContextWorkflowService,
    DesktopVerificationPanelWorkflowService verificationPanelWorkflowService,
    DesktopLearningSuggestionService learningSuggestionService,
    DesktopTelemetryService telemetryService,
    DesktopDiagnosticsService diagnosticsService)
{
    private const string ThinkingPlaceholder = "\uC0DD\uAC01\uC911...";
    private const string ContinuationPrompt =
        "Continue the previous run from where it stopped. Do not repeat completed work. Inspect current files or command results if needed, then continue until the task is complete or you need user input.";
    private readonly DesktopUsageTracker _usageTracker = new();
    private CancellationTokenSource? _activeOperationCts;
    private DesktopPermissionEnforcer? _activePermissionEnforcer;
    private string _activeWorkspaceRoot = string.Empty;
    private string _activeProvider = string.Empty;
    private string _activeModel = string.Empty;

    public void Stop(MainViewModel viewModel)
    {
        if (!viewModel.IsBusy)
        {
            viewModel.StatusText = "AgentQ is idle";
            return;
        }

        _activeOperationCts?.Cancel();
        diagnosticsService.Record(
            "stop_requested",
            "User requested Stop.",
            _activeWorkspaceRoot,
            _activeProvider,
            _activeModel);
        RecordTelemetry(
            "stop_requested",
            _activeWorkspaceRoot,
            _activeProvider,
            _activeModel,
            succeeded: false,
            detail: "User requested Stop.");
        RemoveThinkingPlaceholder(viewModel);
        viewModel.AddRunStep(AgentRunState.Cancelled, "Stop requested", "Cancelling the active model, tool, or verification operation.");
        viewModel.StatusText = "Stopping AgentQ";
        viewModel.AddLog("Stop requested");
    }

    public void SetActiveOperation(CancellationTokenSource operationCts)
    {
        _activeOperationCts = operationCts;
    }

    public void ClearActiveOperation(CancellationTokenSource operationCts)
    {
        if (ReferenceEquals(_activeOperationCts, operationCts))
        {
            _activeOperationCts = null;
        }
    }

    public void ClearRunPermissions(MainViewModel viewModel)
    {
        if (_activePermissionEnforcer == null)
        {
            viewModel.ClearRunPermissionStatus();
            viewModel.StatusText = "No run permissions to reset";
            return;
        }

        _activePermissionEnforcer.ClearRunApprovals();
        viewModel.StatusText = "Run permissions reset";
        viewModel.AddLog("Run permissions reset");
    }

    public async Task SendCurrentMessageAsync(
        MainViewModel viewModel,
        List<DesktopAttachment> attachments,
        Window owner,
        Dispatcher dispatcher,
        Func<string, string> trimForLog,
        bool preserveLastVerificationFailure = false)
    {
        var prompt = viewModel.InputText.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        if (viewModel.IsBusy)
        {
            Stop(viewModel);
            viewModel.StatusText = "Stopping current run; send again to redirect";
            viewModel.AddLog("New input requested while busy; active run is being stopped.");
            return;
        }

        viewModel.InputText = string.Empty;
        viewModel.CanContinueLastRun = false;
        viewModel.LastContinuationPrompt = string.Empty;
        var attachmentsForRequest = attachments.ToList();
        var messageAttachments = attachmentsForRequest.Select(DesktopAttachmentWorkflowService.ToViewModel).ToList();
        viewModel.Messages.Add(new ChatMessageViewModel
        {
            Role = "User",
            Content = prompt,
            Attachments = messageAttachments
        });

        var assistantMessage = new ChatMessageViewModel { Role = "AgentQ", Content = ThinkingPlaceholder };
        viewModel.Messages.Add(assistantMessage);
        var assistantIndex = viewModel.Messages.Count - 1;
        viewModel.RunSteps.Clear();
        viewModel.VerificationPlans.Clear();
        if (preserveLastVerificationFailure)
        {
            verificationPanelWorkflowService.RestoreRetryPlan(viewModel);
        }
        else
        {
            verificationPanelWorkflowService.ClearFailure(viewModel);
        }

        viewModel.IsBusy = true;
        viewModel.StatusText = "Generating response...";
        viewModel.AddLog("Model call started");
        diagnosticsService.SetActiveWorkspace(viewModel.WorkspaceRoot);
        diagnosticsService.Record(
            "send_requested",
            $"attachments={attachmentsForRequest.Count}; preserveLastVerificationFailure={preserveLastVerificationFailure}; prompt=\"{trimForLog(prompt)}\"",
            viewModel.WorkspaceRoot,
            viewModel.Provider,
            viewModel.Model);
        foreach (var visualEvidence in VisualEvidenceService.InspectAttachments(attachmentsForRequest))
        {
            viewModel.AddRunStep(
                AgentRunState.GatheringContext,
                "Evidence: visual attachment",
                VisualEvidenceService.BuildTimelineDetail(visualEvidence));
        }

        CancellationTokenSource? operationCts = null;
        DesktopPermissionEnforcer? permissionEnforcer = null;
        var startedAt = DateTime.UtcNow;

        try
        {
            operationCts = CreateTimeout(viewModel.TimeoutSeconds);
            SetActiveOperation(operationCts);
            var config = viewModel.ToConfiguration();
            var workspaceRoot = viewModel.WorkspaceRoot;
            var workMode = viewModel.WorkMode;
            viewModel.AddLog($"Link auto-read: {(config.DesktopAutoFetchLinks ? "enabled" : "disabled")}");
            _activeWorkspaceRoot = workspaceRoot;
            _activeProvider = config.Provider;
            _activeModel = config.Model;
            diagnosticsService.SetActiveWorkspace(workspaceRoot);
            diagnosticsService.Record(
                "run_started",
                $"workMode={workMode}; timeoutSeconds={viewModel.TimeoutSeconds}; provider={config.Provider}; model={config.Model}; baseUrl={config.BaseUrl}",
                workspaceRoot,
                config.Provider,
                config.Model);
            RecordTelemetry(
                "run_started",
                workspaceRoot,
                config.Provider,
                config.Model,
                succeeded: true,
                detail: workMode.ToString());
            permissionEnforcer = new DesktopPermissionEnforcer(owner, workMode, viewModel.IsKoreanUi, workspaceRoot);
            _activePermissionEnforcer = permissionEnforcer;
            viewModel.ClearRunPermissionStatus();
            permissionEnforcer.ApprovedForRunChanged += approved =>
                dispatcher.Invoke(() => viewModel.SetRunPermissionApprovals(approved));
            permissionEnforcer.PermissionEventRecorded += permissionEvent =>
            {
                void Record()
                {
                    viewModel.AddLog($"Permission: {permissionEvent.DisplayText}");
                    diagnosticsService.Record(
                        "permission_event",
                        permissionEvent.DisplayText,
                        workspaceRoot,
                        config.Provider,
                        config.Model);
                    viewModel.AddRunStep(
                        permissionEvent.Outcome.Contains("Denied", StringComparison.OrdinalIgnoreCase) ||
                        permissionEvent.Outcome.Contains("Blocked", StringComparison.OrdinalIgnoreCase)
                            ? AgentRunState.WaitingForApproval
                            : AgentRunState.RunningTool,
                        $"Permission: {permissionEvent.Outcome}",
                        permissionEvent.DisplayText);
                }

                if (dispatcher.CheckAccess())
                {
                    Record();
                }
                else
                {
                    dispatcher.Invoke(Record);
                }
            };
            var pendingDelta = new StringBuilder();
            var pendingDeltaLock = new object();
            var deltaFlushQueued = false;

            void QueueAssistantDelta(string delta)
            {
                lock (pendingDeltaLock)
                {
                    pendingDelta.Append(delta);
                    if (deltaFlushQueued)
                    {
                        return;
                    }

                    deltaFlushQueued = true;
                }

                dispatcher.BeginInvoke(() =>
                {
                    string text;
                    lock (pendingDeltaLock)
                    {
                        text = pendingDelta.ToString();
                        pendingDelta.Clear();
                        deltaFlushQueued = false;
                    }

                    AppendAssistantDelta(viewModel, assistantMessage, assistantIndex, text);
                }, DispatcherPriority.Background);
            }

            void FlushAssistantDelta()
            {
                string text;
                lock (pendingDeltaLock)
                {
                    text = pendingDelta.ToString();
                    pendingDelta.Clear();
                    deltaFlushQueued = false;
                }

                if (!string.IsNullOrEmpty(text))
                {
                    dispatcher.Invoke(() => AppendAssistantDelta(viewModel, assistantMessage, assistantIndex, text));
                }
            }

            var toolCallbacks = DesktopToolCallbacksFactory.Create(
                viewModel,
                dispatcher,
                trimForLog,
                usage =>
                {
                    var snapshot = _usageTracker.RecordActual(usage);
                    viewModel.UsageText = snapshot.DisplayText;
                    viewModel.AddLog($"Usage recorded: {snapshot.DisplayText}");
                    RecordUsageTelemetry("usage_actual", workspaceRoot, config, snapshot);
                });
            var telemetryCallbacks = WrapTelemetryCallbacks(toolCallbacks, workspaceRoot, config);
            var fullText = await Task.Run(async () =>
                await agentService.SendAsync(
                    config,
                    prompt,
                    attachmentsForRequest,
                    workspaceRoot,
                    workMode,
                    QueueAssistantDelta,
                    permissionEnforcer,
                    telemetryCallbacks,
                    operationCts.Token),
                operationCts.Token);
            FlushAssistantDelta();
            fullText = ModelReasoningTagFilter.Strip(fullText);
            diagnosticsService.Record(
                "model_response_received",
                $"chars={fullText.Length}; empty={string.IsNullOrWhiteSpace(fullText)}",
                workspaceRoot,
                config.Provider,
                config.Model);

            if (assistantIndex >= 0 &&
                assistantIndex < viewModel.Messages.Count &&
                !string.Equals(viewModel.Messages[assistantIndex].Content, fullText, StringComparison.Ordinal))
            {
                viewModel.Messages[assistantIndex] = new ChatMessageViewModel
                {
                    Role = "AgentQ",
                    Content = string.IsNullOrWhiteSpace(fullText) ? viewModel.Messages[assistantIndex].Content : fullText,
                    CreatedAt = assistantMessage.CreatedAt
                };
            }

            if (string.IsNullOrWhiteSpace(fullText) &&
                assistantIndex >= 0 &&
                assistantIndex < viewModel.Messages.Count)
            {
                viewModel.Messages[assistantIndex] = new ChatMessageViewModel
                {
                    Role = "AgentQ",
                    Content = "(empty response)",
                    CreatedAt = assistantMessage.CreatedAt
                };
            }

            UpdateContinuationState(viewModel, fullText);
            AddLearningCandidates(viewModel, prompt, fullText);
            viewModel.StatusText = "Response complete";
            viewModel.AddLog("Model call completed");
            var usage = _usageTracker.RecordEstimate(prompt, fullText);
            if (usage.LastTotalTokens > 0 || usage.IsEstimate)
            {
                viewModel.UsageText = usage.DisplayText;
                viewModel.AddLog($"Usage recorded: {usage.DisplayText}");
                RecordUsageTelemetry("usage_estimate", workspaceRoot, config, usage);
            }
            ClearPendingAttachmentsAfterSuccessfulSend(viewModel, attachments);
            await workspaceContextWorkflowService.SaveSessionSummaryAsync(
                viewModel,
                "Session summary auto-saved",
                trimForLog);
            RecordTelemetry(
                "run_completed",
                workspaceRoot,
                config.Provider,
                config.Model,
                succeeded: true,
                durationMs: (int)(DateTime.UtcNow - startedAt).TotalMilliseconds,
                detail: $"files={viewModel.FileChanges.Count}; verificationPlans={viewModel.VerificationPlans.Count}");
            diagnosticsService.Record(
                "run_completed",
                $"durationMs={(int)(DateTime.UtcNow - startedAt).TotalMilliseconds}; files={viewModel.FileChanges.Count}; verificationPlans={viewModel.VerificationPlans.Count}; responseChars={fullText.Length}",
                workspaceRoot,
                config.Provider,
                config.Model);
        }
        catch (OperationCanceledException)
        {
            diagnosticsService.Record(
                "run_cancelled",
                $"durationMs={(int)(DateTime.UtcNow - startedAt).TotalMilliseconds}",
                viewModel.WorkspaceRoot,
                _activeProvider,
                _activeModel);
            RemoveThinkingPlaceholder(viewModel);
            viewModel.AddRunStep(AgentRunState.Cancelled, "Run cancelled", "The request was cancelled or timed out.");
            AddAttachmentRetryLog(viewModel, attachmentsForRequest.Count);
            viewModel.StatusText = "Request cancelled or timed out.";
            viewModel.AddLog("Request cancelled or timed out");
            RecordTelemetry(
                "run_cancelled",
                viewModel.WorkspaceRoot,
                _activeProvider,
                _activeModel,
                succeeded: false,
                durationMs: (int)(DateTime.UtcNow - startedAt).TotalMilliseconds);
        }
        catch (Exception ex)
        {
            diagnosticsService.Record(
                "run_exception",
                $"durationMs={(int)(DateTime.UtcNow - startedAt).TotalMilliseconds}",
                viewModel.WorkspaceRoot,
                _activeProvider,
                _activeModel,
                ex);
            var providerError = DesktopProviderFailureClassifier.Describe(ex);
            viewModel.AddRunStep(AgentRunState.Failed, providerError.Title, providerError.Detail);
            AddAttachmentRetryLog(viewModel, attachmentsForRequest.Count);
            viewModel.StatusText = providerError.StatusText;
            viewModel.AddLog(providerError.LogText);
            RecordTelemetry(
                "run_failed",
                viewModel.WorkspaceRoot,
                _activeProvider,
                _activeModel,
                succeeded: false,
                isError: true,
                durationMs: (int)(DateTime.UtcNow - startedAt).TotalMilliseconds,
                detail: providerError.Detail);
        }
        finally
        {
            if (operationCts != null)
            {
                ClearActiveOperation(operationCts);
                operationCts.Dispose();
            }

            if (ReferenceEquals(_activePermissionEnforcer, permissionEnforcer))
            {
                _activePermissionEnforcer = null;
            }

            viewModel.IsBusy = false;
            _activeWorkspaceRoot = string.Empty;
            _activeProvider = string.Empty;
            _activeModel = string.Empty;
        }
    }

    public void ClearConversation(MainViewModel viewModel)
    {
        agentService.ClearConversation();
        viewModel.Messages.Clear();
        viewModel.CanContinueLastRun = false;
        viewModel.LastContinuationPrompt = string.Empty;
        viewModel.AddLog("Conversation cleared");
        viewModel.StatusText = "Conversation cleared";
    }

    public bool PrepareContinuation(MainViewModel viewModel)
    {
        if (!viewModel.CanContinueLastRun || string.IsNullOrWhiteSpace(viewModel.LastContinuationPrompt))
        {
            viewModel.StatusText = "No paused run to continue";
            return false;
        }

        viewModel.InputText = viewModel.LastContinuationPrompt;
        viewModel.CanContinueLastRun = false;
        viewModel.AddLog("Continuation prompt prepared");
        return true;
    }

    private static void ClearPendingAttachmentsAfterSuccessfulSend(
        MainViewModel viewModel,
        ICollection<DesktopAttachment> attachments)
    {
        if (!DesktopAttachmentWorkflowService.ClearAfterSuccessfulSend(attachments, viewModel.Attachments))
        {
            return;
        }

        viewModel.AddLog("Attachments cleared after successful send");
    }

    private static void AddAttachmentRetryLog(MainViewModel viewModel, int attachmentCount)
    {
        var log = DesktopAttachmentWorkflowService.BuildRetryLog(attachmentCount);
        if (!string.IsNullOrWhiteSpace(log))
        {
            viewModel.AddLog(log);
        }
    }

    public static void RemoveThinkingPlaceholder(MainViewModel viewModel)
    {
        for (var i = viewModel.Messages.Count - 1; i >= 0; i--)
        {
            var message = viewModel.Messages[i];
            if (message.Role == "AgentQ" && message.Content == ThinkingPlaceholder)
            {
                viewModel.Messages.RemoveAt(i);
                return;
            }
        }
    }

    private static void UpdateContinuationState(MainViewModel viewModel, string fullText)
    {
        var hitToolStepLimit = fullText.Contains(
            "Stopped after reaching the maximum tool steps",
            StringComparison.OrdinalIgnoreCase);
        viewModel.CanContinueLastRun = hitToolStepLimit;
        viewModel.LastContinuationPrompt = hitToolStepLimit ? ContinuationPrompt : string.Empty;
        if (hitToolStepLimit)
        {
            viewModel.AddLog("Run can be continued from the tool step limit.");
        }
    }

    private void AddLearningCandidates(MainViewModel viewModel, string prompt, string fullText)
    {
        foreach (var lesson in learningSuggestionService.SuggestLessons(prompt, fullText, viewModel))
        {
            if (viewModel.PendingMemoryLessons.Any(existing =>
                    string.Equals(existing.Id, lesson.Id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            viewModel.PendingMemoryLessons.Add(lesson);
        }

        if (viewModel.PendingMemoryLessons.Count > 0)
        {
            viewModel.SelectedPendingMemoryLesson ??= viewModel.PendingMemoryLessons[0];
            viewModel.AddLog("Learning candidate prepared in Memory panel.");
        }
    }

    private DesktopToolCallbacks WrapTelemetryCallbacks(
        DesktopToolCallbacks callbacks,
        string workspaceRoot,
        ProviderConfiguration config)
    {
        return new DesktopToolCallbacks
        {
            OnRunStep = (state, title, detail) =>
            {
                SafeCallback("OnRunStep", () => callbacks.OnRunStep?.Invoke(state, title, detail), detail ?? title);
                diagnosticsService.Record(
                    "run_step",
                    $"state={state}; title={title}; detail={detail ?? string.Empty}",
                    workspaceRoot,
                    config.Provider,
                    config.Model);
                if (title.Contains("search retry", StringComparison.OrdinalIgnoreCase))
                {
                    RecordTelemetry("search_retry", workspaceRoot, config.Provider, config.Model, succeeded: true, detail: detail ?? title);
                }
                else if (title.StartsWith("Model route:", StringComparison.OrdinalIgnoreCase))
                {
                    RecordTelemetry("model_route_recommended", workspaceRoot, config.Provider, config.Model, succeeded: true, detail: detail ?? title);
                }
                else if (title.StartsWith("No-tool guard:", StringComparison.OrdinalIgnoreCase))
                {
                    RecordTelemetry(ToNoToolGuardTelemetryEventType(title), workspaceRoot, config.Provider, config.Model, succeeded: true, detail: detail ?? title);
                }
                else if (title.StartsWith("Greeting guard:", StringComparison.OrdinalIgnoreCase))
                {
                    RecordTelemetry("greeting_guard_retry", workspaceRoot, config.Provider, config.Model, succeeded: true, detail: detail ?? title);
                }
            },
            OnToolExecution = toolName =>
            {
                SafeCallback("OnToolExecution", () => callbacks.OnToolExecution?.Invoke(toolName), toolName);
                diagnosticsService.Record(
                    "tool_started",
                    toolName,
                    workspaceRoot,
                    config.Provider,
                    config.Model);
                RecordTelemetry("tool_started", workspaceRoot, config.Provider, config.Model, toolName: toolName, succeeded: true);
            },
            OnToolOutput = (toolName, output) =>
            {
                SafeCallback("OnToolOutput", () => callbacks.OnToolOutput?.Invoke(toolName, output), $"{toolName}; chars={output.Length}");
                diagnosticsService.Record(
                    "tool_completed",
                    $"{toolName}; chars={output.Length}; preview={DesktopPromptBuilder.Truncate(output.ReplaceLineEndings(" "), 500)}",
                    workspaceRoot,
                    config.Provider,
                    config.Model);
                RecordTelemetry("tool_completed", workspaceRoot, config.Provider, config.Model, toolName: toolName, succeeded: true, detail: $"{output.Length} chars");
            },
            OnToolError = (toolName, error) =>
            {
                SafeCallback("OnToolError", () => callbacks.OnToolError?.Invoke(toolName, error), $"{toolName}; {error}");
                diagnosticsService.Record(
                    "tool_failed",
                    $"{toolName}; error={error}",
                    workspaceRoot,
                    config.Provider,
                    config.Model);
                RecordTelemetry("tool_failed", workspaceRoot, config.Provider, config.Model, toolName: toolName, succeeded: false, isError: true, detail: error);
            },
            OnPermissionDenied = toolName =>
            {
                SafeCallback("OnPermissionDenied", () => callbacks.OnPermissionDenied?.Invoke(toolName), toolName);
                diagnosticsService.Record(
                    "permission_denied",
                    toolName,
                    workspaceRoot,
                    config.Provider,
                    config.Model);
                RecordTelemetry("permission_denied", workspaceRoot, config.Provider, config.Model, toolName: toolName, succeeded: false);
            },
            OnFileChanged = change =>
            {
                SafeCallback("OnFileChanged", () => callbacks.OnFileChanged?.Invoke(change), $"{change.RelativePath} {change.Summary}");
                diagnosticsService.Record(
                    "file_changed",
                    $"{change.RelativePath} {change.Summary}",
                    workspaceRoot,
                    config.Provider,
                    config.Model);
                RecordTelemetry("file_changed", workspaceRoot, config.Provider, config.Model, succeeded: true, detail: $"{change.RelativePath} {change.Summary}");
            },
            OnVerificationPlan = plan =>
            {
                SafeCallback("OnVerificationPlan", () => callbacks.OnVerificationPlan?.Invoke(plan), $"{plan.Title}; {plan.Detail}");
                diagnosticsService.Record(
                    "verification_plan",
                    $"{plan.Title}; satisfied={plan.AlreadySatisfied}; {plan.Detail}",
                    workspaceRoot,
                    config.Provider,
                    config.Model);
                RecordTelemetry("verification_plan", workspaceRoot, config.Provider, config.Model, succeeded: plan.AlreadySatisfied, detail: plan.Detail);
            },
            OnVerificationResult = result =>
            {
                SafeCallback("OnVerificationResult", () => callbacks.OnVerificationResult?.Invoke(result), $"{result.Status}; {result.Summary}");
                diagnosticsService.Record(
                    "verification_result",
                    $"{result.Status}; {result.Summary}",
                    workspaceRoot,
                    config.Provider,
                    config.Model);
                RecordTelemetry("verification_result", workspaceRoot, config.Provider, config.Model, succeeded: string.Equals(result.Status, "PASSED", StringComparison.OrdinalIgnoreCase), detail: result.Summary);
            },
            OnUsage = usage => SafeCallback("OnUsage", () => callbacks.OnUsage?.Invoke(usage), usage.ToString() ?? string.Empty),
            OnRequestExtendSteps = currentLimit =>
            {
                try
                {
                    return callbacks.OnRequestExtendSteps?.Invoke(currentLimit) ?? false;
                }
                catch (Exception ex)
                {
                    diagnosticsService.Record(
                        "ui_callback_exception",
                        $"callback=OnRequestExtendSteps; currentLimit={currentLimit}",
                        workspaceRoot,
                        config.Provider,
                        config.Model,
                        ex);
                    return false;
                }
            }
        };

        void SafeCallback(string callbackName, Action action, string detail)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                diagnosticsService.Record(
                    "ui_callback_exception",
                    $"callback={callbackName}; detail={DesktopPromptBuilder.Truncate(detail.ReplaceLineEndings(" "), 500)}",
                    workspaceRoot,
                    config.Provider,
                    config.Model,
                    ex);
            }
        }
    }

    private static string ToNoToolGuardTelemetryEventType(string title)
    {
        var suffix = title["No-tool guard:".Length..].Trim().ToLowerInvariant();
        suffix = suffix.Replace(' ', '_').Replace('-', '_');
        return string.IsNullOrWhiteSpace(suffix)
            ? "no_tool_guard"
            : $"no_tool_guard_{suffix}";
    }

    private void RecordUsageTelemetry(
        string eventType,
        string workspaceRoot,
        ProviderConfiguration config,
        DesktopUsageSnapshot snapshot)
    {
        RecordTelemetry(
            eventType,
            workspaceRoot,
            config.Provider,
            config.Model,
            succeeded: true,
            inputTokens: snapshot.LastInputTokens,
            outputTokens: snapshot.LastOutputTokens,
            isEstimate: snapshot.IsEstimate,
            detail: $"total={snapshot.TotalTokens}; requests={snapshot.RequestCount}");
    }

    private void RecordTelemetry(
        string eventType,
        string workspaceRoot,
        string provider,
        string model,
        bool succeeded,
        string toolName = "",
        bool isError = false,
        int inputTokens = 0,
        int outputTokens = 0,
        bool isEstimate = false,
        int durationMs = 0,
        string detail = "")
    {
        _ = telemetryService.RecordAsync(new DesktopTelemetryEvent
        {
            EventType = eventType,
            WorkspaceRoot = workspaceRoot,
            Provider = provider,
            Model = model,
            ToolName = toolName,
            Succeeded = succeeded,
            IsError = isError,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            IsEstimate = isEstimate,
            DurationMs = durationMs,
            Detail = detail
        });
    }

    private static void AppendAssistantDelta(
        MainViewModel viewModel,
        ChatMessageViewModel assistantMessage,
        int assistantIndex,
        string delta)
    {
        if (string.IsNullOrEmpty(delta) ||
            assistantIndex < 0 ||
            assistantIndex >= viewModel.Messages.Count)
        {
            return;
        }

        var currentContent = viewModel.Messages[assistantIndex].Content;
        var nextContent = currentContent == ThinkingPlaceholder
            ? delta
            : currentContent + delta;
        nextContent = ModelReasoningTagFilter.Strip(nextContent);

        viewModel.Messages[assistantIndex] = new ChatMessageViewModel
        {
            Role = assistantMessage.Role,
            Content = nextContent,
            CreatedAt = assistantMessage.CreatedAt
        };
    }

    private static CancellationTokenSource CreateTimeout(int timeoutSeconds)
    {
        return timeoutSeconds <= 0
            ? new CancellationTokenSource()
            : new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
    }
}
