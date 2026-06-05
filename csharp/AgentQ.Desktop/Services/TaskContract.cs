using System.Text;

namespace AgentQ.Desktop.Services;

public sealed class TaskContract
{
    public TaskContractIntent Intent { get; init; } = TaskContractIntent.None;

    public double Confidence { get; init; }

    public string Goal { get; init; } = string.Empty;

    public IReadOnlyList<string> RequiredActions { get; init; } = [];

    public IReadOnlyList<string> DoneWhen { get; init; } = [];

    public IReadOnlyList<string> InvalidCompletions { get; init; } = [];

    public bool IsActionable => Intent != TaskContractIntent.None && Confidence >= 0.75;
}

public enum TaskContractIntent
{
    None,
    RunLocalServer,
    StopLocalServer,
    CreateProject,
    ModifyCode,
    InspectProject,
    ExplainOrChat
}

public static class UserIntentTranslator
{
    public static TaskContract Translate(string userText)
    {
        var normalized = Normalize(userText);
        if (IsStopLocalServerRequest(normalized))
        {
            return new TaskContract
            {
                Intent = TaskContractIntent.StopLocalServer,
                Confidence = 0.92,
                Goal = "Stop the local development server for the selected workspace.",
                RequiredActions =
                [
                    "find the active local server session for this workspace",
                    "stop the server process",
                    "report whether the server was stopped or no active server was found"
                ],
                DoneWhen =
                [
                    "the active local server process is stopped",
                    "or no active local server session exists"
                ],
                InvalidCompletions =
                [
                    "only describing project structure",
                    "asking what to do next without checking the server session"
                ]
            };
        }

        if (IsRunLocalServerRequest(normalized))
        {
            return new TaskContract
            {
                Intent = TaskContractIntent.RunLocalServer,
                Confidence = 0.93,
                Goal = "Start the local development server and report a reachable localhost URL.",
                RequiredActions =
                [
                    "inspect package scripts or equivalent run configuration",
                    "start the appropriate development server command",
                    "detect and verify the reachable localhost URL"
                ],
                DoneWhen =
                [
                    "a localhost URL responds",
                    "or startup failed with a concrete command error"
                ],
                InvalidCompletions =
                [
                    "only describing project structure",
                    "asking what to do next without attempting startup",
                    "summarizing files without running a command"
                ]
            };
        }

        return new TaskContract
        {
            Intent = TaskContractIntent.None,
            Confidence = 0,
            Goal = string.Empty
        };
    }

    private static bool IsRunLocalServerRequest(string normalized)
    {
        var hasServer = ContainsAny(normalized,
            "localserver", "devserver", "localhost", "server",
            "\uB85C\uCEEC\uC11C\uBC84", "\uAC1C\uBC1C\uC11C\uBC84", "\uC11C\uBC84");
        var hasRunVerb = ContainsAny(normalized,
            "run", "start", "serve", "launch", "open", "preview", "npmrundev",
            "\uB744\uC6CC", "\uB744\uC6B0", "\uC2E4\uD589", "\uC5F4\uC5B4", "\uBCF4\uC5EC", "\uCF1C");
        return (hasServer && hasRunVerb) || ContainsAny(normalized, "npmrundev", "npmstart", "yarn dev", "pnpmdev");
    }

    private static bool IsStopLocalServerRequest(string normalized)
    {
        var hasServer = ContainsAny(normalized,
            "localserver", "devserver", "localhost", "server",
            "\uB85C\uCEEC\uC11C\uBC84", "\uAC1C\uBC1C\uC11C\uBC84", "\uC11C\uBC84");
        var hasStopVerb = ContainsAny(normalized,
            "stop", "kill", "terminate", "shutdown", "close",
            "\uB044", "\uAEBC", "\uB054", "\uC885\uB8CC", "\uC911\uC9C0", "\uB0B4\uB824", "\uB2EB");
        return hasServer && hasStopVerb;
    }

    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text.ToLowerInvariant())
        {
            if (!char.IsWhiteSpace(ch) && ch != '-' && ch != '_' && ch != '`')
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
}

public static class TaskContractPromptBuilder
{
    public static string BuildContext(TaskContract contract)
    {
        if (!contract.IsActionable)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Current task contract:");
        builder.AppendLine($"- Intent: {FormatIntent(contract.Intent)}");
        builder.AppendLine($"- Confidence: {contract.Confidence:0.00}");
        builder.AppendLine($"- Goal: {contract.Goal}");
        AppendList(builder, "Required actions", contract.RequiredActions);
        AppendList(builder, "Done when", contract.DoneWhen);
        AppendList(builder, "Invalid completions", contract.InvalidCompletions);
        builder.AppendLine("- Keep this contract as the completion target even after inspecting files.");
        return builder.ToString().TrimEnd();
    }

    private static void AppendList(StringBuilder builder, string label, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        builder.AppendLine($"- {label}:");
        foreach (var value in values)
        {
            builder.AppendLine($"  - {value}");
        }
    }

    private static string FormatIntent(TaskContractIntent intent) => intent switch
    {
        TaskContractIntent.RunLocalServer => "run_local_server",
        TaskContractIntent.StopLocalServer => "stop_local_server",
        TaskContractIntent.CreateProject => "create_project",
        TaskContractIntent.ModifyCode => "modify_code",
        TaskContractIntent.InspectProject => "inspect_project",
        TaskContractIntent.ExplainOrChat => "explain_or_chat",
        _ => "none"
    };
}

public static class TaskContractCompletionChecker
{
    public static bool ShouldRetry(TaskContract contract, string assistantText, IReadOnlyList<string> executedCommands, AgentWorkMode workMode)
    {
        if (workMode == AgentWorkMode.Readonly || !contract.IsActionable)
        {
            return false;
        }

        return contract.Intent switch
        {
            TaskContractIntent.RunLocalServer => executedCommands.Count == 0 && LooksLikeInvalidRunServerCompletion(assistantText),
            TaskContractIntent.StopLocalServer => false,
            _ => false
        };
    }

    public static bool ShouldReject(TaskContract contract, string assistantText, IReadOnlyList<string> executedCommands, AgentWorkMode workMode)
    {
        if (workMode == AgentWorkMode.Readonly || !contract.IsActionable)
        {
            return false;
        }

        return contract.Intent switch
        {
            TaskContractIntent.RunLocalServer => executedCommands.Count == 0 && LooksLikeInvalidRunServerCompletion(assistantText),
            TaskContractIntent.StopLocalServer => false,
            _ => false
        };
    }

    public static string BuildRetryInstruction(TaskContract contract)
    {
        if (contract.Intent == TaskContractIntent.RunLocalServer)
        {
            return
                "The current task contract is run_local_server. Do not complete with a project structure summary. " +
                "Inspect package scripts if needed, start the local development server with the appropriate command, " +
                "verify the reachable localhost URL, then report that URL. If startup fails, report the concrete command error.";
        }

        if (contract.Intent == TaskContractIntent.StopLocalServer)
        {
            return
                "The current task contract is stop_local_server. Do not complete with a project structure summary. " +
                "Find the active local server session for this workspace, stop it if present, then report the result.";
        }

        return "The previous answer did not satisfy the current task contract. Re-plan from the contract goal and perform the required actions before answering.";
    }

    private static bool LooksLikeInvalidRunServerCompletion(string assistantText)
    {
        var text = assistantText.ToLowerInvariant();
        var mentionsStructure = ContainsAny(text,
            "project structure",
            "프로젝트 구조",
            "src/app.jsx",
            "src/main.jsx",
            "vite.config",
            "현재 앱은",
            "main component",
            "entry point");
        var asksNext = ContainsAny(text,
            "what would you like",
            "what can i help",
            "어떤 작업",
            "무엇을 도와");
        var reportsUrl = ContainsAny(text, "http://localhost:", "http://127.0.0.1:");
        var reportsFailure = ContainsAny(text, "failed", "error", "실패", "오류");
        return !reportsUrl && !reportsFailure && (mentionsStructure || asksNext);
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
}
