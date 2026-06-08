using System.Text;
using System.Text.Json;

namespace AgentQ.Desktop.Services;

public enum TurnIntentType
{
    Conversation,
    Action,
    Hybrid,
    Ambiguous
}

public sealed record TurnIntentClassification
{
    public TurnIntentType Type { get; init; }

    public double Confidence { get; init; }

    public string Rationale { get; init; } = string.Empty;

    public string ActionKind { get; init; } = string.Empty;

    public bool RequiresWrite { get; init; }

    public bool RequiresShell { get; init; }

    public bool RequiresNetwork { get; init; }

    public bool IsConcreteEnough { get; init; }

    public string ClarifyingQuestion { get; init; } = string.Empty;

    public bool AllowsDeterministicExecution =>
        Type is TurnIntentType.Action or TurnIntentType.Hybrid && IsConcreteEnough;
}

public static class TurnIntentClassifier
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    public static TurnIntentClassification Classify(string userText)
    {
        var normalized = Normalize(userText);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Conversation("Empty user turn.", confidence: 0.9);
        }

        var asksForHowTo = HasHowToSignal(normalized);
        var asksAdvice = HasAdviceSignal(normalized);
        var asksInfo = HasInformationSignal(normalized);
        var action = DetectAction(normalized);
        var hasAction = !string.IsNullOrWhiteSpace(action);
        var concrete = IsConcreteEnoughForAction(normalized, action);

        if (HasHybridSignal(normalized) && hasAction && concrete)
        {
            return new TurnIntentClassification
            {
                Type = TurnIntentType.Hybrid,
                Confidence = 0.86,
                Rationale = "The turn asks AgentQ to perform an action and then summarize, explain, or organize the result.",
                ActionKind = action,
                RequiresWrite = RequiresWrite(action),
                RequiresShell = RequiresShell(action),
                RequiresNetwork = RequiresNetwork(normalized, action),
                IsConcreteEnough = true
            };
        }

        if ((asksAdvice || asksInfo || asksForHowTo) &&
            (!hasAction || asksForHowTo || HasConsultativeActionSignal(normalized)))
        {
            return Conversation(
                "The turn asks for explanation, advice, feasibility, comparison, or design discussion rather than immediate execution.",
                confidence: 0.82);
        }

        if (!hasAction)
        {
            return Conversation("No concrete local action request was detected.", confidence: 0.78);
        }

        if (!concrete)
        {
            return new TurnIntentClassification
            {
                Type = TurnIntentType.Ambiguous,
                Confidence = 0.84,
                Rationale = "The turn contains an action verb, but the target or desired output is not concrete enough to execute safely.",
                ActionKind = action,
                RequiresWrite = RequiresWrite(action),
                RequiresShell = RequiresShell(action),
                RequiresNetwork = RequiresNetwork(normalized, action),
                IsConcreteEnough = false,
                ClarifyingQuestion = BuildClarifyingQuestion(action)
            };
        }

        return new TurnIntentClassification
        {
            Type = TurnIntentType.Action,
            Confidence = 0.86,
            Rationale = "The turn contains a concrete execution request.",
            ActionKind = action,
            RequiresWrite = RequiresWrite(action),
            RequiresShell = RequiresShell(action),
            RequiresNetwork = RequiresNetwork(normalized, action),
            IsConcreteEnough = true
        };
    }

    public static string BuildRuleDebugDetail(string userText, TurnIntentClassification classification)
    {
        var normalized = Normalize(userText);
        var action = DetectAction(normalized);
        var hasAction = !string.IsNullOrWhiteSpace(action);
        return
            $"normalized=\"{Truncate(normalized, 180)}\"; " +
            $"signals: info={HasInformationSignal(normalized)}, advice={HasAdviceSignal(normalized)}, howTo={HasHowToSignal(normalized)}, consultativeAction={HasConsultativeActionSignal(normalized)}, hybrid={HasHybridSignal(normalized)}; " +
            $"detectedAction={(hasAction ? action : "none")}; concrete={IsConcreteEnoughForAction(normalized, action)}; " +
            $"ruleResult={classification.Type} confidence={classification.Confidence:0.00} action={(string.IsNullOrWhiteSpace(classification.ActionKind) ? "none" : classification.ActionKind)} concrete={classification.IsConcreteEnough}; " +
            $"clarifyingQuestion=\"{Truncate(classification.ClarifyingQuestion.ReplaceLineEndings(" "), 220)}\"";
    }

    public static bool IsStateChangingTool(string toolName)
    {
        return toolName is
            "write_file" or
            "edit_file" or
            "bash" or
            "create_project_scaffold" or
            "verify_project_scaffold" or
            "run_local_server" or
            "stop_local_server";
    }

    public static bool ShouldUseModelPrimary(TurnIntentClassification ruleClassification)
    {
        _ = ruleClassification;
        return true;
    }

    public static TurnIntentClassification ApplySafetyRules(
        TurnIntentClassification ruleClassification,
        TurnIntentClassification modelClassification)
    {
        if (modelClassification.Type == TurnIntentType.Action &&
            ruleClassification.Type == TurnIntentType.Conversation &&
            modelClassification.Confidence < 0.92)
        {
            return ruleClassification with
            {
                Rationale = ruleClassification.Rationale + " Model-first classification attempted to promote the turn to Action, but confidence was below the safety threshold."
            };
        }

        if (ruleClassification.Type == TurnIntentType.Ambiguous &&
            modelClassification.Type is TurnIntentType.Action or TurnIntentType.Hybrid &&
            (!modelClassification.IsConcreteEnough || modelClassification.Confidence < 0.9))
        {
            return ruleClassification with
            {
                Rationale = ruleClassification.Rationale + " Model-first classification did not provide a high-confidence concrete target."
            };
        }

        if (modelClassification.Type is TurnIntentType.Action or TurnIntentType.Hybrid &&
            !modelClassification.IsConcreteEnough)
        {
            return ruleClassification.Type == TurnIntentType.Ambiguous
                ? ruleClassification
                : modelClassification with
                {
                    Type = TurnIntentType.Ambiguous,
                    Confidence = Math.Min(modelClassification.Confidence, 0.84),
                    IsConcreteEnough = false,
                    ClarifyingQuestion = string.IsNullOrWhiteSpace(modelClassification.ClarifyingQuestion)
                        ? BuildClarifyingQuestion(modelClassification.ActionKind)
                        : modelClassification.ClarifyingQuestion,
                    Rationale = $"Model-first classification requested execution, but the target was not concrete enough. {modelClassification.Rationale}"
                };
        }

        return modelClassification with
        {
            Rationale = $"LLM primary intent classifier: {modelClassification.Rationale} Rule safety pass was {ruleClassification.Type}: {ruleClassification.Rationale}"
        };
    }

    public static TurnIntentClassification BuildModelUnavailableFallback(
        TurnIntentClassification ruleClassification,
        string reason)
    {
        if (ruleClassification.Type == TurnIntentType.Conversation)
        {
            return ruleClassification with
            {
                Rationale = $"{ruleClassification.Rationale} {reason}"
            };
        }

        return new TurnIntentClassification
        {
            Type = TurnIntentType.Ambiguous,
            Confidence = Math.Min(ruleClassification.Confidence, 0.74),
            Rationale = $"{reason} Rule safety pass was {ruleClassification.Type}, but AgentQ does not allow a model classification failure to become an execution decision.",
            ActionKind = ruleClassification.ActionKind,
            RequiresWrite = ruleClassification.RequiresWrite,
            RequiresShell = ruleClassification.RequiresShell,
            RequiresNetwork = ruleClassification.RequiresNetwork,
            IsConcreteEnough = false,
            ClarifyingQuestion = string.IsNullOrWhiteSpace(ruleClassification.ClarifyingQuestion)
                ? BuildClarifyingQuestion(ruleClassification.ActionKind)
                : ruleClassification.ClarifyingQuestion
        };
    }

    public static bool TryParseModelResponse(
        string responseText,
        TurnIntentClassification ruleClassification,
        out TurnIntentClassification classification)
    {
        classification = ruleClassification;
        var json = ExtractJsonObject(responseText);
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var model = JsonSerializer.Deserialize<ModelIntentResponse>(json, JsonOptions);
            if (model == null ||
                string.IsNullOrWhiteSpace(model.Type) ||
                !Enum.TryParse<TurnIntentType>(model.Type, ignoreCase: true, out var type))
            {
                return false;
            }

            var actionKind = string.IsNullOrWhiteSpace(model.ActionKind)
                ? ruleClassification.ActionKind
                : model.ActionKind.Trim().ToLowerInvariant();
            var confidence = Math.Clamp(model.Confidence <= 0 ? 0.75 : model.Confidence, 0, 1);
            var isConcreteEnough = model.IsConcreteEnough ?? (type is TurnIntentType.Action or TurnIntentType.Hybrid);
            var requiresWrite = model.RequiresWrite ?? RequiresWrite(actionKind);
            var requiresShell = model.RequiresShell ?? RequiresShell(actionKind);
            var requiresNetwork = model.RequiresNetwork ?? ruleClassification.RequiresNetwork;

            classification = new TurnIntentClassification
            {
                Type = type,
                Confidence = confidence,
                Rationale = string.IsNullOrWhiteSpace(model.Rationale)
                    ? "Model returned a structured intent classification."
                    : model.Rationale.Trim(),
                ActionKind = actionKind,
                RequiresWrite = requiresWrite,
                RequiresShell = requiresShell,
                RequiresNetwork = requiresNetwork,
                IsConcreteEnough = isConcreteEnough,
                ClarifyingQuestion = string.IsNullOrWhiteSpace(model.ClarifyingQuestion)
                    ? ruleClassification.ClarifyingQuestion
                    : model.ClarifyingQuestion.Trim()
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static TurnIntentClassification Conversation(string rationale, double confidence) => new()
    {
        Type = TurnIntentType.Conversation,
        Confidence = confidence,
        Rationale = rationale,
        IsConcreteEnough = false
    };

    private static string DetectAction(string normalized)
    {
        if (ContainsAny(normalized, "delete", "remove", "erase", "\uC0AD\uC81C", "\uC9C0\uC6CC"))
        {
            return "delete";
        }

        if (ContainsAny(normalized, "commit", "push", "\uCEE4\uBC0B", "\uD478\uC2DC"))
        {
            return "git";
        }

        if (ContainsAny(normalized, "run", "start", "execute", "build", "test", "install", "npmrundev",
                "\uC2E4\uD589", "\uBE4C\uB4DC", "\uD14C\uC2A4\uD2B8", "\uB3CC\uB824", "\uB744\uC6CC", "\uC124\uCE58"))
        {
            return "shell";
        }

        if (ContainsAny(normalized, "fix", "modify", "change", "refactor", "edit",
                "\uC218\uC815", "\uACE0\uCCD0", "\uBCC0\uACBD", "\uB9AC\uD329\uD130\uB9C1", "\uBC14\uAFD4"))
        {
            return "edit";
        }

        if (ContainsAny(normalized, "create", "make", "generate", "scaffold", "implement", "write", "draw",
                "\uB9CC\uB4E4", "\uC0DD\uC131", "\uC791\uC131", "\uAD6C\uD604", "\uCD94\uAC00", "\uADF8\uB824"))
        {
            return "create";
        }

        if (ContainsAny(normalized, "find", "search", "lookup", "research",
                "\uCC3E\uC544", "\uAC80\uC0C9", "\uC870\uC0AC", "\uC870\uD68C"))
        {
            return "search";
        }

        if (ContainsAny(normalized, "open", "read", "save", "copy", "move",
                "\uC5F4\uC5B4", "\uC77D\uC5B4", "\uC800\uC7A5", "\uBCF5\uC0AC", "\uC774\uB3D9"))
        {
            return "file";
        }

        return string.Empty;
    }

    private static bool IsConcreteEnoughForAction(string normalized, string action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return false;
        }

        if (action == "create" &&
            ContainsAny(normalized,
                "newproject", "newapp", "\uC0C8\uD504\uB85C\uC81D\uD2B8", "\uC0C8\uB85C\uC6B4\uD504\uB85C\uC81D\uD2B8", "\uC0C8\uC571") &&
            !HasConcreteProjectTarget(normalized))
        {
            return false;
        }

        if (action is "edit" or "delete" or "file" &&
            !ContainsAny(normalized,
                "file", "code", "ui", "readme", "button", "this", "current",
                "\uD30C\uC77C", "\uCF54\uB4DC", "\uBC84\uD2BC", "\uD604\uC7AC", "\uC774\uAC70"))
        {
            return false;
        }

        return true;
    }

    private static bool HasConcreteProjectTarget(string normalized)
    {
        return ContainsAny(normalized,
            "react", "vite", "nextjs", "next", "vue", "svelte", "angular",
            "portfolio", "homepage", "website", "site", "landingpage", "webpage", "webapp",
            "api", "dashboard", "stock", "stocks", "blog", "wordbook", "glossary", "shopping",
            "python", "fastapi", "rust", "go", "java", "typescript", "javascript",
            "\uB9AC\uC561\uD2B8", "\uBE44\uD2B8", "\uB125\uC2A4\uD2B8", "\uBDF0", "\uC2A4\uBCA8\uD2B8",
            "\uD3EC\uD2B8\uD3F4\uB9AC\uC624", "\uD648\uD398\uC774\uC9C0", "\uC6F9\uC0AC\uC774\uD2B8", "\uC0AC\uC774\uD2B8", "\uC6F9", "\uB79C\uB529",
            "\uC8FC\uC2DD", "\uBD84\uC11D", "\uB300\uC2DC\uBCF4\uB4DC", "\uBE14\uB85C\uADF8", "\uB2E8\uC5B4\uC7A5", "\uC6A9\uC5B4\uC9D1",
            "\uD30C\uC774\uC36C", "\uB7EC\uC2A4\uD2B8", "\uC790\uBC14", "\uD0C0\uC785\uC2A4\uD06C\uB9BD\uD2B8", "\uC790\uBC14\uC2A4\uD06C\uB9BD\uD2B8");
    }

    private static bool HasHybridSignal(string normalized)
    {
        return ContainsAny(normalized,
            "andexplain", "andsummarize", "thenexplain", "thensummarize",
            "\uD558\uACE0\uC774\uC720", "\uD558\uACE0\uC124\uBA85", "\uD558\uACE0\uC815\uB9AC", "\uCC3E\uC544\uC11C\uC815\uB9AC", "\uC218\uC815\uD558\uACE0\uC774\uC720");
    }

    private static bool HasInformationSignal(string normalized)
    {
        return ContainsAny(normalized,
            "what", "why", "explain", "describe", "tellme", "summarize", "meaning", "difference", "principle",
            "\uBB50\uC57C", "\uBB54\uAC00", "\uBB34\uC5C7", "\uC124\uBA85", "\uC54C\uB824", "\uC18C\uAC1C", "\uC815\uB9AC", "\uC694\uC57D", "\uC758\uBBF8", "\uCC28\uC774", "\uC65C", "\uC6D0\uB9AC");
    }

    private static bool HasAdviceSignal(string normalized)
    {
        return ContainsAny(normalized,
            "howis", "howabout", "ok", "good", "recommend", "better", "opinion", "feedback", "review", "analyze", "evaluate", "shouldi",
            "\uC5B4\uB584", "\uC5B4\uB54C", "\uAD1C\uCC2E", "\uC88B\uC744\uAE4C", "\uBCC4\uB85C", "\uCD94\uCC9C", "\uB098\uC544", "\uC758\uACAC", "\uD53C\uB4DC\uBC31", "\uB9AC\uBDF0", "\uBD84\uC11D", "\uD3C9\uAC00", "\uD574\uC57C\uD560\uAE4C", "\uB9D0\uC544\uC57C\uD560\uAE4C");
    }

    private static bool HasHowToSignal(string normalized)
    {
        return ContainsAny(normalized,
            "howto", "wayto", "method", "roadmap", "learn", "study",
            "\uD558\uB294\uBC95", "\uBC29\uBC95", "\uB85C\uB4DC\uB9F5", "\uACF5\uBD80\uBC95", "\uBC30\uC6B0\uACE0\uC2F6", "\uC785\uBB38", "\uC2DC\uC791\uD574");
    }

    private static bool HasConsultativeActionSignal(string normalized)
    {
        return ContainsAny(normalized,
            "wanttocreate", "wanttomake", "wanttobuild", "thinkingaboutcreating", "whatwouldbegood", "possible",
            "\uB9CC\uB4E4\uACE0\uC2F6", "\uB9CC\uB4E4\uC5B4\uBCF4\uACE0\uC2F6", "\uD574\uBCF4\uACE0\uC2F6", "\uC5B4\uB5BB\uAC8C\uC88B", "\uC5B4\uB5A4\uAC8C\uC88B", "\uAC00\uB2A5\uD560\uAE4C", "\uB420\uAE4C");
    }

    private static bool RequiresWrite(string action) => action is "create" or "edit" or "delete" or "file" or "git";

    private static bool RequiresShell(string action) => action is "shell" or "git";

    private static bool RequiresNetwork(string normalized, string action) =>
        action == "search" || ContainsAny(normalized, "web", "internet", "\uC6F9", "\uC778\uD130\uB137", "\uD6C4\uAE30");

    private static string BuildClarifyingQuestion(string action) => action switch
    {
        "create" => "What exactly should AgentQ create? Examples: React stock analysis site, portfolio homepage, Python data analysis tool, API server.",
        "edit" => "Which file, screen, or behavior should AgentQ change?",
        "delete" => "What exactly should AgentQ delete? Deletion requires a clear target and approval.",
        "shell" => "Which command or task should AgentQ run?",
        _ => "Please clarify the target and desired result before AgentQ executes anything."
    };

    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text.ToLowerInvariant())
        {
            if (!char.IsWhiteSpace(ch) &&
                ch != '-' &&
                ch != '_' &&
                ch != '`' &&
                ch != '?' &&
                ch != '!' &&
                ch != '.' &&
                ch != ',')
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "...";
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static string ExtractJsonObject(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return string.Empty;
        }

        var start = responseText.IndexOf('{');
        var end = responseText.LastIndexOf('}');
        return start >= 0 && end > start
            ? responseText[start..(end + 1)]
            : string.Empty;
    }

    private sealed class ModelIntentResponse
    {
        public string Type { get; set; } = string.Empty;

        public double Confidence { get; set; }

        public string Rationale { get; set; } = string.Empty;

        public string ActionKind { get; set; } = string.Empty;

        public bool? RequiresWrite { get; set; }

        public bool? RequiresShell { get; set; }

        public bool? RequiresNetwork { get; set; }

        public bool? IsConcreteEnough { get; set; }

        public string ClarifyingQuestion { get; set; } = string.Empty;
    }
}
