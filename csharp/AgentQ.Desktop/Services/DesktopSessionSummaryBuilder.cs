using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

public static class DesktopSessionSummaryBuilder
{
    public static AgentSessionSummary Build(
        string workspaceRoot,
        string statusText,
        IEnumerable<AgentRunStep> runSteps,
        IEnumerable<FileChangeRecord> fileChanges,
        IEnumerable<VerificationResultCard> verificationResults,
        IEnumerable<AgentPlanItem> planItems,
        IEnumerable<ChatMessageViewModel> messages)
    {
        var completed = runSteps
            .TakeLast(12)
            .Where(step => step.State is AgentRunState.Done or AgentRunState.RecordingChanges)
            .Select(step => string.IsNullOrWhiteSpace(step.Detail) ? step.Title : $"{step.Title}: {step.Detail}")
            .Distinct()
            .Take(8)
            .ToList();

        var changedFiles = fileChanges
            .Select(change => change.RelativePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        var verification = verificationResults
            .Take(6)
            .Select(result => $"{result.Status}: {result.Title} ({result.Command})")
            .ToList();

        var openPlanItems = planItems
            .Where(item => item.Status != AgentPlanItemStatus.Done)
            .OrderBy(item => item.Order)
            .Select(item => $"{item.Order}. {item.Title} [{item.StatusText}]")
            .Take(8)
            .ToList();

        var lastAssistantMessage = messages.LastOrDefault(message =>
            string.Equals(message.Role, "AgentQ", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(message.Content));

        var latestClarifyingStep = runSteps
            .LastOrDefault(step => step.State == AgentRunState.Clarifying);
        var pendingQuestion = latestClarifyingStep == null
            ? string.Empty
            : BuildStepText(latestClarifyingStep);

        var title = !string.IsNullOrWhiteSpace(pendingQuestion)
            ? $"Waiting for answer: {pendingQuestion}"
            : openPlanItems.Count > 0
            ? $"Continue: {openPlanItems[0]}"
            : statusText;

        var narrative = BuildNarrative(lastAssistantMessage, changedFiles, verification);

        var nextSteps = !string.IsNullOrWhiteSpace(pendingQuestion)
            ? [$"Answer AgentQ's pending question: {pendingQuestion}"]
            : openPlanItems.Count > 0
            ? openPlanItems.Take(3).Select(item => $"Continue {item}").ToList()
            : ["Review the latest workspace state and choose the next concrete task."];

        return new AgentSessionSummary
        {
            WorkspaceRoot = workspaceRoot,
            Title = DesktopPromptBuilder.Truncate(title, 120),
            Narrative = narrative,
            CompletedWork = completed,
            ChangedFiles = changedFiles,
            VerificationResults = verification,
            OpenPlanItems = openPlanItems,
            NextSteps = nextSteps
        };
    }

    private static string BuildStepText(AgentRunStep step)
    {
        var text = string.IsNullOrWhiteSpace(step.Detail)
            ? step.Title
            : $"{step.Title}: {step.Detail}";
        return DesktopPromptBuilder.Truncate(text, 160);
    }

    private static string BuildNarrative(
        ChatMessageViewModel? lastAssistantMessage,
        IReadOnlyList<string> changedFiles,
        IReadOnlyList<string> verification)
    {
        if (changedFiles.Count > 0)
        {
            var files = string.Join(", ", changedFiles.Take(6));
            var verifyText = verification.Count == 0
                ? "No verification result was captured in the session summary."
                : $"Verification captured: {string.Join("; ", verification.Take(3))}.";
            return DesktopPromptBuilder.Truncate(
                $"AgentQ changed workspace files: {files}. {verifyText}",
                600);
        }

        if (lastAssistantMessage == null)
        {
            return "No assistant response has been captured yet.";
        }

        if (LooksLikeIrrelevantAssistantText(lastAssistantMessage.Content))
        {
            return "Last assistant response was omitted because it appeared unrelated to the workspace task; inspect the current workspace and latest user request before resuming.";
        }

        return DesktopPromptBuilder.Truncate(lastAssistantMessage.Content.ReplaceLineEndings(" "), 600);
    }

    private static bool LooksLikeIrrelevantAssistantText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var mentionsReadingAndGames =
            value.Contains("독서", StringComparison.OrdinalIgnoreCase) &&
            value.Contains("게임", StringComparison.OrdinalIgnoreCase);

        if (mentionsReadingAndGames)
        {
            return true;
        }

        return value.Contains("무엇을 도와", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("what can I help", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("reading", StringComparison.OrdinalIgnoreCase) && value.Contains("games", StringComparison.OrdinalIgnoreCase);
    }
}

