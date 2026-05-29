using System.IO;

namespace AgentQ.Desktop.Services;

public sealed class WorkerPlanValidator
{
    public WorkerPlanValidationResult Validate(
        WorkerPlan plan,
        string workspaceRoot,
        IEnumerable<string>? projectAllowedCommands = null)
    {
        var issues = new List<WorkerPlanValidationIssue>();
        var root = string.IsNullOrWhiteSpace(workspaceRoot)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(workspaceRoot);

        foreach (var step in plan.Steps)
        {
            ValidateStep(step, root, projectAllowedCommands, issues);
        }

        foreach (var command in plan.VerificationCommands)
        {
            if (!VerificationCommandPolicy.IsAllowed(command, projectAllowedCommands))
            {
                issues.Add(new WorkerPlanValidationIssue
                {
                    Severity = WorkerPlanValidationSeverity.Blocker,
                    Code = "verification_command_not_allowed",
                    Message = $"Verification command is not allowed: {command}"
                });
            }
        }

        return new WorkerPlanValidationResult { Issues = issues };
    }

    private static void ValidateStep(
        WorkerPlanStep step,
        string workspaceRoot,
        IEnumerable<string>? projectAllowedCommands,
        List<WorkerPlanValidationIssue> issues)
    {
        if (step.Kind is WorkerPlanStepKind.CreateFile or WorkerPlanStepKind.ModifyFile or WorkerPlanStepKind.DeleteFile)
        {
            ValidatePathStep(step, workspaceRoot, issues);
        }

        if (step.Kind == WorkerPlanStepKind.DeleteFile)
        {
            issues.Add(new WorkerPlanValidationIssue
            {
                Severity = WorkerPlanValidationSeverity.ApprovalRequired,
                Code = "delete_requires_approval",
                Message = $"Deleting a file requires explicit approval: {step.Path}",
                Path = step.Path
            });
        }

        if (step.RequiresApproval)
        {
            issues.Add(new WorkerPlanValidationIssue
            {
                Severity = WorkerPlanValidationSeverity.ApprovalRequired,
                Code = "step_requires_approval",
                Message = $"Plan step requires explicit approval: {step.Path}",
                Path = step.Path
            });
        }

        if (step.Kind == WorkerPlanStepKind.Verify &&
            !string.IsNullOrWhiteSpace(step.ExpectedChange) &&
            !VerificationCommandPolicy.IsAllowed(step.ExpectedChange, projectAllowedCommands))
        {
            issues.Add(new WorkerPlanValidationIssue
            {
                Severity = WorkerPlanValidationSeverity.Blocker,
                Code = "verification_step_not_allowed",
                Message = $"Verification step is not allowed: {step.ExpectedChange}"
            });
        }
    }

    private static void ValidatePathStep(
        WorkerPlanStep step,
        string workspaceRoot,
        List<WorkerPlanValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(step.Path))
        {
            issues.Add(new WorkerPlanValidationIssue
            {
                Severity = WorkerPlanValidationSeverity.Blocker,
                Code = "missing_path",
                Message = "File plan step is missing a path."
            });
            return;
        }

        var fullPath = Path.IsPathRooted(step.Path)
            ? Path.GetFullPath(step.Path)
            : Path.GetFullPath(Path.Combine(workspaceRoot, step.Path));

        if (!IsInsideWorkspace(workspaceRoot, fullPath))
        {
            issues.Add(new WorkerPlanValidationIssue
            {
                Severity = WorkerPlanValidationSeverity.Blocker,
                Code = "path_outside_workspace",
                Message = $"Plan path is outside the workspace: {step.Path}",
                Path = step.Path
            });
        }
    }

    private static bool IsInsideWorkspace(string workspaceRoot, string fullPath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var root = Path.GetFullPath(workspaceRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootWithSeparator = root + Path.DirectorySeparatorChar;

        return fullPath.Equals(root, comparison) ||
               fullPath.StartsWith(rootWithSeparator, comparison);
    }
}
