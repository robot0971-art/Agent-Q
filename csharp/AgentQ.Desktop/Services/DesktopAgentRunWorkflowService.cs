using System.Windows;
using System.Windows.Threading;
using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

public sealed class DesktopAgentRunWorkflowService(
    DesktopAgentService agentService,
    DesktopWorkspaceContextWorkflowService workspaceContextWorkflowService,
    DesktopVerificationPanelWorkflowService verificationPanelWorkflowService)
{
    private const string ThinkingPlaceholder = "생각중...";
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
            var fullText = await agentService.SendAsync(
                viewModel.ToConfiguration(),
                prompt,
                attachmentsForRequest,
                viewModel.WorkspaceRoot,
                viewModel.WorkMode,
                delta =>
                {
                    dispatcher.Invoke(() =>
                    {
                        if (assistantIndex >= 0 && assistantIndex < viewModel.Messages.Count)
                        {
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
                    });
                },
                new DesktopPermissionEnforcer(owner, viewModel.WorkMode),
                DesktopToolCallbacksFactory.Create(
                    viewModel,
                    dispatcher,
                    trimForLog,
                    usage =>
                    {
                        var snapshot = _usageTracker.RecordActual(usage);
                        viewModel.UsageText = snapshot.DisplayText;
                        viewModel.AddLog($"Usage recorded: {snapshot.DisplayText}");
                    }),
                operationCts.Token);

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
        viewModel.AddLog("Conversation cleared");
        viewModel.StatusText = "Conversation cleared";
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

    private static CancellationTokenSource CreateTimeout(int timeoutSeconds)
    {
        return timeoutSeconds <= 0
            ? new CancellationTokenSource()
            : new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
    }
}
