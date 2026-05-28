using AgentQ.Tools;

namespace AgentQ.Desktop.Services;

public static class DesktopLocalizer
{
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
