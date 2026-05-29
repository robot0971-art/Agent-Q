using System.IO;
using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

public sealed class DesktopPlanCommandService(
    DesktopPlanCheckpointWorkflowService planCheckpointWorkflowService,
    DesktopWorkspaceContextWorkflowService workspaceContextWorkflowService,
    WorkerExecutionPipeline workerExecutionPipeline)
{
    public async Task CreatePlanAsync(MainViewModel viewModel, Func<bool, Task> sendCurrentMessageAsync)
    {
        if (viewModel.IsBusy)
        {
            viewModel.StatusText = "AgentQ is busy";
            return;
        }

        var planPrompt = planCheckpointWorkflowService.BuildPlanPrompt(viewModel);
        if (string.IsNullOrWhiteSpace(planPrompt))
        {
            viewModel.StatusText = "No goal to plan";
            return;
        }

        viewModel.InputText = planPrompt;
        viewModel.AddLog("Plan prompt prepared");
        var messageCountBeforePlan = viewModel.Messages.Count;
        await sendCurrentMessageAsync(false);
        planCheckpointWorkflowService.CapturePlanItems(viewModel, messageCountBeforePlan);
    }

    public async Task ContinueNextPlanItemAsync(MainViewModel viewModel, Func<bool, Task> sendCurrentMessageAsync)
    {
        if (viewModel.IsBusy)
        {
            viewModel.StatusText = "AgentQ is busy";
            return;
        }

        if (planCheckpointWorkflowService.PrepareNextPlanItem(viewModel) == null)
        {
            return;
        }

        await sendCurrentMessageAsync(false);
    }

    public async Task PlanAndRunAsync(MainViewModel viewModel, Func<bool, Task> sendCurrentMessageAsync)
    {
        if (viewModel.IsBusy)
        {
            viewModel.StatusText = "AgentQ is busy";
            return;
        }

        var planPrompt = planCheckpointWorkflowService.BuildPlanPrompt(viewModel);
        if (string.IsNullOrWhiteSpace(planPrompt))
        {
            viewModel.StatusText = "No goal to plan";
            return;
        }

        viewModel.InputText = planPrompt;
        viewModel.AddLog("Plan+run prompt prepared");
        var messageCountBeforePlan = viewModel.Messages.Count;
        await sendCurrentMessageAsync(false);
        if (viewModel.IsBusy)
        {
            return;
        }

        planCheckpointWorkflowService.CapturePlanItems(viewModel, messageCountBeforePlan);
        if (viewModel.PlanItems.Count == 0)
        {
            return;
        }

        await ContinueNextPlanItemAsync(viewModel, sendCurrentMessageAsync);
    }

    public void MarkPlanItemDone(MainViewModel viewModel)
    {
        planCheckpointWorkflowService.MarkSelectedPlanItemDone(viewModel);
    }

    public void ApprovePlan(MainViewModel viewModel)
    {
        if (viewModel.CurrentWorkerExecutionContext != null &&
            workerExecutionPipeline.Approve(viewModel.CurrentWorkerExecutionContext))
        {
            viewModel.SetWorkerExecutionContext(viewModel.CurrentWorkerExecutionContext);
            viewModel.AddLog("Plan approved");
            return;
        }

        viewModel.ApprovePlan();
    }

    public async Task<WorkerScaffoldExecutionResult?> ExecuteWorkerScaffoldAsync(MainViewModel viewModel)
    {
        if (viewModel.IsBusy)
        {
            viewModel.StatusText = "AgentQ is busy";
            return null;
        }

        var context = viewModel.CurrentWorkerExecutionContext;
        if (context == null)
        {
            viewModel.StatusText = "No worker plan to execute";
            return null;
        }

        viewModel.IsBusy = true;
        try
        {
            viewModel.AddRunStep(
                AgentRunState.RunningTool,
                "Worker scaffold execution",
                context.Plan.Summary);
            var result = await workerExecutionPipeline.ExecuteScaffoldAsync(
                context,
                viewModel.WorkspaceRoot,
                ResolveFeatureName(context.Plan),
                CancellationToken.None);

            foreach (var file in result.CreatedFiles)
            {
                viewModel.FileChanges.Add(await CreateCreatedFileChangeAsync(viewModel.WorkspaceRoot, file));
            }

            foreach (var change in result.WiringChanges)
            {
                viewModel.FileChanges.Add(CreateWiringFileChange(viewModel.WorkspaceRoot, change));
            }

            foreach (var verificationPlan in context.VerificationPlans)
            {
                if (!viewModel.VerificationPlans.Any(plan =>
                        string.Equals(plan.Command, verificationPlan.Command, StringComparison.OrdinalIgnoreCase)))
                {
                    viewModel.VerificationPlans.Add(verificationPlan);
                }
            }

            viewModel.SetWorkerExecutionContext(context);
            viewModel.AddRunStep(
                result.Succeeded ? AgentRunState.Done : AgentRunState.Failed,
                result.Succeeded ? "Worker scaffold executed" : "Worker scaffold failed",
                FormatScaffoldResult(result));
            viewModel.AddLog(FormatScaffoldResult(result));
            return result;
        }
        finally
        {
            viewModel.IsBusy = false;
        }
    }

    public async Task ExecuteWorkerScaffoldAndVerifyAsync(
        MainViewModel viewModel,
        Func<AgentVerificationPlan, Task<DesktopVerificationWorkflowResult?>> runVerificationPlanAsync)
    {
        var result = await ExecuteWorkerScaffoldAsync(viewModel);
        var context = viewModel.CurrentWorkerExecutionContext;
        if (result?.Succeeded != true || context == null)
        {
            return;
        }

        var verificationPlan = context.VerificationPlans.FirstOrDefault();
        if (verificationPlan == null)
        {
            viewModel.StatusText = "Worker scaffold executed; no verification command found";
            viewModel.AddRunStep(
                AgentRunState.Done,
                "Worker scaffold verification skipped",
                "No verification command was available.");
            return;
        }

        viewModel.AddRunStep(
            AgentRunState.Verifying,
            "Worker scaffold verification",
            verificationPlan.Command);
        var verificationResult = await runVerificationPlanAsync(verificationPlan);
        if (verificationResult == null)
        {
            return;
        }

        workerExecutionPipeline.ApplyVerificationResult(context, verificationResult);
        viewModel.SetWorkerExecutionContext(context);
        if (context.State == WorkerExecutionState.RepairRequired)
        {
            var repairPrompt = DesktopPromptBuilder.BuildWorkerRepairPrompt(context);
            if (!string.IsNullOrWhiteSpace(repairPrompt))
            {
                viewModel.InputText = repairPrompt;
                viewModel.AddRunStep(
                    AgentRunState.Planning,
                    "Worker repair prompt prepared",
                    context.RepairPlan?.Summary);
            }
        }
    }

    public async Task RunWorkerRepairAsync(
        MainViewModel viewModel,
        Func<bool, Task> sendCurrentMessageAsync,
        Func<AgentVerificationPlan, Task<DesktopVerificationWorkflowResult?>> runVerificationPlanAsync)
    {
        if (viewModel.IsBusy)
        {
            viewModel.StatusText = "AgentQ is busy";
            return;
        }

        var context = viewModel.CurrentWorkerExecutionContext;
        if (context?.RepairPlan == null || context.State != WorkerExecutionState.RepairRequired)
        {
            viewModel.StatusText = "No worker repair is ready";
            return;
        }

        var prompt = DesktopPromptBuilder.BuildWorkerRepairPrompt(context);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            viewModel.StatusText = "Worker repair prompt is empty";
            return;
        }

        viewModel.InputText = prompt;
        viewModel.AddRunStep(
            AgentRunState.Planning,
            "Worker repair started",
            context.RepairPlan.Summary);
        await sendCurrentMessageAsync(true);
        if (viewModel.IsBusy)
        {
            return;
        }

        var verificationPlan = context.VerificationPlans.FirstOrDefault();
        if (verificationPlan == null)
        {
            viewModel.StatusText = "Worker repair completed; no verification command found";
            viewModel.AddRunStep(
                AgentRunState.Done,
                "Worker repair verification skipped",
                "No verification command was available.");
            return;
        }

        viewModel.AddRunStep(
            AgentRunState.Verifying,
            "Worker repair verification",
            verificationPlan.Command);
        var verificationResult = await runVerificationPlanAsync(verificationPlan);
        if (verificationResult == null)
        {
            return;
        }

        workerExecutionPipeline.ApplyVerificationResult(context, verificationResult);
        viewModel.SetWorkerExecutionContext(context);
        if (context.State == WorkerExecutionState.RepairRequired)
        {
            viewModel.InputText = DesktopPromptBuilder.BuildWorkerRepairPrompt(context);
            viewModel.AddRunStep(
                AgentRunState.Planning,
                "Worker repair prompt prepared",
                context.RepairPlan?.Summary);
        }
    }

    public async Task MarkDoneAndContinueAsync(MainViewModel viewModel, Func<bool, Task> sendCurrentMessageAsync)
    {
        MarkPlanItemDone(viewModel);
        if (viewModel.SelectedPlanItem != null)
        {
            await ContinueNextPlanItemAsync(viewModel, sendCurrentMessageAsync);
        }
    }

    private static string ResolveFeatureName(WorkerPlan plan)
    {
        if (!string.IsNullOrWhiteSpace(plan.Goal))
        {
            return plan.Goal;
        }

        return string.IsNullOrWhiteSpace(plan.Summary) ? "Feature" : plan.Summary;
    }

    private static async Task<FileChangeRecord> CreateCreatedFileChangeAsync(string workspaceRoot, string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(workspaceRoot, relativePath));
        var after = File.Exists(fullPath) ? await File.ReadAllTextAsync(fullPath) : string.Empty;
        var diffLines = after.Split(['\r', '\n'], StringSplitOptions.None)
            .Where(line => line.Length > 0)
            .Select(line => new DiffLine
            {
                Kind = DiffLineKind.Added,
                Text = line
            })
            .ToList();

        return new FileChangeRecord
        {
            Path = fullPath,
            RelativePath = relativePath.Replace('\\', '/'),
            ExistedBefore = false,
            After = after,
            DiffLines = diffLines
        };
    }

    private static FileChangeRecord CreateWiringFileChange(
        string workspaceRoot,
        WorkerScaffoldWiringChange change)
    {
        var fullPath = Path.GetFullPath(Path.Combine(workspaceRoot, change.Path));
        return new FileChangeRecord
        {
            Path = fullPath,
            RelativePath = change.Path.Replace('\\', '/'),
            ExistedBefore = !string.IsNullOrEmpty(change.Before),
            Before = change.Before,
            After = change.After,
            DiffLines = BuildSimpleDiff(change.Before, change.After)
        };
    }

    private static IReadOnlyList<DiffLine> BuildSimpleDiff(string before, string after)
    {
        var beforeLines = before.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var afterLines = after.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var diff = new List<DiffLine>();
        diff.AddRange(beforeLines
            .Where(line => !afterLines.Contains(line))
            .Select(line => new DiffLine { Kind = DiffLineKind.Removed, Text = line }));
        diff.AddRange(afterLines
            .Where(line => !beforeLines.Contains(line))
            .Select(line => new DiffLine { Kind = DiffLineKind.Added, Text = line }));
        return diff;
    }

    private static string FormatScaffoldResult(WorkerScaffoldExecutionResult result)
    {
        var created = result.CreatedFiles.Count == 0
            ? "Created: none"
            : $"Created: {string.Join(", ", result.CreatedFiles.Take(5))}";
        var skipped = result.SkippedFiles.Count == 0
            ? string.Empty
            : $" Skipped: {string.Join(", ", result.SkippedFiles.Take(5))}.";
        var wired = result.WiredFiles.Count == 0
            ? string.Empty
            : $" Wired: {string.Join(", ", result.WiredFiles.Take(5))}.";
        var issues = result.Issues.Count == 0
            ? string.Empty
            : $" Issues: {string.Join("; ", result.Issues.Take(3))}.";
        var verification = result.VerificationCommands.Count == 0
            ? string.Empty
            : $" Verification: {string.Join(", ", result.VerificationCommands.Take(3))}.";
        return $"{created}.{skipped}{wired}{issues}{verification}".Trim();
    }

    public async Task SaveCheckpointAsync(MainViewModel viewModel)
    {
        try
        {
            await planCheckpointWorkflowService.SaveCheckpointAsync(viewModel);
        }
        catch (Exception ex)
        {
            viewModel.StatusText = $"Checkpoint save failed: {ex.Message}";
            viewModel.AddLog($"Checkpoint save failed: {ex.Message}");
        }
    }

    public async Task LoadCheckpointAsync(MainViewModel viewModel)
    {
        await planCheckpointWorkflowService.LoadLatestCheckpointAsync(viewModel);
        viewModel.StatusText = planCheckpointWorkflowService.HasCheckpoint ? "Checkpoint loaded" : "No checkpoint found";
    }

    public async Task ResumeCheckpointAsync(MainViewModel viewModel, Func<bool, Task> sendCurrentMessageAsync)
    {
        if (viewModel.IsBusy)
        {
            viewModel.StatusText = "AgentQ is busy";
            return;
        }

        var resumePrompt = await planCheckpointWorkflowService.BuildResumeCheckpointPromptAsync(viewModel);
        if (string.IsNullOrWhiteSpace(resumePrompt))
        {
            return;
        }

        viewModel.InputText = resumePrompt;
        await sendCurrentMessageAsync(false);
    }

    public async Task SaveSessionSummaryAsync(
        MainViewModel viewModel,
        Func<string, string> trimForLog)
    {
        await workspaceContextWorkflowService.SaveSessionSummaryAsync(
            viewModel,
            "Manual session summary saved",
            trimForLog);
    }

    public async Task LoadSessionSummaryAsync(MainViewModel viewModel)
    {
        await workspaceContextWorkflowService.LoadLatestSessionSummaryAsync(viewModel);
        viewModel.StatusText = workspaceContextWorkflowService.HasSessionSummary
            ? "Session summary loaded"
            : "No session summary found";
    }

    public async Task ResumeSessionSummaryAsync(MainViewModel viewModel, Func<bool, Task> sendCurrentMessageAsync)
    {
        if (viewModel.IsBusy)
        {
            viewModel.StatusText = "AgentQ is busy";
            return;
        }

        var resumePrompt = await workspaceContextWorkflowService.BuildResumeSessionSummaryPromptAsync(viewModel);
        if (string.IsNullOrWhiteSpace(resumePrompt))
        {
            return;
        }

        viewModel.InputText = resumePrompt;
        await sendCurrentMessageAsync(false);
    }
}
