using AgentQ.Tools;
using System.Globalization;

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
            nameof(DesktopText.OpenVSCode) => useKoreanUi ? "VS Code" : "VS Code",
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
            nameof(DesktopText.GitRefreshingStatus) => useKoreanUi ? "Git \uC0C1\uD0DC \uC0C8\uB85C\uACE0\uCE68 \uC911" : "Refreshing git status",
            nameof(DesktopText.GitStatusRefreshed) => useKoreanUi ? "Git \uC0C1\uD0DC \uC0C8\uB85C\uACE0\uCE68 \uC644\uB8CC" : "Git status refreshed",
            nameof(DesktopText.GitStatusFailed) => useKoreanUi ? "Git \uC0C1\uD0DC \uC0C8\uB85C\uACE0\uCE68 \uC2E4\uD328" : "Git status failed",
            nameof(DesktopText.GitRefreshingDiff) => useKoreanUi ? "Git diff \uC0C8\uB85C\uACE0\uCE68 \uC911" : "Refreshing git diff",
            nameof(DesktopText.GitDiffRefreshed) => useKoreanUi ? "Git diff \uC0C8\uB85C\uACE0\uCE68 \uC644\uB8CC" : "Git diff refreshed",
            nameof(DesktopText.GitDiffFailed) => useKoreanUi ? "Git diff \uC0C8\uB85C\uACE0\uCE68 \uC2E4\uD328" : "Git diff failed",
            nameof(DesktopText.GitLoadingFileDiff) => useKoreanUi ? "\uD30C\uC77C diff \uBD88\uB7EC\uC624\uB294 \uC911" : "Loading file diff",
            nameof(DesktopText.GitFileDiffLoaded) => useKoreanUi ? "\uD30C\uC77C diff \uBD88\uB7EC\uC634" : "File diff loaded",
            nameof(DesktopText.GitFileDiffFailed) => useKoreanUi ? "\uD30C\uC77C diff \uBD88\uB7EC\uC624\uAE30 \uC2E4\uD328" : "File diff failed",
            nameof(DesktopText.GitNoSelectedChangedFile) => useKoreanUi ? "\uC120\uD0DD\uB41C \uBCC0\uACBD \uD30C\uC77C\uC774 \uC5C6\uC2B5\uB2C8\uB2E4" : "No selected changed file",
            nameof(DesktopText.GitStagingSelectedFile) => useKoreanUi ? "\uC120\uD0DD\uD55C \uD30C\uC77C stage \uC911" : "Staging selected file",
            nameof(DesktopText.GitStagingApprovedFiles) => useKoreanUi ? "\uC2B9\uC778\uB41C \uD30C\uC77C stage \uC911" : "Staging approved files",
            nameof(DesktopText.GitUnstagingSelectedFile) => useKoreanUi ? "\uC120\uD0DD\uD55C \uD30C\uC77C unstage \uC911" : "Unstaging selected file",
            nameof(DesktopText.GitSelectedFileStaged) => useKoreanUi ? "\uC120\uD0DD\uD55C \uD30C\uC77C stage \uC644\uB8CC" : "Selected file staged",
            nameof(DesktopText.GitStageSelectedFailed) => useKoreanUi ? "\uC120\uD0DD\uD55C \uD30C\uC77C stage \uC2E4\uD328" : "Stage selected failed",
            nameof(DesktopText.GitApprovedFilesStaged) => useKoreanUi ? "\uC2B9\uC778\uB41C \uD30C\uC77C stage \uC644\uB8CC" : "Approved files staged",
            nameof(DesktopText.GitStageApprovedFailed) => useKoreanUi ? "\uC2B9\uC778\uB41C \uD30C\uC77C stage \uC2E4\uD328" : "Stage approved failed",
            nameof(DesktopText.GitSelectedFileUnstaged) => useKoreanUi ? "\uC120\uD0DD\uD55C \uD30C\uC77C unstage \uC644\uB8CC" : "Selected file unstaged",
            nameof(DesktopText.GitUnstageSelectedFailed) => useKoreanUi ? "\uC120\uD0DD\uD55C \uD30C\uC77C unstage \uC2E4\uD328" : "Unstage selected failed",
            nameof(DesktopText.GitCommitMessageRequired) => useKoreanUi ? "\uCEE4\uBC0B \uBA54\uC2DC\uC9C0\uAC00 \uD544\uC694\uD569\uB2C8\uB2E4" : "Commit message is required",
            nameof(DesktopText.GitNoStagedFilesToCommit) => useKoreanUi ? "\uCEE4\uBC0B\uD560 staged \uD30C\uC77C\uC774 \uC5C6\uC2B5\uB2C8\uB2E4" : "No staged files to commit",
            nameof(DesktopText.GitCreatingCommit) => useKoreanUi ? "\uCEE4\uBC0B \uC0DD\uC131 \uC911" : "Creating commit",
            nameof(DesktopText.GitCommitCreated) => useKoreanUi ? "\uCEE4\uBC0B \uC0DD\uC131 \uC644\uB8CC" : "Commit created",
            nameof(DesktopText.GitCommitFailed) => useKoreanUi ? "\uCEE4\uBC0B \uC2E4\uD328" : "Commit failed",
            nameof(DesktopText.GitCheckingPullSafety) => useKoreanUi ? "Pull \uC548\uC804\uC131 \uD655\uC778 \uC911" : "Checking pull safety",
            nameof(DesktopText.GitPullUnavailable) => useKoreanUi ? "Pull \uC0AC\uC6A9 \uBD88\uAC00: {0}" : "Pull unavailable: {0}",
            nameof(DesktopText.GitPullBlocked) => useKoreanUi ? "Pull \uCC28\uB2E8\uB428: {0}" : "Pull blocked: {0}",
            nameof(DesktopText.GitPullingFastForward) => useKoreanUi ? "fast-forward only\uB85C pull \uC911" : "Pulling with fast-forward only",
            nameof(DesktopText.GitPullCompleted) => useKoreanUi ? "Pull \uC644\uB8CC" : "Pull completed",
            nameof(DesktopText.GitPullFailed) => useKoreanUi ? "Pull \uC2E4\uD328" : "Pull failed",
            nameof(DesktopText.GitCreatingBackupBranch) => useKoreanUi ? "\uBC31\uC5C5 \uBE0C\uB79C\uCE58 \uC0DD\uC131 \uC911" : "Creating backup branch",
            nameof(DesktopText.GitBackupBranchCreated) => useKoreanUi ? "\uBC31\uC5C5 \uBE0C\uB79C\uCE58 \uC0DD\uC131\uB428: {0}" : "Backup branch created: {0}",
            nameof(DesktopText.GitBackupBranchFailed) => useKoreanUi ? "\uBC31\uC5C5 \uBE0C\uB79C\uCE58 \uC0DD\uC131 \uC2E4\uD328" : "Backup branch failed",
            nameof(DesktopText.GitCheckingBranchSwitchSafety) => useKoreanUi ? "\uBE0C\uB79C\uCE58 \uC804\uD658 \uC548\uC804\uC131 \uD655\uC778 \uC911" : "Checking branch switch safety",
            nameof(DesktopText.GitSwitchBlocked) => useKoreanUi ? "\uBE0C\uB79C\uCE58 \uC804\uD658 \uCC28\uB2E8\uB428: {0}" : "Switch blocked: {0}",
            nameof(DesktopText.GitSwitchingToMain) => useKoreanUi ? "main\uC73C\uB85C \uC804\uD658 \uC911" : "Switching to main",
            nameof(DesktopText.GitSwitchedToMain) => useKoreanUi ? "main\uC73C\uB85C \uC804\uD658\uB428" : "Switched to main",
            nameof(DesktopText.GitSwitchToMainFailed) => useKoreanUi ? "main \uC804\uD658 \uC2E4\uD328" : "Switch to main failed",
            nameof(DesktopText.GitChangeMarked) => useKoreanUi ? "\uBCC0\uACBD \uD45C\uC2DC\uB428: {0}" : "Change marked {0}",
            nameof(DesktopText.GitChangeReviewStatus) => useKoreanUi ? "\uBCC0\uACBD \uAC80\uD1A0 \uC0C1\uD0DC: {0} - {1}" : "Change review status: {0} - {1}",
            nameof(DesktopText.GitCodeReviewCaptured) => useKoreanUi ? "\uCF54\uB4DC \uB9AC\uBDF0 \uAE30\uB85D\uB428" : "Code review captured",
            nameof(DesktopText.GitNoChangedFiles) => useKoreanUi ? "\uBCC0\uACBD \uD30C\uC77C\uC774 \uC5C6\uC2B5\uB2C8\uB2E4." : "No changed files.",
            nameof(DesktopText.GitLastUpdated) => useKoreanUi ? "\uB9C8\uC9C0\uB9C9 \uC5C5\uB370\uC774\uD2B8: {0}" : "Last updated: {0}",
            nameof(DesktopText.NoValidProjectFolderToOpen) => useKoreanUi ? "\uC5F4 \uC218 \uC788\uB294 \uC720\uD6A8\uD55C \uD504\uB85C\uC81D\uD2B8 \uD3F4\uB354\uAC00 \uC5C6\uC2B5\uB2C8\uB2E4." : "No valid project folder to open.",
            nameof(DesktopText.VSCodeOpened) => useKoreanUi ? "VS Code\uB85C \uD504\uB85C\uC81D\uD2B8\uB97C \uC5F4\uC5C8\uC2B5\uB2C8\uB2E4." : "Project opened in VS Code.",
            nameof(DesktopText.VSCodeOpenFailed) => useKoreanUi ? "VS Code\uB97C \uC2E4\uD589\uD558\uC9C0 \uBABB\uD588\uC2B5\uB2C8\uB2E4. PATH\uC5D0 'code' \uBA85\uB839\uC774 \uB4F1\uB85D\uB418\uC5B4 \uC788\uB294\uC9C0 \uD655\uC778\uD558\uC138\uC694." : "Could not start VS Code. Make sure the 'code' command is available on PATH.",
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

    public static string FormatUiText(string key, bool useKoreanUi, params object[] args)
    {
        return string.Format(CultureInfo.InvariantCulture, UiText(key, useKoreanUi), args);
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
                AgentRunState.Clarifying => "\uC9C8\uBB38",
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
            AgentRunState.Clarifying => "QUESTION",
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
            AgentRunState.Clarifying => "\uC9C8\uBB38 \uB300\uAE30",
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
            "Waiting for user answer" => "\uC0AC\uC6A9\uC790 \uB2F5\uBCC0 \uB300\uAE30",
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
                AgentRunState.Clarifying => "\uC9C8\uBB38 \uC900\uBE44",
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
            AgentRunState.Clarifying => "\uB2F5\uBCC0 \uB300\uAE30",
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

    public static string NextActionAnswerQuestion(bool useKoreanUi) => useKoreanUi ? "AgentQ\uC758 \uC9C8\uBB38\uC5D0 \uB2F5\uD558\uBA74 \uC774\uC5B4\uC11C \uC9C4\uD589\uD569\uB2C8\uB2E4." : "Answer AgentQ's question to continue.";

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
                PermissionRiskLevel.LowRiskProjectWrite => "AgentQ wants to create an empty workspace item.",
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
            PermissionRiskLevel.LowRiskProjectWrite => "AgentQ\uAC00 \uBE48 \uD30C\uC77C \uB610\uB294 \uBE48 \uD3F4\uB354\uB97C \uB9CC\uB4E4\uB824\uACE0 \uD569\uB2C8\uB2E4.",
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
                AgentRunState.Clarifying => "Preparing question",
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
            AgentRunState.Clarifying => "Waiting for answer",
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
