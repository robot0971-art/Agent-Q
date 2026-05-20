using System.Text;
using System.Windows;
using System.Windows.Threading;
using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

public sealed class DesktopAgentRunWorkflowService(
    DesktopAgentService agentService,
    DesktopWorkspaceContextWorkflowService workspaceContextWorkflowService,
    DesktopVerificationPanelWorkflowService verificationPanelWorkflowService,
    DesktopLearningSuggestionService learningSuggestionService)
{
    private const string ThinkingPlaceholder = "생각중...";
    private const string ContinuationPrompt =
        "Continue the previous run from where it stopped. Do not repeat completed work. Inspect current files or command results if needed, then continue until the task is complete or you need user input.";
    private readonly DesktopUsageTracker _usageTracker = new();
    private CancellationTokenSource? _activeOperationCts;

    public void Stop(MainViewModel viewModel)
    {
        if (!viewModel.IsBusy)
        {
            viewModel.StatusText = "AgentQ is idle";
            return;
        }

        _activeOperationCts?.Cancel();
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

    public async Task SendCurrentMessageAsync(
        MainViewModel viewModel,
        List<DesktopAttachment> attachments,
        Window owner,
        Dispatcher dispatcher,
        Func<string, string> trimForLog,
        bool preserveLastVerificationFailure = false)
    {
        var prompt = viewModel.InputText.Trim();
        if (string.IsNullOrWhiteSpace(prompt) || viewModel.IsBusy)
        {
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

        CancellationTokenSource? operationCts = null;

        try
        {
            operationCts = CreateTimeout(viewModel.TimeoutSeconds);
            SetActiveOperation(operationCts);
            var config = viewModel.ToConfiguration();
            var workspaceRoot = viewModel.WorkspaceRoot;
            var workMode = viewModel.WorkMode;
            var permissionEnforcer = new DesktopPermissionEnforcer(owner, workMode);
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
                });
            var fullText = await Task.Run(async () =>
                await agentService.SendAsync(
                    config,
                    prompt,
                    attachmentsForRequest,
                    workspaceRoot,
                    workMode,
                    QueueAssistantDelta,
                    permissionEnforcer,
                    toolCallbacks,
                    operationCts.Token),
                operationCts.Token);
            FlushAssistantDelta();

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
            }
            ClearPendingAttachmentsAfterSuccessfulSend(viewModel, attachments);
            await workspaceContextWorkflowService.SaveSessionSummaryAsync(
                viewModel,
                "Session summary auto-saved",
                trimForLog);
        }
        catch (OperationCanceledException)
        {
            RemoveThinkingPlaceholder(viewModel);
            viewModel.AddRunStep(AgentRunState.Cancelled, "Run cancelled", "The request was cancelled or timed out.");
            AddAttachmentRetryLog(viewModel, attachmentsForRequest.Count);
            viewModel.StatusText = "Request cancelled or timed out.";
            viewModel.AddLog("Request cancelled or timed out");
        }
        catch (Exception ex)
        {
            viewModel.AddRunStep(AgentRunState.Failed, "Run failed", ex.Message);
            AddAttachmentRetryLog(viewModel, attachmentsForRequest.Count);
            viewModel.StatusText = $"Error: {ex.Message}";
            viewModel.AddLog($"Error: {ex.Message}");
        }
        finally
        {
            if (operationCts != null)
            {
                ClearActiveOperation(operationCts);
                operationCts.Dispose();
            }

            viewModel.IsBusy = false;
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
        viewModel.Messages[assistantIndex] = new ChatMessageViewModel
        {
            Role = assistantMessage.Role,
            Content = currentContent == ThinkingPlaceholder
                ? delta
                : currentContent + delta,
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
