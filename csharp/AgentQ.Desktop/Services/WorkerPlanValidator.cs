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
            issues.Add(new WorkerPlanValidationIssue
            {
                Severity = WorkerPlanValidationSeverity.ApprovalRequired,
                Code = "file_mutation_requires_approval",
                Message = $"Creating, modifying, or deleting a file requires explicit approval: {step.Path}",
                Path = step.Path
            });
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

        if (step.Kind == WorkerPlanStepKind.RunCommand)
        {
            ValidateRunCommandStep(step, projectAllowedCommands, issues);
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

    private static void ValidateRunCommandStep(
        WorkerPlanStep step,
        IEnumerable<string>? projectAllowedCommands,
        List<WorkerPlanValidationIssue> issues)
    {
        var command = ResolveCommandText(step);
        issues.Add(new WorkerPlanValidationIssue
        {
            Severity = WorkerPlanValidationSeverity.ApprovalRequired,
            Code = "run_command_requires_approval",
            Message = string.IsNullOrWhiteSpace(command)
                ? "Running a command requires explicit approval."
                : $"Running a command requires explicit approval: {command}",
            Path = step.Path
        });

        if (LooksLikeShellCommand(command) &&
            !VerificationCommandPolicy.IsAllowed(command, projectAllowedCommands))
        {
            issues.Add(new WorkerPlanValidationIssue
            {
                Severity = WorkerPlanValidationSeverity.Blocker,
                Code = "run_command_not_allowed",
                Message = $"Run command is not allowed: {command}",
                Path = step.Path
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

        string fullPath;
        try
        {
            fullPath = Path.IsPathRooted(step.Path)
                ? Path.GetFullPath(step.Path)
                : Path.GetFullPath(Path.Combine(workspaceRoot, step.Path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            issues.Add(new WorkerPlanValidationIssue
            {
                Severity = WorkerPlanValidationSeverity.Blocker,
                Code = "invalid_path",
                Message = $"Plan path is invalid: {step.Path}",
                Path = step.Path
            });
            return;
        }

        if (!IsInsideWorkspace(workspaceRoot, fullPath))
        {
            issues.Add(new WorkerPlanValidationIssue
            {
                Severity = WorkerPlanValidationSeverity.Blocker,
                Code = "path_outside_workspace",
                Message = $"Plan path is outside the workspace: {step.Path}",
                Path = step.Path
            });
            return;
        }

        if (!WorkspacePathResolver.IsResolvedInsideWorkspace(workspaceRoot, fullPath))
        {
            issues.Add(new WorkerPlanValidationIssue
            {
                Severity = WorkerPlanValidationSeverity.Blocker,
                Code = "path_resolves_outside_workspace",
                Message = $"Plan path resolves outside the workspace: {step.Path}",
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

    private static string ResolveCommandText(WorkerPlanStep step)
    {
        if (!string.IsNullOrWhiteSpace(step.Path))
        {
            return step.Path.Trim();
        }

        if (!string.IsNullOrWhiteSpace(step.ExpectedChange))
        {
            return step.ExpectedChange.Trim();
        }

        return step.Reason.Trim();
    }

    private static bool LooksLikeShellCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var lower = command.Trim().ToLowerInvariant();
        return lower.StartsWith("cmd ", StringComparison.Ordinal) ||
               lower.StartsWith("cmd.exe ", StringComparison.Ordinal) ||
               lower.StartsWith("powershell ", StringComparison.Ordinal) ||
               lower.StartsWith("pwsh ", StringComparison.Ordinal) ||
               lower.StartsWith("bash ", StringComparison.Ordinal) ||
               lower.StartsWith("sh ", StringComparison.Ordinal) ||
               lower.StartsWith("dotnet ", StringComparison.Ordinal) ||
               lower.StartsWith("npm ", StringComparison.Ordinal) ||
               lower.StartsWith("pnpm ", StringComparison.Ordinal) ||
               lower.StartsWith("yarn ", StringComparison.Ordinal) ||
               lower.StartsWith("bun ", StringComparison.Ordinal) ||
               lower.StartsWith("bunx ", StringComparison.Ordinal) ||
               lower.StartsWith("npx ", StringComparison.Ordinal) ||
               lower.StartsWith("python ", StringComparison.Ordinal) ||
               lower.StartsWith("pytest", StringComparison.Ordinal) ||
               lower.StartsWith("go ", StringComparison.Ordinal) ||
               lower.StartsWith("cargo ", StringComparison.Ordinal) ||
               lower.StartsWith("mvn ", StringComparison.Ordinal) ||
               lower.StartsWith("gradle ", StringComparison.Ordinal) ||
               lower.StartsWith("./gradlew ", StringComparison.Ordinal) ||
               lower.StartsWith("remove-item", StringComparison.Ordinal) ||
               lower.StartsWith("rm ", StringComparison.Ordinal) ||
               lower.StartsWith("del ", StringComparison.Ordinal) ||
               lower.StartsWith("rmdir ", StringComparison.Ordinal) ||
               lower.StartsWith("rd ", StringComparison.Ordinal) ||
               lower.StartsWith("erase ", StringComparison.Ordinal) ||
               lower.StartsWith("git ", StringComparison.Ordinal);
    }
}
