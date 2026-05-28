using AgentQ.Tools;

namespace AgentQ.Desktop.Services;

public static class DesktopLocalizer
{
    public static bool IsKoreanUi(string uiLanguage) =>
        uiLanguage.Equals("\uD55C\uAD6D\uC5B4", StringComparison.OrdinalIgnoreCase) ||
        uiLanguage.Equals("Korean", StringComparison.OrdinalIgnoreCase);

    public static string UiText(string key, bool useKoreanUi)
    {
        return key switch
        {
            nameof(DesktopText.MenuFile) => useKoreanUi ? "\uD30C\uC77C" : "File",
            nameof(DesktopText.MenuSelectProjectFolder) => useKoreanUi ? "\uD504\uB85C\uC81D\uD2B8 \uD3F4\uB354 \uC120\uD0DD" : "Select project folder",
            nameof(DesktopText.MenuAddAttachment) => useKoreanUi ? "\uCCA8\uBD80 \uCD94\uAC00" : "Add attachment",
            nameof(DesktopText.MenuClearAttachments) => useKoreanUi ? "\uCCA8\uBD80 \uC9C0\uC6B0\uAE30" : "Clear attachments",
            nameof(DesktopText.MenuExit) => useKoreanUi ? "\uC885\uB8CC" : "Exit",
            nameof(DesktopText.MenuEdit) => useKoreanUi ? "\uD3B8\uC9D1" : "Edit",
            nameof(DesktopText.MenuCopyLastAnswer) => useKoreanUi ? "\uB9C8\uC9C0\uB9C9 \uB2F5\uBCC0 \uBCF5\uC0AC" : "Copy last answer",
            nameof(DesktopText.MenuCopyConversation) => useKoreanUi ? "\uC804\uCCB4 \uB300\uD654 \uBCF5\uC0AC" : "Copy conversation",
            nameof(DesktopText.MenuClearConversation) => useKoreanUi ? "\uB300\uD654 \uCD08\uAE30\uD654" : "Clear conversation",
            nameof(DesktopText.MenuSettings) => useKoreanUi ? "\uC124\uC815" : "Settings",
            nameof(DesktopText.MenuSaveSettings) => useKoreanUi ? "\uC124\uC815 \uC800\uC7A5" : "Save settings",
            nameof(DesktopText.MenuView) => useKoreanUi ? "\uBCF4\uAE30" : "View",
            nameof(DesktopText.MenuIncreaseFont) => useKoreanUi ? "\uAE00\uC790 \uD06C\uAC8C" : "Increase font",
            nameof(DesktopText.MenuDecreaseFont) => useKoreanUi ? "\uAE00\uC790 \uC791\uAC8C" : "Decrease font",
            nameof(DesktopText.MenuResetFont) => useKoreanUi ? "\uAE30\uBCF8 \uAE00\uC790 \uD06C\uAE30" : "Reset font size",
            nameof(DesktopText.MenuHelp) => useKoreanUi ? "\uB3C4\uC6C0\uB9D0" : "Help",
            nameof(DesktopText.MenuShowStatus) => useKoreanUi ? "\uC0C1\uD0DC \uBCF4\uAE30" : "Show status",
            nameof(DesktopText.SettingsHeader) => useKoreanUi ? "\uC124\uC815" : "Settings",
            nameof(DesktopText.Save) => useKoreanUi ? "\uC800\uC7A5" : "Save",
            nameof(DesktopText.UiLanguage) => useKoreanUi ? "UI \uC5B8\uC5B4" : "UI Language",
            nameof(DesktopText.ProjectContextAutoAttach) => useKoreanUi ? "\uD504\uB85C\uC81D\uD2B8 \uCEE8\uD14D\uC2A4\uD2B8 \uC790\uB3D9 \uCCA8\uBD80" : "Auto attach project context",
            nameof(DesktopText.AutoFetchLinks) => useKoreanUi ? "\uB9C1\uD06C \uC790\uB3D9 \uC77D\uAE30" : "Auto fetch links",
            nameof(DesktopText.ProjectHeader) => useKoreanUi ? "\uD504\uB85C\uC81D\uD2B8" : "Project",
            nameof(DesktopText.ProjectFolder) => useKoreanUi ? "\uD504\uB85C\uC81D\uD2B8 \uD3F4\uB354" : "Project folder",
            nameof(DesktopText.BrowseFolder) => useKoreanUi ? "\uD3F4\uB354 \uC120\uD0DD" : "Browse",
            nameof(DesktopText.OpenFolder) => useKoreanUi ? "\uD3F4\uB354 \uC5F4\uAE30" : "Open",
            nameof(DesktopText.BuildEmbeddingIndex) => useKoreanUi ? "\uC784\uBCA0\uB529 \uC778\uB371\uC2A4 \uC0DD\uC131" : "Build embedding index",
            nameof(DesktopText.ChatHeader) => useKoreanUi ? "\uC0C8 \uB300\uD654" : "New chat",
            nameof(DesktopText.AttachFiles) => useKoreanUi ? "\uCCA8\uBD80" : "Attach",
            nameof(DesktopText.CodeBlock) => useKoreanUi ? "\uCF54\uB4DC \uBE14\uB85D" : "Code block",
            nameof(DesktopText.AddProjectFile) => useKoreanUi ? "\uD504\uB85C\uC81D\uD2B8 \uD30C\uC77C \uCD94\uAC00" : "Add project file",
            nameof(DesktopText.ClearAttachments) => useKoreanUi ? "\uCCA8\uBD80 \uC9C0\uC6B0\uAE30" : "Clear",
            nameof(DesktopText.Send) => useKoreanUi ? "\uC804\uC1A1\nCtrl+Enter" : "Send\nCtrl+Enter",
            nameof(DesktopText.Copy) => useKoreanUi ? "\uBCF5\uC0AC" : "Copy",
            nameof(DesktopText.CopyWholeMessage) => useKoreanUi ? "\uBA54\uC2DC\uC9C0 \uC804\uCCB4 \uBCF5\uC0AC" : "Copy whole message",
            nameof(DesktopText.ToolsHeader) => useKoreanUi ? "\uB3C4\uAD6C" : "Tools",
            nameof(DesktopText.Manage) => useKoreanUi ? "\uAD00\uB9AC" : "Manage",
            nameof(DesktopText.ReadFileTool) => useKoreanUi ? "read_file - \uD30C\uC77C \uB0B4\uC6A9\uC744 \uC77D\uC2B5\uB2C8\uB2E4" : "read_file - Read file contents",
            nameof(DesktopText.WriteFileTool) => useKoreanUi ? "write_file - \uD30C\uC77C\uC744 \uC218\uC815\uD569\uB2C8\uB2E4" : "write_file - Edit files",
            nameof(DesktopText.ShellExecuteTool) => useKoreanUi ? "shell_execute - \uBA85\uB839\uC744 \uC2E4\uD589\uD569\uB2C8\uB2E4" : "shell_execute - Run commands",
            nameof(DesktopText.SearchFilesTool) => useKoreanUi ? "search_files - \uD30C\uC77C\uC744 \uAC80\uC0C9\uD569\uB2C8\uB2E4" : "search_files - Search files",
            nameof(DesktopText.ListDirectoryTool) => useKoreanUi ? "list_directory - \uBAA9\uB85D\uC744 \uBD05\uB2C8\uB2E4" : "list_directory - List directories",
            nameof(DesktopText.StatusPanel) => useKoreanUi ? "\uC0C1\uD0DC \uD328\uB110" : "Status panel",
            nameof(DesktopText.Clear) => useKoreanUi ? "\uBE44\uC6B0\uAE30" : "Clear",
            nameof(DesktopText.RunLog) => useKoreanUi ? "\uC791\uC5C5 \uB85C\uADF8" : "Run log",
            nameof(DesktopText.ChangePreview) => useKoreanUi ? "\uBCC0\uACBD \uBBF8\uB9AC\uBCF4\uAE30" : "Change preview",
            nameof(DesktopText.All) => useKoreanUi ? "\uC804\uCCB4" : "ALL",
            nameof(DesktopText.EvidenceTrail) => useKoreanUi ? "\uADFC\uAC70 \uD750\uB984" : "Evidence",
            nameof(DesktopText.EvalDashboard) => useKoreanUi ? "\uD3C9\uAC00" : "Eval",
            nameof(DesktopText.EvalDashboardRefresh) => useKoreanUi ? "\uC0C8\uB85C\uACE0\uCE68" : "Refresh",
            nameof(DesktopText.EvalDashboardHelp) => useKoreanUi ? "\uCD5C\uC2E0 replay, telemetry, \uAC80\uC99D \uACB0\uACFC, \uBC18\uBCF5 \uC2E4\uD328 fingerprint\uB97C \uC694\uC57D\uD569\uB2C8\uB2E4." : "Summarizes latest replay, telemetry, verification results, and recurring failure fingerprints.",
            nameof(DesktopText.EvidenceTrailHelp) => useKoreanUi ? "\uC228\uC740 \uC0AC\uACE0 \uACFC\uC815\uC774 \uC544\uB2CC, \uC0AC\uC6A9\uD55C \uBA54\uBAA8\uB9AC, \uD30C\uC77C, \uAC80\uC0C9, \uBA85\uB839, \uAC80\uC99D \uD750\uB984\uC744 \uBCF4\uC5EC\uC90D\uB2C8\uB2E4." : "Shows used memory, files, searches, commands, changes, and verification flow instead of hidden model reasoning.",
            nameof(DesktopText.SaveSummary) => useKoreanUi ? "\uC694\uC57D \uC800\uC7A5" : "Save summary",
            nameof(DesktopText.Load) => useKoreanUi ? "\uBD88\uB7EC\uC624\uAE30" : "Load",
            nameof(DesktopText.Resume) => useKoreanUi ? "\uC774\uC5B4\uC11C" : "Resume",
            nameof(DesktopText.LearningCandidates) => useKoreanUi ? "\uD559\uC2B5 \uD6C4\uBCF4" : "Learning candidates",
            nameof(DesktopText.LearningCandidatesHelp) => useKoreanUi ? "\uC791\uC5C5 \uD6C4 AgentQ\uAC00 \uB2E4\uC74C\uC5D0 \uAE30\uC5B5\uD558\uBA74 \uC88B\uC744 \uADDC\uCE59\uC744 \uC81C\uC548\uD569\uB2C8\uB2E4. \uC2B9\uC778\uD55C \uD56D\uBAA9\uB9CC \uC774 \uD504\uB85C\uC81D\uD2B8\uC758 \uB85C\uCEEC \uBA54\uBAA8\uB9AC\uC5D0 \uC800\uC7A5\uB429\uB2C8\uB2E4." : "After a run, AgentQ may suggest rules worth remembering. Only approved items are saved to this project's local memory.",
            nameof(DesktopText.SaveLesson) => useKoreanUi ? "\uD559\uC2B5 \uC800\uC7A5" : "Save lesson",
            nameof(DesktopText.Dismiss) => useKoreanUi ? "\uBB34\uC2DC" : "Dismiss",
            nameof(DesktopText.SavedMemory) => useKoreanUi ? "\uC800\uC7A5\uB41C \uBA54\uBAA8\uB9AC" : "Saved memory",
            nameof(DesktopText.Refresh) => useKoreanUi ? "\uC0C8\uB85C\uACE0\uCE68" : "Refresh",
            nameof(DesktopText.Disable) => useKoreanUi ? "\uBE44\uD65C\uC131" : "Disable",
            nameof(DesktopText.Delete) => useKoreanUi ? "\uC0AD\uC81C" : "Delete",
            nameof(DesktopText.SessionSummary) => useKoreanUi ? "\uC138\uC158 \uC694\uC57D" : "Session summary",
            nameof(DesktopText.GitStatusEmpty) => useKoreanUi ? "\uC0C1\uD0DC\uB97C \uB20C\uB7EC \uD604\uC7AC \uBE0C\uB79C\uCE58\uC640 \uBCC0\uACBD \uD30C\uC77C\uC744 \uD655\uC778\uD558\uC138\uC694." : "Click Status to inspect the current branch and changed files.",
            nameof(DesktopText.GitDiffEmpty) => useKoreanUi ? "\uD604\uC7AC \uC6CC\uD06C\uC2A4\uD398\uC774\uC2A4 diff\uB97C \uBD88\uB7EC\uC624\uB824\uBA74 Diff\uB97C \uB204\uB974\uC138\uC694." : "Click Diff to load the current workspace diff.",
            nameof(DesktopText.GitSelectedFileEmpty) => useKoreanUi ? "\uBCC0\uACBD \uD30C\uC77C\uC744 \uC120\uD0DD\uD558\uBA74 diff\uAC00 \uD45C\uC2DC\uB429\uB2C8\uB2E4." : "Select a changed file to view its diff.",
            nameof(DesktopText.GitWaitingForRefresh) => useKoreanUi ? "Git \uD328\uB110\uC774 \uC0C8\uB85C\uACE0\uCE68\uC744 \uAE30\uB2E4\uB9AC\uB294 \uC911\uC785\uB2C8\uB2E4." : "Git panel is waiting for refresh.",
            nameof(DesktopText.ProjectNotAnalyzed) => useKoreanUi ? "\uC544\uC9C1 \uC6CC\uD06C\uC2A4\uD398\uC774\uC2A4\uB97C \uBD84\uC11D\uD558\uC9C0 \uC54A\uC558\uC2B5\uB2C8\uB2E4." : "Workspace not analyzed yet.",
            nameof(DesktopText.ProjectAnalyzeToDetect) => useKoreanUi ? "\uBD84\uC11D\uD558\uBA74 \uAC10\uC9C0\uB429\uB2C8\uB2E4" : "Analyze to detect",
            nameof(DesktopText.ProjectAnalyzeStats) => useKoreanUi ? "\uBD84\uC11D\uD558\uBA74 \uD30C\uC77C\uACFC \uD3F4\uB354 \uC218\uB97C \uACC4\uC0B0\uD569\uB2C8\uB2E4." : "Analyze to count files and folders.",
            nameof(DesktopText.ProjectAnalysisUpdatedEmpty) => useKoreanUi ? "\uC544\uC9C1 \uBD84\uC11D\uB418\uC9C0 \uC54A\uC74C." : "Not analyzed yet.",
            nameof(DesktopText.ProjectDashboardEmpty) => useKoreanUi ? "\uD504\uB85C\uC81D\uD2B8 \uB300\uC2DC\uBCF4\uB4DC\uB97C \uAD6C\uC131\uD558\uB824\uBA74 \uC6CC\uD06C\uC2A4\uD398\uC774\uC2A4\uB97C \uBD84\uC11D\uD558\uC138\uC694." : "Analyze the workspace to build the project dashboard.",
            nameof(DesktopText.ProjectWaitingForAnalysis) => useKoreanUi ? "\uBD84\uC11D \uB300\uAE30 \uC911" : "Waiting for analysis",
            nameof(DesktopText.ProjectMapEmpty) => useKoreanUi ? "\uC544\uC9C1 \uD504\uB85C\uC81D\uD2B8 \uB9F5\uC774 \uC5C6\uC2B5\uB2C8\uB2E4." : "No project map yet.",
            nameof(DesktopText.ProjectSymbolsEmpty) => useKoreanUi ? "\uC544\uC9C1 \uD575\uC2EC \uC2EC\uBCFC\uC744 \uCC3E\uC9C0 \uBABB\uD588\uC2B5\uB2C8\uB2E4." : "No key symbols found yet.",
            nameof(DesktopText.ProjectDependenciesEmpty) => useKoreanUi ? "\uC544\uC9C1 \uC758\uC874\uC131 \uADF8\uB798\uD504 \uC2E0\uD638\uAC00 \uC5C6\uC2B5\uB2C8\uB2E4." : "No dependency graph signals yet.",
            nameof(DesktopText.ProjectFilesEmpty) => useKoreanUi ? "\uC544\uC9C1 \uD575\uC2EC \uD30C\uC77C\uC744 \uAC10\uC9C0\uD558\uC9C0 \uBABB\uD588\uC2B5\uB2C8\uB2E4." : "No key files detected yet.",
            nameof(DesktopText.ProjectVerificationEmpty) => useKoreanUi ? "\uC544\uC9C1 \uAC80\uC99D \uBA85\uB839\uC744 \uAC10\uC9C0\uD558\uC9C0 \uBABB\uD588\uC2B5\uB2C8\uB2E4." : "No verification command detected yet.",
            nameof(DesktopText.EvalDashboardEmpty) => useKoreanUi ? "\uC0C8\uB85C\uACE0\uCE68\uC744 \uB20C\uB7EC replay, telemetry, \uAC80\uC99D, \uBC18\uBCF5 \uC2E4\uD328 \uC2E0\uD638\uB97C \uBD88\uB7EC\uC624\uC138\uC694." : "Click Refresh to load replay, telemetry, verification, and recurring failure signals.",
            nameof(DesktopText.EvalWaitingForRefresh) => useKoreanUi ? "\uCCAB \uC0C8\uB85C\uACE0\uCE68\uC744 \uAE30\uB2E4\uB9AC\uB294 \uC911\uC785\uB2C8\uB2E4." : "Waiting for first refresh.",
            _ => key
        };
    }

    public static string TimelineLabel(AgentRunState state, bool useKoreanUi)
    {
        if (useKoreanUi)
        {
            return state switch
            {
                AgentRunState.Planning => "\uACC4\uD68D",
                AgentRunState.GatheringContext => "\uCEE8\uD14D\uC2A4\uD2B8",
                AgentRunState.Generating => "\uBAA8\uB378",
                AgentRunState.RunningTool => "\uB3C4\uAD6C",
                AgentRunState.WaitingForApproval => "\uC2B9\uC778",
                AgentRunState.RecordingChanges => "\uBCC0\uACBD",
                AgentRunState.Verifying => "\uAC80\uC99D",
                AgentRunState.Done => "\uC644\uB8CC",
                AgentRunState.Failed => "\uC2E4\uD328",
                AgentRunState.Cancelled => "\uCDE8\uC18C",
                _ => "\uC2E4\uD589"
            };
        }

        return state switch
        {
            AgentRunState.Planning => "PLAN",
            AgentRunState.GatheringContext => "CONTEXT",
            AgentRunState.Generating => "MODEL",
            AgentRunState.RunningTool => "TOOL",
            AgentRunState.WaitingForApproval => "APPROVAL",
            AgentRunState.RecordingChanges => "CHANGE",
            AgentRunState.Verifying => "VERIFY",
            AgentRunState.Done => "DONE",
            AgentRunState.Failed => "FAILED",
            AgentRunState.Cancelled => "CANCELLED",
            _ => "RUN"
        };
    }

    public static string RunState(AgentRunState state, bool useKoreanUi)
    {
        if (!useKoreanUi)
        {
            return state.ToString();
        }

        return state switch
        {
            AgentRunState.Planning => "\uACC4\uD68D",
            AgentRunState.GatheringContext => "\uCEE8\uD14D\uC2A4\uD2B8 \uC218\uC9D1",
            AgentRunState.Generating => "\uC751\uB2F5 \uC0DD\uC131",
            AgentRunState.RunningTool => "\uB3C4\uAD6C \uC2E4\uD589",
            AgentRunState.WaitingForApproval => "\uC2B9\uC778 \uB300\uAE30",
            AgentRunState.RecordingChanges => "\uBCC0\uACBD \uAE30\uB85D",
            AgentRunState.Verifying => "\uAC80\uC99D",
            AgentRunState.Done => "\uC644\uB8CC",
            AgentRunState.Failed => "\uC2E4\uD328",
            AgentRunState.Cancelled => "\uCDE8\uC18C",
            _ => state.ToString()
        };
    }

    public static string TimelineTitle(string title, bool useKoreanUi)
    {
        if (!useKoreanUi || string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        if (title.StartsWith("Permission: ", StringComparison.OrdinalIgnoreCase))
        {
            return title.Replace("Permission:", "\uAD8C\uD55C:", StringComparison.OrdinalIgnoreCase)
                .Replace("Allowed by run approval", "\uC2E4\uD589 \uAD8C\uD55C\uC73C\uB85C \uD5C8\uC6A9", StringComparison.OrdinalIgnoreCase)
                .Replace("Allowed by policy", "\uC815\uCC45\uC0C1 \uD5C8\uC6A9", StringComparison.OrdinalIgnoreCase)
                .Replace("Approved", "\uC2B9\uC778\uB428", StringComparison.OrdinalIgnoreCase)
                .Replace("Denied", "\uAC70\uBD80\uB428", StringComparison.OrdinalIgnoreCase)
                .Replace("Blocked", "\uCC28\uB2E8\uB428", StringComparison.OrdinalIgnoreCase);
        }

        if (title.StartsWith("Blocked:", StringComparison.OrdinalIgnoreCase))
        {
            return title.Replace("Blocked:", "\uCC28\uB2E8\uB428:", StringComparison.OrdinalIgnoreCase);
        }

        if (title.StartsWith("Evidence:", StringComparison.OrdinalIgnoreCase))
        {
            return title.Replace("Evidence:", "\uADFC\uAC70:", StringComparison.OrdinalIgnoreCase);
        }

        return title switch
        {
            "Waiting for approval" => "\uC2B9\uC778 \uB300\uAE30",
            "Running verification" => "\uAC80\uC99D \uC2E4\uD589 \uC911",
            "Verification passed" => "\uAC80\uC99D \uD1B5\uACFC",
            "Verification cancelled" => "\uAC80\uC99D \uCDE8\uC18C\uB428",
            "Run complete" => "\uC2E4\uD589 \uC644\uB8CC",
            "Run started" => "\uC2E4\uD589 \uC2DC\uC791",
            _ => title
        };
    }

    public static string NoTimelineDetail(bool useKoreanUi) =>
        useKoreanUi ? "\uCD94\uAC00 \uC138\uBD80 \uC815\uBCF4 \uC5C6\uC74C." : "No additional detail.";

    public static string RunSummaryPhase(AgentRunState state, string statusText, bool isBusy, bool useKoreanUi)
    {
        if (!useKoreanUi)
        {
            return EnglishRunSummaryPhase(state, statusText, isBusy);
        }

        if (isBusy)
        {
            return state switch
            {
                AgentRunState.GatheringContext => "\uCEE8\uD14D\uC2A4\uD2B8 \uC218\uC9D1",
                AgentRunState.Generating => "\uC751\uB2F5 \uC0DD\uC131",
                AgentRunState.RunningTool => "\uB3C4\uAD6C \uC2E4\uD589",
                AgentRunState.WaitingForApproval => "\uC2B9\uC778 \uB300\uAE30",
                AgentRunState.RecordingChanges => "\uBCC0\uACBD \uAE30\uB85D",
                AgentRunState.Verifying => "\uAC80\uC99D \uC911",
                AgentRunState.Planning => "\uACC4\uD68D \uC911",
                _ => "\uC2E4\uD589 \uC911"
            };
        }

        if (IsProblemStatus(statusText))
        {
            return "\uD655\uC778 \uD544\uC694";
        }

        return state switch
        {
            AgentRunState.Done => "\uC644\uB8CC",
            AgentRunState.Failed => "\uC2E4\uD328",
            AgentRunState.Cancelled => "\uCDE8\uC18C\uB428",
            AgentRunState.Idle => "\uB300\uAE30",
            _ => state.ToString()
        };
    }

    public static string NoEvidence(bool useKoreanUi) =>
        useKoreanUi
            ? "AgentQ\uAC00 \uD30C\uC77C \uC77D\uAE30, \uAC80\uC0C9, \uB3C4\uAD6C \uC2E4\uD589, \uAC80\uC99D\uC744 \uC218\uD589\uD558\uBA74 \uADFC\uAC70\uAC00 \uC5EC\uAE30\uC5D0 \uD45C\uC2DC\uB429\uB2C8\uB2E4."
            : "Evidence will appear after AgentQ reads files, searches, runs tools, or verifies changes.";

    public static string NotVerified(bool useKoreanUi) => useKoreanUi ? "\uAC80\uC99D \uC548 \uB428" : "Not verified";

    public static string ChangedFiles(int count, bool useKoreanUi) => useKoreanUi ? $"\uBCC0\uACBD {count}\uAC1C" : $"{count} changed";

    public static string NoTiming(bool useKoreanUi) => useKoreanUi ? "\uC544\uC9C1 \uC2DC\uAC04 \uC815\uBCF4 \uC5C6\uC74C" : "No timing yet";

    public static string Timing(string duration, int steps, bool useKoreanUi) =>
        useKoreanUi ? $"{duration} \uACBD\uACFC / {steps:0}\uB2E8\uACC4" : $"{duration} elapsed / {steps:0} step(s)";

    public static string CommitReadinessNoChanges(bool useKoreanUi) => useKoreanUi ? "\uBCC0\uACBD \uC5C6\uC74C" : "No changes";

    public static string CommitReadinessNeedsEdit(bool useKoreanUi) => useKoreanUi ? "\uCEE4\uBC0B \uC804 \uC218\uC815 \uD544\uC694" : "Needs edit before commit";

    public static string CommitReadinessReviewChanges(bool useKoreanUi) => useKoreanUi ? "\uBCC0\uACBD \uAC80\uD1A0 \uD544\uC694" : "Review changes";

    public static string CommitReadinessReady(bool useKoreanUi) => useKoreanUi ? "\uCEE4\uBC0B \uC900\uBE44\uB428" : "Ready to commit";

    public static string CommitReadinessVerify(bool useKoreanUi) => useKoreanUi ? "\uCEE4\uBC0B \uC804 \uAC80\uC99D \uD544\uC694" : "Verify before commit";

    public static string NextActionReviewTool(bool useKoreanUi) => useKoreanUi ? "\uC694\uCCAD\uB41C \uB3C4\uAD6C \uC791\uC5C5\uC744 \uAC80\uD1A0\uD558\uC138\uC694." : "Review the requested tool action.";

    public static string NextActionWait(bool useKoreanUi) => useKoreanUi ? "\uD604\uC7AC \uC2E4\uD589\uC774 \uB05D\uB0A0 \uB54C\uAE4C\uC9C0 \uAE30\uB2E4\uB9AC\uC138\uC694." : "Wait for the current run to finish.";

    public static string NextActionInspectFailure(bool useKoreanUi) => useKoreanUi ? "\uADFC\uAC70 \uB610\uB294 \uAC80\uC99D \uD328\uB110\uC5D0\uC11C \uC2E4\uD328\uB97C \uD655\uC778\uD558\uC138\uC694." : "Open Evidence or Verify to inspect the failure.";

    public static string NextActionFixNeedsEdit(bool useKoreanUi) => useKoreanUi ? "\uC218\uC815 \uD544\uC694\uB85C \uD45C\uC2DC\uB41C \uBCC0\uACBD\uC744 \uACE0\uCE58\uC138\uC694." : "Fix changes marked as needing edits.";

    public static string NextActionReviewChanges(bool useKoreanUi) => useKoreanUi ? "\uBCC0\uACBD \uD30C\uC77C\uC744 \uAC80\uD1A0\uD55C \uB4A4 \uAC80\uC99D\uC744 \uC2E4\uD589\uD558\uC138\uC694." : "Review changed files, then run verification.";

    public static string NextActionCommit(bool useKoreanUi) => useKoreanUi ? "\uCEE4\uBC0B \uBA54\uC2DC\uC9C0\uB97C \uC900\uBE44\uD558\uACE0 \uCEE4\uBC0B\uD558\uC138\uC694." : "Prepare the commit message and commit.";

    public static string NextActionVerify(bool useKoreanUi) => useKoreanUi ? "\uCEE4\uBC0B \uC804\uC5D0 \uC9D1\uC911 \uAC80\uC99D\uC744 \uC2E4\uD589\uD558\uC138\uC694." : "Run focused verification before committing.";

    public static string NextActionDefault(bool useKoreanUi) => useKoreanUi ? "\uC694\uCCAD\uC744 \uBCF4\uB0B4\uAC70\uB098 \uD504\uB85C\uC81D\uD2B8 \uBD84\uC11D\uC744 \uC0C8\uB85C\uACE0\uCE68\uD558\uC138\uC694." : "Send a request or refresh project analysis.";

    public static string PermissionSummary(ToolPermissionAssessment assessment, bool useKoreanUi)
    {
        if (!useKoreanUi)
        {
            return assessment.RiskLevel switch
            {
                PermissionRiskLevel.ProjectWrite => "AgentQ wants to modify a project file.",
                PermissionRiskLevel.VerificationCommand => "AgentQ wants to run a build or test command.",
                PermissionRiskLevel.GitWrite => "AgentQ wants to change Git state.",
                PermissionRiskLevel.Network => "AgentQ wants to run a command that may use the network.",
                PermissionRiskLevel.Destructive => "AgentQ tried to run a command classified as risky.",
                _ => "AgentQ wants to run an operation that needs approval."
            };
        }

        return assessment.RiskLevel switch
        {
            PermissionRiskLevel.ProjectWrite => "AgentQ\uAC00 \uD504\uB85C\uC81D\uD2B8 \uD30C\uC77C\uC744 \uC218\uC815\uD558\uB824\uACE0 \uD569\uB2C8\uB2E4.",
            PermissionRiskLevel.VerificationCommand => "AgentQ\uAC00 \uBE4C\uB4DC \uB610\uB294 \uD14C\uC2A4\uD2B8 \uBA85\uB839\uC744 \uC2E4\uD589\uD558\uB824\uACE0 \uD569\uB2C8\uB2E4.",
            PermissionRiskLevel.GitWrite => "AgentQ\uAC00 Git \uC0C1\uD0DC\uB97C \uBCC0\uACBD\uD558\uB824\uACE0 \uD569\uB2C8\uB2E4.",
            PermissionRiskLevel.Network => "AgentQ\uAC00 \uB124\uD2B8\uC6CC\uD06C\uB97C \uC0AC\uC6A9\uD560 \uC218 \uC788\uB294 \uBA85\uB839\uC744 \uC2E4\uD589\uD558\uB824\uACE0 \uD569\uB2C8\uB2E4.",
            PermissionRiskLevel.Destructive => "AgentQ\uAC00 \uC704\uD5D8\uD55C \uC791\uC5C5\uC73C\uB85C \uBD84\uB958\uB41C \uBA85\uB839\uC744 \uC2E4\uD589\uD558\uB824\uACE0 \uD588\uC2B5\uB2C8\uB2E4.",
            _ => "AgentQ\uAC00 \uC2B9\uC778 \uD544\uC694\uD55C \uC791\uC5C5\uC744 \uC2E4\uD589\uD558\uB824\uACE0 \uD569\uB2C8\uB2E4."
        };
    }

    public static string PermissionBlockedTitle(bool useKoreanUi) =>
        useKoreanUi ? "AgentQ \uAD8C\uD55C \uCC28\uB2E8" : "AgentQ permission blocked";

    public static string PermissionBlockedMessage(
        ToolPermissionAssessment assessment,
        AgentWorkMode workMode,
        string policyReason,
        bool useKoreanUi)
    {
        if (!useKoreanUi)
        {
            return
                $"Blocked by AgentQ safety policy.{Environment.NewLine}{Environment.NewLine}" +
                $"Risk: {assessment.RiskLevel}{Environment.NewLine}" +
                $"Operation: {assessment.Operation}{Environment.NewLine}" +
                $"Target: {assessment.Target}{Environment.NewLine}" +
                $"Mode: {workMode}{Environment.NewLine}" +
                $"Policy: {policyReason}{Environment.NewLine}{Environment.NewLine}" +
                assessment.Reason;
        }

        return
            $"AgentQ \uC548\uC804 \uC815\uCC45\uC5D0 \uC758\uD574 \uCC28\uB2E8\uB418\uC5C8\uC2B5\uB2C8\uB2E4.{Environment.NewLine}{Environment.NewLine}" +
            $"\uC704\uD5D8\uB3C4: {assessment.RiskLevel}{Environment.NewLine}" +
            $"\uC791\uC5C5: {assessment.Operation}{Environment.NewLine}" +
            $"\uB300\uC0C1: {assessment.Target}{Environment.NewLine}" +
            $"\uBAA8\uB4DC: {workMode}{Environment.NewLine}" +
            $"\uC815\uCC45: {policyReason}{Environment.NewLine}{Environment.NewLine}" +
            assessment.Reason;
    }

    public static string ReusableApprovalHint(bool useKoreanUi)
    {
        return useKoreanUi
            ? $"{Environment.NewLine}\uAC19\uC740 \uC885\uB958 \uD5C8\uC6A9\uC740 \uC774\uBC88 \uC2E4\uD589 \uB3D9\uC548 \uAC19\uC740 \uC791\uC5C5 \uC720\uD615\uC758 \uBC18\uBCF5 \uD655\uC778\uC744 \uAC74\uB108\uB701\uB2C8\uB2E4. \uD3B8\uC9D1+\uBE4C\uB4DC \uD5C8\uC6A9\uC740 \uC6CC\uD06C\uC2A4\uD398\uC774\uC2A4 \uD30C\uC77C \uD3B8\uC9D1\uACFC \uBE4C\uB4DC/\uD14C\uC2A4\uD2B8 \uBA85\uB839\uC5D0\uB9CC \uC801\uC6A9\uB429\uB2C8\uB2E4."
            : $"{Environment.NewLine}Allow similar will skip repeat prompts for this operation type during the current run. Allow edits + builds will skip repeat prompts for workspace file edits and verification commands only.";
    }

    private static string EnglishRunSummaryPhase(AgentRunState state, string statusText, bool isBusy)
    {
        if (isBusy)
        {
            return state switch
            {
                AgentRunState.GatheringContext => "Gathering context",
                AgentRunState.Generating => "Generating",
                AgentRunState.RunningTool => "Running tool",
                AgentRunState.WaitingForApproval => "Waiting for approval",
                AgentRunState.RecordingChanges => "Recording changes",
                AgentRunState.Verifying => "Verifying",
                AgentRunState.Planning => "Planning",
                _ => "Running"
            };
        }

        if (IsProblemStatus(statusText))
        {
            return "Needs attention";
        }

        return state switch
        {
            AgentRunState.Done => "Completed",
            AgentRunState.Failed => "Failed",
            AgentRunState.Cancelled => "Cancelled",
            AgentRunState.Idle => "Idle",
            _ => state.ToString()
        };
    }

    public static bool IsProblemStatus(string statusText)
    {
        return statusText.Contains("error", StringComparison.OrdinalIgnoreCase) ||
               statusText.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
               statusText.Contains("blocked", StringComparison.OrdinalIgnoreCase) ||
               statusText.Contains("cancelled", StringComparison.OrdinalIgnoreCase) ||
               statusText.Contains("denied", StringComparison.OrdinalIgnoreCase);
    }
}
