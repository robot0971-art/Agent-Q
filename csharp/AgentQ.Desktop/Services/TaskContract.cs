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
    DeletePath,
    CreateDirectory,
    CreateFile,
    CreateProject,
    ModifyCode,
    RunVerification,
    SearchAndSummarize,
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

        if (IsDeletePathRequest(normalized))
        {
            var target = ExtractRequestedTarget(userText, "file", "folder", "directory", "path", "\uD30C\uC77C", "\uD3F4\uB354", "\uB514\uB809\uD1A0\uB9AC", "\uACBD\uB85C");
            var goal = string.IsNullOrWhiteSpace(target)
                ? "Delete the explicit workspace file or folder requested by the user."
                : $"Delete the explicit workspace file or folder requested by the user: {target}.";
            return new TaskContract
            {
                Intent = TaskContractIntent.DeletePath,
                Confidence = 0.88,
                Goal = goal,
                RequiredActions =
                [
                    string.IsNullOrWhiteSpace(target)
                        ? "identify the explicit workspace-relative path from the user request"
                        : $"use the requested workspace-relative target: {target}",
                    "inspect the workspace only if needed to confirm the target exists",
                    "call delete_path for the explicit target instead of using shell deletion commands",
                    "report success, missing target, or permission denial"
                ],
                DoneWhen =
                [
                    "delete_path succeeds for the requested target",
                    "or delete_path reports that the target is missing or unsafe",
                    "or the user denies the required delete approval"
                ],
                InvalidCompletions =
                [
                    "only listing the workspace repeatedly",
                    "describing AgentQ or tool capabilities",
                    "asking what to do next when the user already named the target"
                ]
            };
        }

        if (IsCreateDirectoryRequest(normalized))
        {
            var target = ExtractRequestedTarget(userText, "folder", "directory", "dir", "\uD3F4\uB354", "\uB514\uB809\uD1A0\uB9AC");
            var goal = string.IsNullOrWhiteSpace(target)
                ? "Create the requested empty workspace folder."
                : $"Create the requested empty workspace folder: {target}.";
            return new TaskContract
            {
                Intent = TaskContractIntent.CreateDirectory,
                Confidence = 0.86,
                Goal = goal,
                RequiredActions =
                [
                    string.IsNullOrWhiteSpace(target)
                        ? "identify the explicit folder name or workspace-relative path"
                        : $"use the requested workspace-relative folder path: {target}",
                    "call create_directory for that target",
                    "report the created folder path or the concrete error"
                ],
                DoneWhen =
                [
                    "create_directory succeeds for the requested target",
                    "or create_directory reports that the target is unsafe or already exists"
                ],
                InvalidCompletions =
                [
                    "only saying the folder can be created",
                    "describing steps without calling create_directory",
                    "asking what to do next when the user already named the folder"
                ]
            };
        }

        if (IsRunVerificationRequest(normalized))
        {
            return new TaskContract
            {
                Intent = TaskContractIntent.RunVerification,
                Confidence = 0.88,
                Goal = "Run the requested build, test, lint, or verification command and report the concrete result.",
                RequiredActions =
                [
                    "identify the requested verification command or infer the focused workspace verification command",
                    "run the build, test, lint, or verification command with shell tools",
                    "report pass, fail, or the concrete command error"
                ],
                DoneWhen =
                [
                    "a verification command was executed and its result was reported",
                    "or no safe verification command could be identified and the assistant asked one focused question"
                ],
                InvalidCompletions =
                [
                    "only explaining how to run tests",
                    "summarizing project files without running a command",
                    "asking what to do next when the user already asked to run verification"
                ]
            };
        }

        if (IsCreateFileRequest(normalized))
        {
            var target = ExtractRequestedTarget(userText, "file", "\uD30C\uC77C");
            var goal = string.IsNullOrWhiteSpace(target)
                ? "Create the requested workspace file."
                : $"Create the requested workspace file: {target}.";
            return new TaskContract
            {
                Intent = TaskContractIntent.CreateFile,
                Confidence = 0.86,
                Goal = goal,
                RequiredActions =
                [
                    string.IsNullOrWhiteSpace(target)
                        ? "identify the explicit file name or workspace-relative path"
                        : $"use the requested workspace-relative file path: {target}",
                    "call write_file for the target file",
                    "report the created file path or the concrete error"
                ],
                DoneWhen =
                [
                    "write_file succeeds for the requested file",
                    "or write_file reports that the target is unsafe or needs approval"
                ],
                InvalidCompletions =
                [
                    "only saying the file can be created",
                    "showing file contents in prose without calling write_file",
                    "asking what to do next when the user already named the file"
                ]
            };
        }

        if (IsModifyCodeRequest(normalized))
        {
            return new TaskContract
            {
                Intent = TaskContractIntent.ModifyCode,
                Confidence = 0.84,
                Goal = "Modify the requested workspace code or file and report what changed.",
                RequiredActions =
                [
                    "inspect the relevant file or search the workspace to find it",
                    "edit the requested file with workspace mutation tools",
                    "run focused verification when useful",
                    "summarize the changed file and result"
                ],
                DoneWhen =
                [
                    "the requested file was edited",
                    "or the target could not be found and the assistant asked one focused question"
                ],
                InvalidCompletions =
                [
                    "only explaining what should be changed",
                    "providing a patch in prose without applying it",
                    "summarizing the project without editing the target"
                ]
            };
        }

        if (IsSearchAndSummarizeRequest(normalized))
        {
            return new TaskContract
            {
                Intent = TaskContractIntent.SearchAndSummarize,
                Confidence = 0.82,
                Goal = "Search for the requested information and summarize the findings with evidence.",
                RequiredActions =
                [
                    "use search, read, fetch, or available network tools appropriate to the requested source",
                    "collect concrete evidence before summarizing",
                    "summarize the findings and mention any limits"
                ],
                DoneWhen =
                [
                    "evidence was gathered and summarized",
                    "or the requested source is unavailable and that limitation is reported"
                ],
                InvalidCompletions =
                [
                    "answering from guesses without searching",
                    "claiming to have checked without tool evidence",
                    "asking what to search when the user already named the topic"
                ]
            };
        }

        if (IsCreateProjectRequest(normalized))
        {
            return new TaskContract
            {
                Intent = TaskContractIntent.CreateProject,
                Confidence = 0.84,
                Goal = "Create or scaffold the requested project in the workspace.",
                RequiredActions =
                [
                    "use the scaffold or file creation tools to create the project",
                    "honor the requested stack and constraints",
                    "run focused verification when useful",
                    "report created files and verification result"
                ],
                DoneWhen =
                [
                    "project files were created",
                    "or the target is ambiguous and the assistant asked one focused question"
                ],
                InvalidCompletions =
                [
                    "only describing a possible project",
                    "saying the project will be created without creating files",
                    "asking what to do next when the requested stack is concrete"
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
        if (IsConsultativeRequest(normalized) || IsExplanationOnlyRequest(normalized))
        {
            return false;
        }

        var hasServer = ContainsAny(normalized,
            "localserver", "devserver", "localhost", "server",
            "\uB85C\uCEEC\uC11C\uBC84", "\uAC1C\uBC1C\uC11C\uBC84", "\uC11C\uBC84");
        var hasRunVerb = ContainsAny(normalized,
            "run", "start", "serve", "launch", "open", "preview", "npmrundev",
            "\uB744\uC6CC", "\uB744\uC6B0", "\uC2E4\uD589", "\uC5F4\uC5B4", "\uBCF4\uC5EC", "\uCF1C");
        return (hasServer && hasRunVerb) || ContainsAny(normalized, "npmrundev", "npmstart", "yarndev", "pnpmdev");
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

    private static bool IsDeletePathRequest(string normalized)
    {
        var hasDeleteVerb = ContainsAny(normalized, "delete", "remove", "erase", "\uC0AD\uC81C", "\uC9C0\uC6CC");
        var hasTargetHint = ContainsAny(normalized,
            "file", "folder", "directory", "path", "thisfolder", "currentfolder",
            "\uD30C\uC77C", "\uD3F4\uB354", "\uB514\uB809\uD1A0\uB9AC", "\uACBD\uB85C", "\uC774\uD3F4\uB354", "\uD604\uC7AC\uD3F4\uB354");
        return hasDeleteVerb && hasTargetHint;
    }

    private static bool IsRunVerificationRequest(string normalized)
    {
        var hasRunVerb = ContainsAny(normalized, "run", "start", "execute", "\uB3CC\uB824", "\uC2E4\uD589", "\uD574\uC918");
        var hasVerification = ContainsAny(normalized,
            "test", "build", "lint", "verify", "verification", "dotnettest", "npmtest", "npmrunbuild",
            "\uD14C\uC2A4\uD2B8", "\uBE4C\uB4DC", "\uAC80\uC99D", "\uD655\uC778");
        var asksHow = ContainsAny(normalized, "howto", "howcan", "\uBC29\uBC95", "\uC5B4\uB5BB\uAC8C");
        return hasRunVerb && hasVerification && !asksHow && !IsExplanationOnlyRequest(normalized);
    }

    private static bool IsCreateDirectoryRequest(string normalized)
    {
        return HasExecutionCreateVerb(normalized) &&
               ContainsAny(normalized, "folder", "directory", "dir", "\uD3F4\uB354", "\uB514\uB809\uD1A0\uB9AC") &&
               !IsConsultativeRequest(normalized);
    }

    private static bool IsCreateFileRequest(string normalized)
    {
        return HasExecutionCreateVerb(normalized) &&
               ContainsAny(normalized, "file", ".txt", ".md", ".json", ".cs", ".js", ".ts", ".tsx", ".jsx", "\uD30C\uC77C") &&
               !ContainsAny(normalized, "project", "\uD504\uB85C\uC81D\uD2B8") &&
               !IsConsultativeRequest(normalized);
    }

    private static bool IsModifyCodeRequest(string normalized)
    {
        var hasModifyVerb = ContainsAny(normalized,
            "fix", "edit", "modify", "update", "change", "refactor", "implement",
            "\uACE0\uCCD0", "\uC218\uC815", "\uBCC0\uACBD", "\uBC14\uAFC0", "\uAD6C\uD604");
        var hasTarget = ContainsAny(normalized,
            "code", "file", "bug", "error", "function", "class", ".cs", ".js", ".ts", ".tsx", ".jsx",
            "\uCF54\uB4DC", "\uD30C\uC77C", "\uBC84\uADF8", "\uC624\uB958", "\uD568\uC218", "\uD074\uB798\uC2A4");
        return hasModifyVerb && hasTarget && !IsConsultativeRequest(normalized);
    }

    private static bool IsSearchAndSummarizeRequest(string normalized)
    {
        var hasSearch = ContainsAny(normalized, "search", "find", "lookup", "research", "\uCC3E\uC544", "\uAC80\uC0C9", "\uC870\uC0AC");
        var hasSummary = ContainsAny(normalized, "summarize", "summary", "organize", "report", "\uC815\uB9AC", "\uC694\uC57D", "\uBCF4\uACE0");
        return hasSearch && hasSummary && !IsConsultativeRequest(normalized);
    }

    private static bool IsCreateProjectRequest(string normalized)
    {
        return HasExecutionCreateVerb(normalized) &&
               ContainsAny(normalized, "project", "app", "website", "site", "web", "webapp", "react", "vite", "wordbook", "glossary", "homepage", "portfolio", "landingpage", "blog", "shopping", "shop", "store", "mall", "commerce", "clothing", "fashion", "apparel", "\uD504\uB85C\uC81D\uD2B8", "\uC571", "\uC6F9\uC0AC\uC774\uD2B8", "\uC0AC\uC774\uD2B8", "\uC6F9", "\uD648\uD398\uC774\uC9C0", "\uD3EC\uD2B8\uD3F4\uB9AC\uC624", "\uB79C\uB529", "\uBE14\uB85C\uADF8", "\uB2E8\uC5B4\uC7A5", "\uC6A9\uC5B4\uC9D1", "\uC1FC\uD551", "\uC1FC\uD551\uBAB0", "\uC0C1\uC810", "\uC7A5\uBC14\uAD6C\uB2C8", "\uC758\uB958", "\uD328\uC158") &&
               !IsConsultativeRequest(normalized);
    }

    private static bool HasExecutionCreateVerb(string normalized)
    {
        return ContainsAny(normalized,
            "create", "make", "build", "scaffold", "generate", "write", "implement", "proceed", "continue", "goahead",
            "\uB9CC\uB4E4\uC5B4\uC918", "\uB9CC\uB4E4\uC5B4\uC8FC", "\uB9CC\uB4E4\uC790", "\uC0DD\uC131\uD574\uC918", "\uC0DD\uC131\uD574\uC8FC", "\uC791\uC131\uD574\uC918",
            "\uAD6C\uD604\uD574\uC918", "\uAD6C\uD604\uD574\uC8FC", "\uC9C4\uD589\uD574", "\uC9C4\uD589\uD574\uC918", "\uC9C4\uD589\uD574\uC8FC");
    }

    private static bool IsConsultativeRequest(string normalized)
    {
        return ContainsAny(normalized,
            "shouldi", "shouldwe", "wouldit", "isitok", "isitgood", "whatshould", "howabout",
            "possible", "isitpossible", "canwe", "cani", "howto", "howdo", "howcan", "method", "wayto",
            "\uB9CC\uB4E4\uAE4C", "\uAD1C\uCC2E\uC744\uAE4C", "\uC5B4\uB5A8\uAE4C", "\uC5B4\uB5A4\uBC29\uD5A5", "\uC88B\uC744\uAE4C", "\uC5B4\uB5BB\uAC8C\uD560\uAE4C",
            "\uAC00\uB2A5\uD55C\uAC00", "\uAC00\uB2A5\uD560\uAE4C", "\uC218\uC788\uB294\uC9C0", "\uC218\uC788\uC744\uAE4C",
            "\uBC29\uBC95", "\uD558\uB294\uBC95", "\uC5B4\uB5BB\uAC8C");
    }

    private static bool IsExplanationOnlyRequest(string normalized)
    {
        return ContainsAny(
            normalized,
            "explain", "describe", "tellme", "meaning", "whatdoes", "whatis", "difference",
            "\uC124\uBA85", "\uC54C\uB824", "\uBB50\uC57C", "\uBB34\uC5C7", "\uB73B", "\uC758\uBBF8", "\uCC28\uC774");
    }

    private static string ExtractRequestedTarget(string userText, params string[] markers)
    {
        if (string.IsNullOrWhiteSpace(userText))
        {
            return string.Empty;
        }

        foreach (var marker in markers)
        {
            var searchIndex = 0;
            while (searchIndex < userText.Length)
            {
                var markerIndex = userText.IndexOf(marker, searchIndex, StringComparison.OrdinalIgnoreCase);
                if (markerIndex <= 0)
                {
                    break;
                }

                searchIndex = markerIndex + marker.Length;
                var beforeMarker = userText[..markerIndex].Trim();
                if (string.IsNullOrWhiteSpace(beforeMarker))
                {
                    continue;
                }

                var tokens = beforeMarker
                    .Split([' ', '\t', '\r', '\n', '"', '\'', '`'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (tokens.Length == 0)
                {
                    continue;
                }

                for (var i = tokens.Length - 1; i >= 0; i--)
                {
                    var candidate = TrimTargetToken(tokens[i]);
                    if (LooksLikeTarget(candidate) && !LooksLikeDeicticOrFillerTarget(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        return string.Empty;
    }

    private static string TrimTargetToken(string token)
    {
        var trimmed = token.Trim().Trim(',', '.', ':', ';', '!', '?', '(', ')', '[', ']', '{', '}', '<', '>');
        foreach (var suffix in new[] { "\uC774\uB77C\uB294", "\uB77C\uB294", "\uC774\uB77C\uACE0", "\uB77C\uACE0", "\uB780", "\uC744", "\uB97C", "\uC5D0", "\uB85C" })
        {
            if (trimmed.Length > suffix.Length &&
                trimmed.EndsWith(suffix, StringComparison.Ordinal))
            {
                return trimmed[..^suffix.Length];
            }
        }

        return trimmed;
    }

    private static bool LooksLikeTarget(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 120)
        {
            return false;
        }

        return token.Any(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-' or '/' or '\\');
    }

    private static bool LooksLikeDeicticOrFillerTarget(string token)
    {
        var normalized = Normalize(token);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        var exactFiller = new[]
        {
            "this", "that", "here", "there", "current", "workspace", "named", "called", "name", "new", "empty",
            "thisfolder", "currentfolder", "thisfile", "currentfile",
            "\uC774", "\uADF8", "\uC800", "\uC5EC\uAE30", "\uD604\uC7AC", "\uD574\uB2F9", "\uC774\uB984", "\uC0C8", "\uC0C8\uB85C\uC6B4", "\uBE48",
            "\uB77C\uB294", "\uC774\uB77C\uB294", "\uB77C\uACE0", "\uC774\uB77C\uACE0", "\uB780", "\uD558\uB098"
        };
        if (exactFiller.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalized.StartsWith("folder", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("directory", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("file", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("path", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("\uD3F4\uB354", StringComparison.Ordinal) ||
               normalized.StartsWith("\uD30C\uC77C", StringComparison.Ordinal) ||
               normalized.StartsWith("\uB514\uB809\uD1A0\uB9AC", StringComparison.Ordinal) ||
               normalized.StartsWith("\uACBD\uB85C", StringComparison.Ordinal);
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
        AppendList(builder, "Required completion evidence", BuildRequiredEvidence(contract.Intent));
        builder.AppendLine("- Keep this contract as the completion target even after inspecting files.");
        builder.AppendLine("- Do not produce a final success answer until the required evidence exists in this run. If the target is unclear or a safety policy blocks the action, ask one focused question or report the concrete policy/tool result.");
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
        TaskContractIntent.DeletePath => "delete_path",
        TaskContractIntent.CreateDirectory => "create_directory",
        TaskContractIntent.CreateFile => "create_file",
        TaskContractIntent.CreateProject => "create_project",
        TaskContractIntent.ModifyCode => "modify_code",
        TaskContractIntent.RunVerification => "run_verification",
        TaskContractIntent.SearchAndSummarize => "search_and_summarize",
        TaskContractIntent.InspectProject => "inspect_project",
        TaskContractIntent.ExplainOrChat => "explain_or_chat",
        _ => "none"
    };

    private static IReadOnlyList<string> BuildRequiredEvidence(TaskContractIntent intent) => intent switch
    {
        TaskContractIntent.RunLocalServer =>
        [
            "executed command evidence for starting the server",
            "a reachable localhost/127.0.0.1 URL or a concrete startup error"
        ],
        TaskContractIntent.StopLocalServer =>
        [
            "local server stop result from the Desktop local server service"
        ],
        TaskContractIntent.DeletePath =>
        [
            "delete_path tool result for the explicit target"
        ],
        TaskContractIntent.CreateDirectory =>
        [
            "create_directory tool result for the requested folder"
        ],
        TaskContractIntent.CreateFile =>
        [
            "write_file tool result for the requested file"
        ],
        TaskContractIntent.CreateProject =>
        [
            "scaffold or file-creation tool results",
            "created file list and verification result when available"
        ],
        TaskContractIntent.ModifyCode =>
        [
            "read/search evidence for the target file",
            "workspace edit tool result or recorded file change",
            "focused verification result when useful"
        ],
        TaskContractIntent.RunVerification =>
        [
            "executed build/test/lint command",
            "pass/fail/exit-code result or concrete command error"
        ],
        TaskContractIntent.SearchAndSummarize =>
        [
            "web_search/search/read/fetch evidence from the requested source",
            "summary grounded in gathered evidence",
            "or a clear limitation report when no web/search/fetch source is available"
        ],
        _ => []
    };
}

public static class TaskContractCompletionChecker
{
    public static bool ShouldRetry(TaskContract contract, string assistantText, IReadOnlyList<string> executedCommands, AgentWorkMode workMode)
        => ShouldRetry(contract, assistantText, executedCommands, workMode, []);

    public static bool ShouldRetry(
        TaskContract contract,
        string assistantText,
        IReadOnlyList<string> executedCommands,
        AgentWorkMode workMode,
        IReadOnlyList<ToolReplayEntry> replayEntries)
    {
        if (workMode == AgentWorkMode.Readonly || !contract.IsActionable)
        {
            return false;
        }

        if (MissingRequiredEvidence(contract, executedCommands, replayEntries) &&
            !HasAcceptableNoEvidenceCompletion(contract, assistantText, replayEntries))
        {
            return true;
        }

        return contract.Intent switch
        {
            TaskContractIntent.RunLocalServer => executedCommands.Count == 0 && LooksLikeInvalidRunServerCompletion(assistantText),
            TaskContractIntent.StopLocalServer => false,
            TaskContractIntent.DeletePath => LooksLikeInvalidDeletePathCompletion(assistantText),
            TaskContractIntent.CreateDirectory => LooksLikeInvalidToolActionCompletion(assistantText, "create_directory", "\uD3F4\uB354"),
            TaskContractIntent.CreateFile => LooksLikeInvalidToolActionCompletion(assistantText, "write_file", "\uD30C\uC77C"),
            TaskContractIntent.CreateProject => LooksLikeInvalidMutationCompletion(assistantText),
            TaskContractIntent.ModifyCode => LooksLikeInvalidMutationCompletion(assistantText),
            TaskContractIntent.RunVerification => executedCommands.Count == 0 && LooksLikeInvalidVerificationCompletion(assistantText),
            TaskContractIntent.SearchAndSummarize => LooksLikeInvalidSearchCompletion(assistantText),
            _ => false
        };
    }

    public static bool ShouldReject(TaskContract contract, string assistantText, IReadOnlyList<string> executedCommands, AgentWorkMode workMode)
        => ShouldReject(contract, assistantText, executedCommands, workMode, []);

    public static bool ShouldReject(
        TaskContract contract,
        string assistantText,
        IReadOnlyList<string> executedCommands,
        AgentWorkMode workMode,
        IReadOnlyList<ToolReplayEntry> replayEntries)
    {
        if (workMode == AgentWorkMode.Readonly || !contract.IsActionable)
        {
            return false;
        }

        if (MissingRequiredEvidence(contract, executedCommands, replayEntries) &&
            !HasAcceptableNoEvidenceCompletion(contract, assistantText, replayEntries))
        {
            return true;
        }

        return contract.Intent switch
        {
            TaskContractIntent.RunLocalServer => executedCommands.Count == 0 && LooksLikeInvalidRunServerCompletion(assistantText),
            TaskContractIntent.StopLocalServer => false,
            TaskContractIntent.DeletePath => LooksLikeInvalidDeletePathCompletion(assistantText),
            TaskContractIntent.CreateDirectory => LooksLikeInvalidToolActionCompletion(assistantText, "create_directory", "\uD3F4\uB354"),
            TaskContractIntent.CreateFile => LooksLikeInvalidToolActionCompletion(assistantText, "write_file", "\uD30C\uC77C"),
            TaskContractIntent.CreateProject => LooksLikeInvalidMutationCompletion(assistantText),
            TaskContractIntent.ModifyCode => LooksLikeInvalidMutationCompletion(assistantText),
            TaskContractIntent.RunVerification => executedCommands.Count == 0 && LooksLikeInvalidVerificationCompletion(assistantText),
            TaskContractIntent.SearchAndSummarize => LooksLikeInvalidSearchCompletion(assistantText),
            _ => false
        };
    }

    public static string BuildRetryInstruction(TaskContract contract)
    {
        string WithGoal(string instruction)
        {
            var goal = string.IsNullOrWhiteSpace(contract.Goal)
                ? "current user request"
                : DesktopPromptBuilder.Truncate(contract.Goal.ReplaceLineEndings(" "), 500);
            return $"Current user task goal: {goal}. {instruction}";
        }

        if (contract.Intent == TaskContractIntent.RunLocalServer)
        {
            return WithGoal(
                "The current task contract is run_local_server. Do not complete with a project structure summary. " +
                "Inspect package scripts if needed, start the local development server with the appropriate command, " +
                "verify the reachable localhost URL, then report that URL. If startup fails, report the concrete command error.");
        }

        if (contract.Intent == TaskContractIntent.StopLocalServer)
        {
            return WithGoal(
                "The current task contract is stop_local_server. Do not complete with a project structure summary. " +
                "Find the active local server session for this workspace, stop it if present, then report the result.");
        }

        if (contract.Intent == TaskContractIntent.DeletePath)
        {
            return WithGoal(
                "The current task contract is delete_path. Do not describe AgentQ and do not repeat directory listings. " +
                "Identify the explicit target from the user's request, call delete_path for that target, then report the result. " +
                "If the target is missing or unsafe, report that concrete delete_path result.");
        }

        if (contract.Intent == TaskContractIntent.CreateDirectory)
        {
            return WithGoal(
                "The current task contract is create_directory. Do not only say the folder can be created. " +
                "Identify the requested workspace-relative folder path, call create_directory now, then report the created path or concrete error.");
        }

        if (contract.Intent == TaskContractIntent.CreateFile)
        {
            return WithGoal(
                "The current task contract is create_file. Do not only show file contents in prose. " +
                "Identify the requested workspace-relative file path, call write_file now, then report the created path or concrete error.");
        }

        if (contract.Intent == TaskContractIntent.CreateProject)
        {
            return WithGoal(
                "The current task contract is create_project. Do not only describe the project. " +
                "Use scaffold or workspace file creation tools now, honor the requested stack, then report created files and verification.");
        }

        if (contract.Intent == TaskContractIntent.ModifyCode)
        {
            return WithGoal(
                "The current task contract is modify_code. Do not only explain the change. " +
                "Inspect/search the target file, apply the edit with workspace mutation tools, run focused verification when useful, then summarize the changed file.");
        }

        if (contract.Intent == TaskContractIntent.RunVerification)
        {
            return WithGoal(
                "The current task contract is run_verification. Do not only explain how to run verification. " +
                "Run the requested build/test/lint command or the focused inferred verification command now, then report pass, fail, or the concrete command error.");
        }

        if (contract.Intent == TaskContractIntent.SearchAndSummarize)
        {
            return WithGoal(
                "The current task contract is search_and_summarize. Do not answer from guesses. " +
                "Use web_search or other available search/read/fetch tools to gather evidence first, then summarize the findings and limits. " +
                "If no web search tool or source URL is available, say that concrete limitation instead of inventing findings.");
        }

        return WithGoal("The previous answer did not satisfy the current task contract. Re-plan from the contract goal and perform the required actions before answering.");
    }

    private static bool LooksLikeInvalidRunServerCompletion(string assistantText)
    {
        var text = assistantText.ToLowerInvariant();
        var mentionsStructure = ContainsAny(text,
            "project structure",
            "\uD504\uB85C\uC81D\uD2B8 \uAD6C\uC870",
            "src/app.jsx",
            "src/main.jsx",
            "vite.config",
            "\uD604\uC7AC \uAD6C\uC131",
            "main component",
            "entry point");
        var asksNext = ContainsAny(text,
            "what would you like",
            "what can i help",
            "\uC5B4\uB5A4 \uC791\uC5C5",
            "\uBB34\uC5C7\uC744 \uB3C4\uC640");
        var reportsUrl = ContainsAny(text, "http://localhost:", "http://127.0.0.1:");
        var reportsFailure = ContainsAny(text, "failed", "error", "\uC2E4\uD328", "\uC624\uB958");
        return !reportsUrl && !reportsFailure && (mentionsStructure || asksNext);
    }

    private static bool LooksLikeInvalidDeletePathCompletion(string assistantText)
    {
        var text = assistantText.ToLowerInvariant();
        var reportsDelete = ContainsAny(text,
            "deleted", "removed", "delete_path", "permission denied", "not found",
            "\uC0AD\uC81C\uD588", "\uC0AD\uC81C\uB418", "\uC9C0\uC6E0", "\uAC70\uBD80",
            "\uCC3E\uC744 \uC218 \uC5C6", "\uC874\uC7AC\uD558\uC9C0 \uC54A", "\uB300\uC0C1\uC774 \uC5C6", "\uACBD\uB85C\uAC00 \uC5C6");
        var describesAgent = ContainsAny(text, "agentq desktop", "agentq\uB294", "agentq desktop\uC740");
        var asksNext = ContainsAny(text, "what would you like", "what can i help", "\uBB34\uC5C7\uC744 \uB3C4\uC640", "\uAD81\uAE08\uD558\uC2E0 \uC810");
        return !reportsDelete && (describesAgent || asksNext || text.Length > 0);
    }

    private static bool LooksLikeInvalidToolActionCompletion(string assistantText, string successMarker, string localizedTarget)
    {
        var text = assistantText.ToLowerInvariant();
        var reportsTool = ContainsAny(text, successMarker, "created", "success", "\uC0DD\uC131\uD588", "\uB9CC\uB4E4\uC5C8", localizedTarget + "\uB97C \uC0DD\uC131");
        var onlyPromises = ContainsAny(text,
            "can create", "will create", "i'll create", "i can make", "steps",
            "\uC0DD\uC131\uD560 \uC218", "\uB9CC\uB4E4 \uC218", "\uB9CC\uB4E4\uACA0", "\uD558\uACA0\uC2B5\uB2C8\uB2E4");
        var asksNext = ContainsAny(text, "what would you like", "what can i help", "\uBB34\uC5C7\uC744 \uB3C4\uC640");
        return !reportsTool && (onlyPromises || asksNext || text.Length > 0);
    }

    private static bool LooksLikeInvalidMutationCompletion(string assistantText)
    {
        var text = assistantText.ToLowerInvariant();
        var reportsMutation = ContainsAny(text,
            "created", "modified", "updated", "changed", "edited", "write_file", "edit_file", "create_project_scaffold",
            "\uC0DD\uC131\uD588", "\uC218\uC815\uD588", "\uBCC0\uACBD\uD588", "\uC791\uC131\uD588");
        var onlyExplains = ContainsAny(text,
            "can create", "will create", "i can", "would", "steps", "plan",
            "\uAC00\uB2A5\uD569\uB2C8\uB2E4", "\uD560 \uC218", "\uD558\uACA0\uC2B5\uB2C8\uB2E4", "\uACC4\uD68D");
        return !reportsMutation && (onlyExplains || text.Length > 0);
    }

    private static bool LooksLikeInvalidVerificationCompletion(string assistantText)
    {
        var text = assistantText.ToLowerInvariant();
        var reportsResult = ContainsAny(text,
            "passed", "failed", "exit code", "error", "build succeeded", "test result",
            "\uD1B5\uACFC", "\uC2E4\uD328", "\uC624\uB958", "\uACB0\uACFC");
        var onlyExplains = ContainsAny(text,
            "run this", "you can run", "how to run", "command is",
            "\uC2E4\uD589\uD558\uBA74", "\uB3CC\uB9AC\uBA74", "\uBA85\uB839\uC740", "\uBC29\uBC95");
        return !reportsResult && (onlyExplains || text.Length > 0);
    }

    private static bool LooksLikeInvalidSearchCompletion(string assistantText)
    {
        var text = assistantText.ToLowerInvariant();
        var reportsEvidence = ContainsAny(text,
            "source", "according", "found", "searched", "read", "http://", "https://",
            "\uCC3E\uC544", "\uAC80\uC0C9", "\uC870\uC0AC", "\uCD9C\uCC98", "\uADFC\uAC70");
        var guesses = ContainsAny(text,
            "probably", "likely", "generally", "i think", "\uC77C\uBC18\uC801", "\uC544\uB9C8", "\uC0DD\uAC01");
        return !reportsEvidence && (guesses || text.Length > 0);
    }

    private static bool MissingRequiredEvidence(
        TaskContract contract,
        IReadOnlyList<string> executedCommands,
        IReadOnlyList<ToolReplayEntry> replayEntries)
    {
        if (!contract.IsActionable)
        {
            return false;
        }

        return contract.Intent switch
        {
            TaskContractIntent.RunLocalServer => executedCommands.Count == 0 && !HasAnyTool(replayEntries, "bash"),
            TaskContractIntent.StopLocalServer => false,
            TaskContractIntent.DeletePath => !HasAnyTool(replayEntries, "delete_path"),
            TaskContractIntent.CreateDirectory => !HasAnyTool(replayEntries, "create_directory"),
            TaskContractIntent.CreateFile => !HasAnyTool(replayEntries, "write_file"),
            TaskContractIntent.CreateProject => !HasAnyTool(replayEntries, "create_project_scaffold", "write_file"),
            TaskContractIntent.ModifyCode => !HasAnyTool(replayEntries, "edit_file", "write_file"),
            TaskContractIntent.InspectProject => !HasAnyTool(replayEntries, "read_file", "list_directory", "grep_search", "glob_search", "hybrid_search", "symbol_search", "semantic_search"),
            TaskContractIntent.RunVerification => executedCommands.Count == 0 && !HasAnyTool(replayEntries, "bash"),
            TaskContractIntent.SearchAndSummarize => !HasAnyTool(replayEntries, "web_search", "fetch_url", "read_file", "grep_search", "hybrid_search", "semantic_search"),
            _ => false
        };
    }

    private static bool HasAcceptableNoEvidenceCompletion(
        TaskContract contract,
        string assistantText,
        IReadOnlyList<ToolReplayEntry> replayEntries)
    {
        if (LooksLikeClarification(assistantText))
        {
            return true;
        }

        if (!LooksLikeConcreteLimitation(assistantText))
        {
            return false;
        }

        return !RequiresReplayBackedLimitation(contract.Intent) || HasFailedToolEvidence(replayEntries);
    }

    private static bool HasAnyTool(IReadOnlyList<ToolReplayEntry> replayEntries, params string[] toolNames)
    {
        if (replayEntries.Count == 0)
        {
            return false;
        }

        return replayEntries.Any(entry =>
            entry.IsError != true &&
            toolNames.Any(toolName => string.Equals(entry.ToolName, toolName, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool HasFailedToolEvidence(IReadOnlyList<ToolReplayEntry> replayEntries) =>
        replayEntries.Any(entry => entry.IsError);

    private static bool RequiresReplayBackedLimitation(TaskContractIntent intent) =>
        intent is TaskContractIntent.RunLocalServer
            or TaskContractIntent.DeletePath
            or TaskContractIntent.CreateDirectory
            or TaskContractIntent.CreateFile
            or TaskContractIntent.CreateProject
            or TaskContractIntent.ModifyCode
            or TaskContractIntent.RunVerification
            or TaskContractIntent.SearchAndSummarize;

    private static bool LooksLikeClarificationOrConcreteLimitation(string assistantText)
    {
        return LooksLikeClarification(assistantText) || LooksLikeConcreteLimitation(assistantText);
    }

    private static bool LooksLikeClarification(string assistantText)
    {
        if (string.IsNullOrWhiteSpace(assistantText))
        {
            return false;
        }

        var text = assistantText.ToLowerInvariant();
        if (LooksLikeCompletionClaim(text))
        {
            return false;
        }

        return text.Contains('?') ||
            ContainsAny(
                text,
                "clarify",
                "\uBA85\uD655",
                "\uC9C8\uBB38");
    }

    private static bool LooksLikeConcreteLimitation(string assistantText)
    {
        if (string.IsNullOrWhiteSpace(assistantText))
        {
            return false;
        }

        var text = assistantText.ToLowerInvariant();
        var reportsConcreteLimitation = ContainsAny(
            text,
            "permission denied",
            "blocked",
            "policy",
            "not found",
            "failed",
            "error",
            "no web search",
            "no search tool",
            "source url",
            "cannot access",
            "\uAD8C\uD55C",
            "\uAC70\uBD80",
            "\uCC28\uB2E8",
            "\uCC3E\uC744 \uC218 \uC5C6",
            "\uC874\uC7AC\uD558\uC9C0 \uC54A",
            "\uB300\uC0C1\uC774 \uC5C6",
            "\uACBD\uB85C\uAC00 \uC5C6",
            "\uC2E4\uD328",
            "\uC624\uB958");
        var claimsCompletion = ContainsAny(
            text,
            "created",
            "deleted",
            "modified",
            "updated",
            "completed",
            "done",
            "success",
            "\uC0DD\uC131\uD588",
            "\uB9CC\uB4E4\uC5C8",
            "\uC0AD\uC81C\uD588",
            "\uC218\uC815\uD588",
            "\uBCC0\uACBD\uD588",
            "\uC644\uB8CC",
            "\uC131\uACF5");
        if (claimsCompletion && !reportsConcreteLimitation)
        {
            return false;
        }

        return reportsConcreteLimitation;
    }

    private static bool LooksLikeCompletionClaim(string text) =>
        ContainsAny(
            text,
            "created",
            "deleted",
            "modified",
            "updated",
            "completed",
            "done",
            "success",
            "\uC0DD\uC131\uD588",
            "\uB9CC\uB4E4\uC5C8",
            "\uC0AD\uC81C\uD588",
            "\uC218\uC815\uD588",
            "\uBCC0\uACBD\uD588",
            "\uC644\uB8CC",
            "\uC131\uACF5");

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
}
