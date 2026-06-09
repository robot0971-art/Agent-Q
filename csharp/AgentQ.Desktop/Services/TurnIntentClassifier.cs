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
            "create_directory" or
            "delete_path" or
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
        if (ruleClassification.Type == TurnIntentType.Conversation &&
            modelClassification.Type is TurnIntentType.Action or TurnIntentType.Hybrid &&
            modelClassification.RequiresWrite)
        {
            return ruleClassification with
            {
                Rationale = ruleClassification.Rationale + " Model-first classification attempted to promote a conversation turn to a write action, so AgentQ kept the non-executing classification."
            };
        }

        if (modelClassification.Type == TurnIntentType.Action &&
            ruleClassification.Type == TurnIntentType.Conversation &&
            modelClassification.Confidence < 0.92)
        {
            return ruleClassification with
            {
                Rationale = ruleClassification.Rationale + " Model-first classification attempted to promote the turn to Action, but confidence was below the safety threshold."
            };
        }

        if (ruleClassification.Type is TurnIntentType.Action or TurnIntentType.Hybrid &&
            ruleClassification.IsConcreteEnough &&
            ruleClassification.RequiresWrite &&
            modelClassification.Type is TurnIntentType.Conversation or TurnIntentType.Ambiguous)
        {
            return ruleClassification with
            {
                Rationale = ruleClassification.Rationale + " Model-first classification downgraded a concrete workspace mutation request to a non-executing turn, so AgentQ kept the concrete rule target while preserving permission checks."
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
            if (ruleClassification.Type is TurnIntentType.Action or TurnIntentType.Hybrid &&
                ruleClassification.IsConcreteEnough &&
                ruleClassification.RequiresWrite)
            {
                return ruleClassification with
                {
                    Rationale = ruleClassification.Rationale + " Model-first classification lost a concrete workspace target, so AgentQ kept the rule-based execution target."
                };
            }

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

        if (ContainsAny(normalized, "create", "make", "generate", "write", "\uB9CC\uB4E4", "\uC0DD\uC131", "\uC791\uC131", "\uCD94\uAC00") &&
            ContainsAny(normalized, "file", "folder", "directory", "\uD30C\uC77C", "\uD3F4\uB354", "\uB514\uB809\uD1A0\uB9AC"))
        {
            return "create";
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

        if (action == "delete" &&
            ContainsAny(normalized, "unnecessary", "unused", "useless", "\uBD88\uD544\uC694", "\uC548\uC4F0\uB294", "\uC4F0\uC9C0\uC54A\uB294") &&
            !ContainsAny(normalized, "thisfolder", "currentfolder", "entirefolder", "\uC774\uD3F4\uB354", "\uD604\uC7AC\uD3F4\uB354", "\uC804\uCCB4"))
        {
            return false;
        }

        if (action is "edit" or "delete" or "file" &&
            !ContainsAny(normalized,
                "file", "folder", "directory", "code", "ui", "readme", "button", "this", "current", "all", "everything", "entire",
                "\uD30C\uC77C", "\uD3F4\uB354", "\uB514\uB809\uD1A0\uB9AC", "\uCF54\uB4DC", "\uBC84\uD2BC", "\uD604\uC7AC", "\uC774\uAC70", "\uC774\uD3F4\uB354", "\uC804\uBD80", "\uBAA8\uB450", "\uC804\uCCB4", "\uB2E4"))
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
            "\uC5B4\uB584", "\uC5B4\uB54C", "\uC5B4\uB5A8\uAE4C", "\uC5B4\uB5A4\uAC78", "\uC5B4\uB5A4\uAC8C", "\uBB58", "\uBB50\uAC00", "\uBB34\uC5C7\uC744", "\uBCFC\uAE4C", "\uC218\uC788\uC744\uAE4C", "\uAD1C\uCC2E", "\uC88B\uC744\uAE4C", "\uBCC4\uB85C", "\uCD94\uCC9C", "\uB098\uC544", "\uC758\uACAC", "\uC0DD\uAC01", "\uBC29\uD5A5", "\uAE30\uB2A5\uC740", "\uD53C\uB4DC\uBC31", "\uB9AC\uBDF0", "\uBD84\uC11D", "\uD3C9\uAC00", "\uD574\uC57C\uD560\uAE4C", "\uB9D0\uC544\uC57C\uD560\uAE4C");
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
            "\uB9CC\uB4E4\uACE0\uC2F6", "\uB9CC\uB4E4\uC5B4\uBCF4\uACE0\uC2F6", "\uB9CC\uB4E4\uC5B4\uBCF4\uBA74", "\uB9CC\uB4E4\uC5B4\uBCFC\uAE4C", "\uB9CC\uB4E4\uAE4C", "\uB9CC\uB4E4\uB824\uACE0", "\uD558\uB824\uACE0\uD558\uB294\uB370", "\uD574\uBCF4\uACE0\uC2F6", "\uC5B4\uB5BB\uAC8C\uC88B", "\uC5B4\uB5A4\uAC8C\uC88B", "\uC5B4\uB5A4\uAC78\uB9CC\uB4E4", "\uC5B4\uB5A4\uAC8C\uB9CC\uB4E4", "\uC218\uC788\uC744\uAE4C", "\uAC00\uB2A5\uD560\uAE4C", "\uB420\uAE4C");
    }

    private static bool RequiresWrite(string action) => action is "create" or "edit" or "delete" or "file" or "git";

    private static bool RequiresShell(string action) => action is "shell" or "git";

    private static bool RequiresNetwork(string normalized, string action) =>
        action == "search" || ContainsAny(normalized, "web", "internet", "\uC6F9", "\uC778\uD130\uB137", "\uD6C4\uAE30");

    private static string BuildClarifyingQuestion(string action) => action switch
    {
        "create" => "\uBB34\uC5C7\uC744 \uB9CC\uB4E4\uC9C0 \uC880 \uB354 \uAD6C\uCCB4\uC801\uC73C\uB85C \uC54C\uB824\uC8FC\uC138\uC694. \uC608: React \uC8FC\uC2DD \uBD84\uC11D \uC0AC\uC774\uD2B8, \uD3EC\uD2B8\uD3F4\uB9AC\uC624 \uD648\uD398\uC774\uC9C0, Python \uB370\uC774\uD130 \uBD84\uC11D \uB3C4\uAD6C, API \uC11C\uBC84.",
        "edit" => "\uC5B4\uB290 \uD30C\uC77C, \uD654\uBA74, \uB3D9\uC791\uC744 \uBC14\uAFC0\uC9C0 \uC54C\uB824\uC8FC\uC138\uC694.",
        "delete" => "\uC0AD\uC81C \uB300\uC0C1\uC744 \uBA85\uD655\uD788 \uC54C\uB824\uC8FC\uC138\uC694. \uC0AD\uC81C\uB294 \uC2E4\uD589 \uC804\uC5D0 \uBC18\uB4DC\uC2DC \uC2B9\uC778\uC774 \uD544\uC694\uD569\uB2C8\uB2E4.",
        "shell" => "\uC2E4\uD589\uD560 \uBA85\uB839\uC774\uB098 \uC791\uC5C5\uC744 \uAD6C\uCCB4\uC801\uC73C\uB85C \uC54C\uB824\uC8FC\uC138\uC694.",
        _ => "\uC2E4\uD589\uD558\uAE30 \uC804\uC5D0 \uB300\uC0C1\uACFC \uC6D0\uD558\uB294 \uACB0\uACFC\uB97C \uC880 \uB354 \uAD6C\uCCB4\uC801\uC73C\uB85C \uC54C\uB824\uC8FC\uC138\uC694."
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
