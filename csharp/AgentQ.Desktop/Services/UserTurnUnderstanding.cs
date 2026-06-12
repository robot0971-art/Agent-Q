using System.Text.Json;

namespace AgentQ.Desktop.Services;

public sealed record EmbeddedContentItem
{
    public string Kind { get; init; } = "other";

    public string Text { get; init; } = string.Empty;

    public bool ShouldExecute { get; init; }

    public string Reason { get; init; } = string.Empty;
}

public sealed record ExecutionDecision
{
    public bool ShouldExecute { get; init; }

    public string ActionKind { get; init; } = "none";

    public string Target { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;
}

public sealed record UserTurnUnderstanding
{
    public string PrimaryIntent { get; init; } = "Conversation";

    public string UserGoal { get; init; } = string.Empty;

    public IReadOnlyList<EmbeddedContentItem> EmbeddedContent { get; init; } = [];

    public ExecutionDecision ActualRequestedAction { get; init; } = new();

    public bool RequiresReadOnlyInspection { get; init; }

    public bool RequiresWrite { get; init; }

    public bool RequiresShell { get; init; }

    public bool RequiresNetwork { get; init; }

    public bool IsConcreteEnough { get; init; }

    public string ClarifyingQuestion { get; init; } = string.Empty;

    public double Confidence { get; init; }

    public string RoutingText =>
        string.IsNullOrWhiteSpace(UserGoal)
            ? ActualRequestedAction.Target
            : UserGoal;
}

public static class UserTurnUnderstandingService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    public static UserTurnUnderstanding Understand(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText))
        {
            return new UserTurnUnderstanding
            {
                PrimaryIntent = "Conversation",
                UserGoal = string.Empty,
                Confidence = 0.9
            };
        }

        if (TryUnderstandMetaFeedback(userText, out var metaFeedback))
        {
            return metaFeedback;
        }

        if (TryUnderstandEmbeddedEvidence(userText, out var embeddedEvidence))
        {
            return embeddedEvidence;
        }

        var contract = UserIntentTranslator.Translate(userText);
        return FromTaskContract(userText, contract);
    }

    public static bool TryParseModelResponse(
        string responseText,
        string userText,
        UserTurnUnderstanding safetyFallback,
        out UserTurnUnderstanding understanding)
    {
        understanding = safetyFallback;
        var json = ExtractJsonObject(responseText);
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var model = JsonSerializer.Deserialize<ModelUnderstandingResponse>(json, JsonOptions);
            if (model == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(model.PrimaryIntent))
            {
                understanding = FromModelUnderstanding(model, userText, safetyFallback);
                return true;
            }

            if (!string.IsNullOrWhiteSpace(model.Type))
            {
                understanding = FromLegacyIntentModel(model, userText, safetyFallback);
                return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    public static UserTurnUnderstanding ApplySafetyRules(
        UserTurnUnderstanding safetyFallback,
        UserTurnUnderstanding modelUnderstanding)
    {
        if (safetyFallback.ActualRequestedAction.ShouldExecute &&
            !modelUnderstanding.ActualRequestedAction.ShouldExecute)
        {
            return safetyFallback with
            {
                Confidence = Math.Max(safetyFallback.Confidence, modelUnderstanding.Confidence),
                ActualRequestedAction = safetyFallback.ActualRequestedAction with
                {
                    Reason =
                        $"Deterministic fallback found a concrete current action while the model returned non-executing intent {modelUnderstanding.PrimaryIntent}; preserving the explicit action contract."
                }
            };
        }

        if (safetyFallback.ActualRequestedAction.ShouldExecute)
        {
            if (!ActionKindsAreCompatible(
                    safetyFallback.ActualRequestedAction.ActionKind,
                    modelUnderstanding.ActualRequestedAction.ActionKind))
            {
                return safetyFallback with
                {
                    Confidence = Math.Max(safetyFallback.Confidence, modelUnderstanding.Confidence),
                    ActualRequestedAction = safetyFallback.ActualRequestedAction with
                    {
                        Reason =
                            $"Deterministic fallback found a concrete current action ({safetyFallback.ActualRequestedAction.ActionKind}) while the model returned a different action ({modelUnderstanding.ActualRequestedAction.ActionKind}); preserving the explicit action contract."
                    }
                };
            }

            return PreserveCurrentActionSurface(safetyFallback, modelUnderstanding);
        }

        if (modelUnderstanding.ActualRequestedAction.ShouldExecute &&
            (modelUnderstanding.RequiresWrite ||
             modelUnderstanding.RequiresShell ||
             RequiresWrite(modelUnderstanding.ActualRequestedAction.ActionKind) ||
             RequiresShell(modelUnderstanding.ActualRequestedAction.ActionKind)))
        {
            return safetyFallback with
            {
                Confidence = Math.Max(safetyFallback.Confidence, modelUnderstanding.Confidence),
                ActualRequestedAction = safetyFallback.ActualRequestedAction with
                {
                    Reason =
                        $"Deterministic fallback found no concrete current execution request while the model promoted the turn to {modelUnderstanding.PrimaryIntent}; blocking write/shell execution."
                }
            };
        }

        if (!string.Equals(safetyFallback.PrimaryIntent, "MetaFeedback", StringComparison.OrdinalIgnoreCase) &&
            safetyFallback.EmbeddedContent.Count == 0)
        {
            return modelUnderstanding;
        }

        return safetyFallback with
        {
            Confidence = Math.Max(safetyFallback.Confidence, modelUnderstanding.Confidence),
            ActualRequestedAction = safetyFallback.ActualRequestedAction with
            {
                Reason = string.IsNullOrWhiteSpace(safetyFallback.ActualRequestedAction.Reason)
                    ? "Safety fallback identified this as non-executing embedded evidence."
                    : safetyFallback.ActualRequestedAction.Reason
            }
        };
    }

    public static TurnIntentClassification ToTurnIntentClassification(UserTurnUnderstanding understanding)
    {
        if (!understanding.ActualRequestedAction.ShouldExecute)
        {
            return new TurnIntentClassification
            {
                Type = string.Equals(understanding.PrimaryIntent, "Ambiguous", StringComparison.OrdinalIgnoreCase)
                    ? TurnIntentType.Ambiguous
                    : TurnIntentType.Conversation,
                Confidence = understanding.Confidence <= 0 ? 0.78 : understanding.Confidence,
                Rationale = string.IsNullOrWhiteSpace(understanding.ActualRequestedAction.Reason)
                    ? understanding.UserGoal
                    : understanding.ActualRequestedAction.Reason,
                ActionKind = string.Empty,
                RequiresWrite = false,
                RequiresShell = false,
                RequiresNetwork = understanding.RequiresNetwork,
                IsConcreteEnough = understanding.IsConcreteEnough,
                ClarifyingQuestion = understanding.ClarifyingQuestion
            };
        }

        var type = string.Equals(understanding.PrimaryIntent, "Hybrid", StringComparison.OrdinalIgnoreCase)
            ? TurnIntentType.Hybrid
            : TurnIntentType.Action;
        var actionKind = NormalizeActionKind(understanding.ActualRequestedAction.ActionKind);
        return new TurnIntentClassification
        {
            Type = type,
            Confidence = understanding.Confidence <= 0 ? 0.75 : understanding.Confidence,
            Rationale = string.IsNullOrWhiteSpace(understanding.ActualRequestedAction.Reason)
                ? understanding.UserGoal
                : understanding.ActualRequestedAction.Reason,
            ActionKind = actionKind,
            RequiresWrite = understanding.RequiresWrite,
            RequiresShell = understanding.RequiresShell,
            RequiresNetwork = understanding.RequiresNetwork,
            IsConcreteEnough = understanding.IsConcreteEnough,
            ClarifyingQuestion = understanding.ClarifyingQuestion
        };
    }

    private static UserTurnUnderstanding FromTaskContract(string userText, TaskContract contract)
    {
        return new UserTurnUnderstanding
        {
            PrimaryIntent = contract.IsActionable ? "Action" : "Conversation",
            UserGoal = userText,
            ActualRequestedAction = new ExecutionDecision
            {
                ShouldExecute = contract.IsActionable,
                ActionKind = contract.Intent == TaskContractIntent.None ? "none" : contract.Intent.ToString(),
                Target = userText,
                Reason = contract.IsActionable
                    ? "The current turn itself is an actionable request."
                    : "No current-turn execution request was detected."
            },
            RequiresWrite = contract.Intent is TaskContractIntent.CreateDirectory or TaskContractIntent.CreateFile or TaskContractIntent.CreateProject or TaskContractIntent.ModifyCode or TaskContractIntent.DeletePath,
            RequiresShell = contract.Intent is TaskContractIntent.RunLocalServer or TaskContractIntent.RunVerification,
            RequiresNetwork = contract.Intent is TaskContractIntent.SearchAndSummarize,
            IsConcreteEnough = contract.IsActionable,
            Confidence = contract.IsActionable ? contract.Confidence : 0.78
        };
    }

    private static UserTurnUnderstanding FromModelUnderstanding(
        ModelUnderstandingResponse model,
        string userText,
        UserTurnUnderstanding safetyFallback)
    {
        var action = model.ActualRequestedAction ?? new ModelExecutionDecision();
        var embedded = model.EmbeddedContent?
            .Where(item => item != null)
            .Select(item => new EmbeddedContentItem
            {
                Kind = string.IsNullOrWhiteSpace(item.Kind) ? "other" : item.Kind.Trim(),
                Text = item.Text?.Trim() ?? string.Empty,
                ShouldExecute = item.ShouldExecute,
                Reason = item.Reason?.Trim() ?? string.Empty
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .Take(12)
            .ToList() ?? [];

        var shouldExecute = action.ShouldExecute == true;
        var actionKind = string.IsNullOrWhiteSpace(action.ActionKind)
            ? "none"
            : action.ActionKind.Trim();
        var confidence = Math.Clamp(model.Confidence <= 0 ? 0.75 : model.Confidence, 0, 1);
        return new UserTurnUnderstanding
        {
            PrimaryIntent = string.IsNullOrWhiteSpace(model.PrimaryIntent)
                ? safetyFallback.PrimaryIntent
                : model.PrimaryIntent.Trim(),
            UserGoal = string.IsNullOrWhiteSpace(model.UserGoal) ? userText : model.UserGoal.Trim(),
            EmbeddedContent = embedded,
            ActualRequestedAction = new ExecutionDecision
            {
                ShouldExecute = shouldExecute,
                ActionKind = actionKind,
                Target = string.IsNullOrWhiteSpace(action.Target) ? userText : action.Target.Trim(),
                Reason = action.Reason?.Trim() ?? string.Empty
            },
            RequiresReadOnlyInspection = model.RequiresReadOnlyInspection ?? safetyFallback.RequiresReadOnlyInspection,
            RequiresWrite = model.RequiresWrite ?? RequiresWrite(actionKind),
            RequiresShell = model.RequiresShell ?? RequiresShell(actionKind),
            RequiresNetwork = model.RequiresNetwork ?? RequiresNetwork(actionKind),
            IsConcreteEnough = model.IsConcreteEnough ?? shouldExecute,
            ClarifyingQuestion = model.ClarifyingQuestion?.Trim() ?? string.Empty,
            Confidence = confidence
        };
    }

    private static UserTurnUnderstanding FromLegacyIntentModel(
        ModelUnderstandingResponse model,
        string userText,
        UserTurnUnderstanding safetyFallback)
    {
        var type = model.Type?.Trim() ?? safetyFallback.PrimaryIntent;
        var actionKind = model.ActionKind?.Trim() ?? string.Empty;
        var shouldExecute = string.Equals(type, "Action", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "Hybrid", StringComparison.OrdinalIgnoreCase);
        var confidence = Math.Clamp(model.Confidence <= 0 ? 0.75 : model.Confidence, 0, 1);
        return new UserTurnUnderstanding
        {
            PrimaryIntent = type,
            UserGoal = userText,
            ActualRequestedAction = new ExecutionDecision
            {
                ShouldExecute = shouldExecute,
                ActionKind = string.IsNullOrWhiteSpace(actionKind) ? "none" : actionKind,
                Target = userText,
                Reason = string.IsNullOrWhiteSpace(model.Rationale)
                    ? "Legacy model intent JSON was converted into UserTurnUnderstanding."
                    : model.Rationale.Trim()
            },
            RequiresWrite = model.RequiresWrite ?? RequiresWrite(actionKind),
            RequiresShell = model.RequiresShell ?? RequiresShell(actionKind),
            RequiresNetwork = model.RequiresNetwork ?? RequiresNetwork(actionKind),
            IsConcreteEnough = model.IsConcreteEnough ?? shouldExecute,
            ClarifyingQuestion = model.ClarifyingQuestion?.Trim() ?? string.Empty,
            Confidence = confidence
        };
    }

    private static bool TryUnderstandMetaFeedback(string userText, out UserTurnUnderstanding understanding)
    {
        understanding = new UserTurnUnderstanding();
        var normalized = Normalize(userText);
        if (!LooksLikeBadAgentResponseComplaint(normalized))
        {
            return false;
        }

        var parts = SplitLikelyEmbeddedSections(userText);
        if (parts.Count < 2)
        {
            return false;
        }

        var embedded = new List<EmbeddedContentItem>();
        if (!string.IsNullOrWhiteSpace(parts[0]))
        {
            embedded.Add(new EmbeddedContentItem
            {
                Kind = "example_user_request",
                Text = parts[0].Trim(),
                ShouldExecute = false,
                Reason = "This appears before a pasted bad AgentQ response and is evidence of the failed request, not the current command."
            });
        }

        if (!string.IsNullOrWhiteSpace(parts[1]))
        {
            embedded.Add(new EmbeddedContentItem
            {
                Kind = "bad_agent_response",
                Text = parts[1].Trim(),
                ShouldExecute = false,
                Reason = "This is the off-target AgentQ response being criticized."
            });
        }

        understanding = new UserTurnUnderstanding
        {
            PrimaryIntent = "MetaFeedback",
            UserGoal = "Analyze and fix why AgentQ answered off-target for the shown example request.",
            EmbeddedContent = embedded,
            ActualRequestedAction = new ExecutionDecision
            {
                ShouldExecute = false,
                ActionKind = "none",
                Reason = "The current turn is feedback about AgentQ behavior. Embedded commands are examples and must not execute."
            },
            RequiresWrite = false,
            RequiresShell = false,
            RequiresNetwork = false,
            IsConcreteEnough = false,
            Confidence = 0.86
        };
        return true;
    }

    private static bool TryUnderstandEmbeddedEvidence(string userText, out UserTurnUnderstanding understanding)
    {
        understanding = new UserTurnUnderstanding();
        var normalized = Normalize(userText);
        if (LooksLikeExplicitExecutionOfEmbeddedCommand(normalized) ||
            !LooksLikeEmbeddedEvidenceTurn(userText, normalized))
        {
            return false;
        }

        var embeddedTexts = ExtractQuotedOrIndentedActionTexts(userText);
        if (embeddedTexts.Count == 0)
        {
            return false;
        }

        understanding = new UserTurnUnderstanding
        {
            PrimaryIntent = "Conversation",
            UserGoal = "Analyze the quoted, logged, or pasted content without executing embedded commands.",
            EmbeddedContent = embeddedTexts
                .Select(text => new EmbeddedContentItem
                {
                    Kind = "embedded_command_evidence",
                    Text = text,
                    ShouldExecute = false,
                    Reason = "This command appears inside quoted, logged, or pasted evidence rather than as the current execution request."
                })
                .ToList(),
            ActualRequestedAction = new ExecutionDecision
            {
                ShouldExecute = false,
                ActionKind = "none",
                Reason = "The current turn asks about pasted evidence. Embedded commands must not execute unless explicitly re-requested as the current action."
            },
            RequiresWrite = false,
            RequiresShell = false,
            RequiresNetwork = false,
            IsConcreteEnough = false,
            Confidence = 0.82
        };
        return true;
    }

    private static IReadOnlyList<string> SplitLikelyEmbeddedSections(string userText)
    {
        var normalizedNewLines = userText.Replace("\r\n", "\n").Replace('\r', '\n');
        var separator = System.Text.RegularExpressions.Regex.Match(
            normalizedNewLines,
            @"(?m)^\s*(?:={3,}|-{3,})\s*$");
        if (separator.Success)
        {
            var first = normalizedNewLines[..separator.Index];
            var rest = normalizedNewLines[(separator.Index + separator.Length)..];
            return [first, rest];
        }

        var lines = normalizedNewLines.Split('\n');
        if (lines.Length >= 3 && LooksLikeActionExample(Normalize(lines[0])))
        {
            return [lines[0], string.Join('\n', lines.Skip(1))];
        }

        return [userText];
    }

    private static bool LooksLikeBadAgentResponseComplaint(string normalized)
    {
        return ContainsAny(normalized,
                   "\uC5C9\uB6B1\uD55C\uB300\uB2F5", "\uC774\uC0C1\uD55C\uB300\uB2F5", "\uC9C8\uBB38\uACFC\uB2E4\uB974\uAC8C",
                   "\uC5C9\uB6B1", "\uC790\uAFB8", "\uBABB\uACE0\uCE58", "\uD2C0\uB9B0\uB300\uB2F5",
                   "badagentresponse", "offtarget", "wronganswer", "irrelevantanswer") &&
               ContainsAny(normalized,
                   "\uC778\uACF5\uC9C0\uB2A5", "\uB3C5\uC11C", "\uAC8C\uC784", "\uC990\uAE38",
                   "\uBB34\uC5C7\uC744\uB3C4\uC640", "agentq", "\uC5D0\uC774\uC804\uD2B8q", "\uB300\uB2F5");
    }

    private static bool LooksLikeActionExample(string normalized)
    {
        return ContainsAny(normalized,
            "\uC0DD\uC131\uD574\uC918", "\uB9CC\uB4E4\uC5B4\uC918", "\uC0AD\uC81C\uD574\uC918", "\uC218\uC815\uD574\uC918",
            "\uC2E4\uD589\uD574\uC918", "\uBE4C\uB4DC\uD574\uC918", "\uD14C\uC2A4\uD2B8\uB3CC\uB824\uC918",
            "create", "make", "delete", "remove", "run", "build", "test");
    }

    private static bool LooksLikeEmbeddedEvidenceTurn(string userText, string normalized)
    {
        var hasEvidenceFrame = ContainsAny(
            normalized,
            "\uB85C\uADF8", "\uC5D0\uB7EC", "\uC624\uB958", "\uCD9C\uB825", "\uC608\uC2DC", "\uC778\uC6A9", "\uB530\uC634\uD45C",
            "\uBD99\uC5EC\uB123", "\uB300\uD654", "\uC751\uB2F5", "\uB2F5\uBCC0", "\uC6D0\uC778", "\uBD84\uC11D",
            "\uB73B", "\uC758\uBBF8", "\uC774\uBBF8\uC9C0", "\uC2A4\uD06C\uB9B0\uC0F7", "\uD654\uBA74",
            "log", "error", "output", "example", "quoted", "quote", "pasted", "transcript", "response", "analyze", "analysis", "why", "meaning", "image", "screenshot");
        if (!hasEvidenceFrame)
        {
            return false;
        }

        return userText.Contains('`') ||
               userText.Contains('"') ||
               userText.Contains('\'') ||
               userText.Contains('>') ||
               userText.Contains("\n", StringComparison.Ordinal);
    }

    private static bool LooksLikeExplicitExecutionOfEmbeddedCommand(string normalized)
    {
        return ContainsAny(
            normalized,
            "\uC778\uC6A9\uB41C\uBA85\uB839\uC2E4\uD589", "\uB530\uC634\uD45C\uC548\uBA85\uB839\uC2E4\uD589",
            "\uB85C\uADF8\uC5D0\uC788\uB294\uBA85\uB839\uC2E4\uD589", "\uC704\uBA85\uB839\uC2E4\uD589", "\uADF8\uBA85\uB839\uC2E4\uD589",
            "executequotedcommand", "runquotedcommand", "executethequotedcommand", "runthequotedcommand");
    }

    private static IReadOnlyList<string> ExtractQuotedOrIndentedActionTexts(string userText)
    {
        var candidates = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                     userText,
                     "```[^\\r\\n`]*\\r?\\n(?<text>.*?)```",
                     System.Text.RegularExpressions.RegexOptions.Singleline))
        {
            AddActionTextCandidates(candidates, match.Groups["text"].Value);
        }

        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                     userText,
                     "`(?<text>[^`]+)`|\"(?<text>[^\"]+)\"|'(?<text>[^']+)'|^\\s*>\\s*(?<text>.+)$",
                     System.Text.RegularExpressions.RegexOptions.Multiline))
        {
            AddActionTextCandidates(candidates, match.Groups["text"].Value);
        }

        var normalizedNewLines = userText.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                     normalizedNewLines,
                     @"(?ms)^\s*(?:={3,}|-{3,})\s*$\n(?<text>.*?)(?=^\s*(?:={3,}|-{3,})\s*$|\z)"))
        {
            AddActionTextCandidates(candidates, match.Groups["text"].Value);
        }

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();
    }

    private static void AddActionTextCandidates(List<string> candidates, string text)
    {
        var trimmed = text.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return;
        }

        if (LooksLikeActionExample(Normalize(trimmed)))
        {
            candidates.Add(trimmed);
            return;
        }

        foreach (var line in trimmed.ReplaceLineEndings("\n").Split('\n'))
        {
            var candidate = line.Trim();
            if (LooksLikeActionExample(Normalize(candidate)))
            {
                candidates.Add(candidate);
            }
        }
    }

    private static string Normalize(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
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

    private static string NormalizeActionKind(string actionKind)
    {
        var normalized = actionKind.Trim().ToLowerInvariant();
        return normalized switch
        {
            "createproject" or "create_project" or "scaffold" => "create",
            "rundevserver" or "runlocalserver" or "server" => "shell",
            "runverification" or "verification" => "shell",
            "searchandsummarize" => "search",
            "inspectproject" or "inspect" => "read",
            "createfile" or "createdirectory" => "create",
            "deletepath" => "delete",
            _ => normalized
        };
    }

    private static bool RequiresWrite(string actionKind)
    {
        var normalized = NormalizeActionKind(actionKind);
        return normalized is "create" or "edit" or "delete" or "file" or "git";
    }

    private static bool RequiresShell(string actionKind)
    {
        var normalized = NormalizeActionKind(actionKind);
        return normalized is "shell" or "run" or "build" or "test" or "install";
    }

    private static bool RequiresNetwork(string actionKind)
    {
        var normalized = NormalizeActionKind(actionKind);
        return normalized is "search" or "network";
    }

    private static bool ActionKindsAreCompatible(string fallbackActionKind, string modelActionKind)
    {
        var fallback = NormalizeActionKind(fallbackActionKind);
        var model = NormalizeActionKind(modelActionKind);

        if (string.IsNullOrWhiteSpace(fallback) || fallback == "none")
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(model) || model == "none")
        {
            return false;
        }

        return string.Equals(fallback, model, StringComparison.OrdinalIgnoreCase);
    }

    private static UserTurnUnderstanding PreserveCurrentActionSurface(
        UserTurnUnderstanding safetyFallback,
        UserTurnUnderstanding modelUnderstanding)
    {
        var modelTarget = modelUnderstanding.ActualRequestedAction.Target;
        var target = IsModelTargetCompatibleWithFallback(modelTarget, safetyFallback)
            ? modelTarget
            : safetyFallback.ActualRequestedAction.Target;

        return modelUnderstanding with
        {
            UserGoal = string.IsNullOrWhiteSpace(safetyFallback.UserGoal)
                ? modelUnderstanding.UserGoal
                : safetyFallback.UserGoal,
            RequiresWrite = safetyFallback.RequiresWrite || modelUnderstanding.RequiresWrite,
            RequiresShell = safetyFallback.RequiresShell || modelUnderstanding.RequiresShell,
            RequiresNetwork = safetyFallback.RequiresNetwork || modelUnderstanding.RequiresNetwork,
            IsConcreteEnough = safetyFallback.IsConcreteEnough || modelUnderstanding.IsConcreteEnough,
            ActualRequestedAction = modelUnderstanding.ActualRequestedAction with
            {
                Target = target
            }
        };
    }

    private static bool IsModelTargetCompatibleWithFallback(
        string modelTarget,
        UserTurnUnderstanding safetyFallback)
    {
        if (string.IsNullOrWhiteSpace(modelTarget))
        {
            return false;
        }

        var normalizedModelTarget = Normalize(modelTarget);
        if (string.IsNullOrWhiteSpace(normalizedModelTarget))
        {
            return false;
        }

        var normalizedGoal = Normalize(safetyFallback.UserGoal);
        var normalizedFallbackTarget = Normalize(safetyFallback.ActualRequestedAction.Target);
        return normalizedGoal.Contains(normalizedModelTarget, StringComparison.Ordinal) ||
               normalizedFallbackTarget.Contains(normalizedModelTarget, StringComparison.Ordinal);
    }

    private sealed class ModelUnderstandingResponse
    {
        public string PrimaryIntent { get; set; } = string.Empty;

        public string UserGoal { get; set; } = string.Empty;

        public List<ModelEmbeddedContentItem>? EmbeddedContent { get; set; }

        public ModelExecutionDecision? ActualRequestedAction { get; set; }

        public bool? RequiresReadOnlyInspection { get; set; }

        public bool? RequiresWrite { get; set; }

        public bool? RequiresShell { get; set; }

        public bool? RequiresNetwork { get; set; }

        public bool? IsConcreteEnough { get; set; }

        public string ClarifyingQuestion { get; set; } = string.Empty;

        public double Confidence { get; set; }

        public string Type { get; set; } = string.Empty;

        public string Rationale { get; set; } = string.Empty;

        public string ActionKind { get; set; } = string.Empty;
    }

    private sealed class ModelEmbeddedContentItem
    {
        public string Kind { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;

        public bool ShouldExecute { get; set; }

        public string Reason { get; set; } = string.Empty;
    }

    private sealed class ModelExecutionDecision
    {
        public bool? ShouldExecute { get; set; }

        public string ActionKind { get; set; } = string.Empty;

        public string Target { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;
    }
}
