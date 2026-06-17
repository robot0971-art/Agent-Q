using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentQ.Desktop.Services;

public sealed record ImplementationContract
{
    public required string Goal { get; init; }

    public required IReadOnlyList<ImplementationRequirement> Requirements { get; init; }

    public required IReadOnlyList<string> RequiredFiles { get; init; }

    public required IReadOnlyList<string> ForbiddenPlaceholders { get; init; }

    public bool RequiresRuntimePreview { get; init; }

    public bool RequiresVisualEvidence { get; init; }
}

public sealed record ImplementationRequirement
{
    public required string Id { get; init; }

    public required string Description { get; init; }

    public required IReadOnlyList<string> AnyKeywords { get; init; }
}

public sealed record ImplementationVerificationResult
{
    public required bool Succeeded { get; init; }

    public required bool RequiresImplementation { get; init; }

    public required IReadOnlyList<string> MissingRequirements { get; init; }

    public required IReadOnlyList<string> PlaceholderFindings { get; init; }

    public required IReadOnlyList<string> InspectedFiles { get; init; }

    public required bool RuntimePreviewRequired { get; init; }

    public required bool VisualEvidenceRequired { get; init; }

    public string Summary
    {
        get
        {
            if (Succeeded)
            {
                return "Implementation contract satisfied by inspected files.";
            }

            var parts = new List<string>();
            if (PlaceholderFindings.Count > 0)
            {
                parts.Add("placeholders: " + string.Join("; ", PlaceholderFindings));
            }

            if (MissingRequirements.Count > 0)
            {
                parts.Add("missing: " + string.Join("; ", MissingRequirements));
            }

            return parts.Count == 0
                ? "Implementation contract requires additional runtime or visual evidence."
                : string.Join(" | ", parts);
        }
    }
}

public sealed record ImplementationPreviewVerificationResult
{
    public required bool Succeeded { get; init; }

    public required bool RequiresPreviewEvidence { get; init; }

    public required bool RootRendered { get; init; }

    public required IReadOnlyList<string> MissingDomRequirements { get; init; }

    public required IReadOnlyList<string> ConsoleErrors { get; init; }

    public required IReadOnlyList<string> VisualFindings { get; init; }

    public string Url { get; init; } = string.Empty;

    public string ScreenshotDirectory { get; init; } = string.Empty;

    public string Summary
    {
        get
        {
            if (Succeeded)
            {
                return "Preview, DOM, and first-pass visual evidence satisfied the implementation contract.";
            }

            var parts = new List<string>();
            if (!RootRendered)
            {
                parts.Add("root did not render meaningful content");
            }

            if (MissingDomRequirements.Count > 0)
            {
                parts.Add("missing DOM evidence: " + string.Join("; ", MissingDomRequirements));
            }

            if (ConsoleErrors.Count > 0)
            {
                parts.Add("console errors: " + string.Join("; ", ConsoleErrors));
            }

            if (VisualFindings.Count > 0)
            {
                parts.Add("visual findings: " + string.Join("; ", VisualFindings));
            }

            return parts.Count == 0
                ? "Preview evidence is required before reporting completion."
                : string.Join(" | ", parts);
        }
    }
}

public static class ImplementationCompletionService
{
    private static readonly Regex WordSplitter = new("[^a-z0-9]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] DefaultForbiddenPlaceholders =
    [
        "Hello World",
        "Vite + React",
        "ShoppingCart is ready",
        "App is ready",
        "Lorem ipsum",
        "TODO",
        "is ready."
    ];

    public static ImplementationContract BuildContract(AgentTurnState turnState)
    {
        var requestText = turnState.RoutingText;
        var text = $"{requestText} {turnState.ProjectScaffoldPlan.Intent?.ProjectType} {turnState.ProjectScaffoldPlan.Intent?.Framework}".ToLowerInvariant();
        var files = turnState.ProjectScaffoldPlan.Plan?.Files ?? [];
        var appFiles = files
            .Where(file => file.EndsWith("src/App.jsx", StringComparison.OrdinalIgnoreCase) ||
                           file.EndsWith("src/App.tsx", StringComparison.OrdinalIgnoreCase) ||
                           file.EndsWith("src/styles.css", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var requirements = new List<ImplementationRequirement>();
        var isShopping = ContainsAny(
            text,
            "shop",
            "shopping",
            "store",
            "mall",
            "commerce",
            "clothing",
            "fashion",
            "apparel",
            "\uC1FC\uD551",
            "\uC1FC\uD551\uBAB0",
            "\uC0C1\uC810",
            "\uC758\uB958",
            "\uD328\uC158");
        var isLuxury = ContainsAny(
            text,
            "luxury",
            "premium",
            "vip",
            "atelier",
            "curated",
            "\uB7ED\uC154\uB9AC",
            "\uACE0\uAE09",
            "\uBA85\uD488",
            "\uD504\uB9AC\uBBF8\uC5C4");

        if (isShopping)
        {
            requirements.Add(new ImplementationRequirement
            {
                Id = "product-catalog",
                Description = "Product catalog/cards are rendered.",
                AnyKeywords = ["product", "collection", "card", "price", "catalog", "item", "\uC0C1\uD488", "\uC81C\uD488", "\uAC00\uACA9"]
            });
            requirements.Add(new ImplementationRequirement
            {
                Id = "cart",
                Description = "Cart or bag interaction exists.",
                AnyKeywords = ["cart", "bag", "add to", "checkout", "\uC7A5\uBC14\uAD6C\uB2C8", "\uB2F4\uAE30"]
            });
            requirements.Add(new ImplementationRequirement
            {
                Id = "wishlist",
                Description = "Wishlist/save interaction exists.",
                AnyKeywords = ["wishlist", "wish", "save", "heart", "favorite", "\uC704\uC2DC", "\uAD00\uC2EC", "\uD558\uD2B8"]
            });
            requirements.Add(new ImplementationRequirement
            {
                Id = "lookbook",
                Description = "Hero/lookbook/editorial section exists.",
                AnyKeywords = ["lookbook", "hero", "editorial", "campaign", "collection", "\uB8E9\uBD81", "\uD788\uC5B4\uB85C"]
            });
        }

        if (isLuxury)
        {
            requirements.Add(new ImplementationRequirement
            {
                Id = "luxury-style",
                Description = "Luxury visual language is represented.",
                AnyKeywords = ["luxury", "atelier", "vip", "premium", "curated", "bespoke", "editorial", "\uB7ED\uC154\uB9AC", "\uACE0\uAE09", "\uBA85\uD488"]
            });
        }

        if (requirements.Count == 0 && IsFrontendScaffold(turnState.ProjectScaffoldPlan))
        {
            requirements.Add(new ImplementationRequirement
            {
                Id = "non-template-ui",
                Description = "The frontend contains a user-specific UI rather than the default scaffold panel.",
                AnyKeywords = BuildTokens(requestText).Where(token => token.Length >= 3).Take(8).ToList()
            });
        }

        return new ImplementationContract
        {
            Goal = string.IsNullOrWhiteSpace(requestText) ? "Implement the requested application." : requestText,
            Requirements = requirements,
            RequiredFiles = appFiles.Count > 0 ? appFiles : files.Take(8).ToList(),
            ForbiddenPlaceholders = DefaultForbiddenPlaceholders,
            RequiresRuntimePreview = IsFrontendScaffold(turnState.ProjectScaffoldPlan),
            RequiresVisualEvidence = IsFrontendScaffold(turnState.ProjectScaffoldPlan)
        };
    }

    public static bool ShouldRequireImplementation(AgentTurnState turnState) =>
        turnState.TaskContract.Intent == TaskContractIntent.CreateProject &&
        turnState.ProjectScaffoldPlan.IsGreenfieldRequest &&
        turnState.ProjectScaffoldPlan.CanProceed &&
        IsFrontendScaffold(turnState.ProjectScaffoldPlan);

    public static ImplementationVerificationResult Verify(string workspaceRoot, ImplementationContract contract)
    {
        var inspected = new List<string>();
        var combined = new StringBuilder();
        foreach (var relativePath in contract.RequiredFiles)
        {
            var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(workspaceRoot, normalized));
            if (!WorkspacePathResolver.IsResolvedInsideWorkspace(workspaceRoot, fullPath) ||
                !File.Exists(fullPath))
            {
                continue;
            }

            inspected.Add(relativePath.Replace('\\', '/'));
            combined.AppendLine(File.ReadAllText(fullPath));
        }

        if (inspected.Count == 0)
        {
            foreach (var relativePath in DiscoverFrontendImplementationFiles(workspaceRoot))
            {
                var fullPath = Path.GetFullPath(Path.Combine(workspaceRoot, relativePath));
                if (!WorkspacePathResolver.IsResolvedInsideWorkspace(workspaceRoot, fullPath) ||
                    !File.Exists(fullPath))
                {
                    continue;
                }

                inspected.Add(relativePath.Replace('\\', '/'));
                combined.AppendLine(File.ReadAllText(fullPath));
            }
        }

        var text = combined.ToString();
        var placeholderFindings = DetectPlaceholders(text, contract.ForbiddenPlaceholders);
        var missing = FindMissingRequirements(text, contract.Requirements);
        var succeeded = inspected.Count > 0 &&
                        placeholderFindings.Count == 0 &&
                        missing.Count == 0;

        return new ImplementationVerificationResult
        {
            Succeeded = succeeded,
            RequiresImplementation = !succeeded,
            MissingRequirements = missing,
            PlaceholderFindings = placeholderFindings,
            InspectedFiles = inspected,
            RuntimePreviewRequired = contract.RequiresRuntimePreview,
            VisualEvidenceRequired = contract.RequiresVisualEvidence
        };
    }

    public static IReadOnlyList<string> DetectPlaceholders(string text, IReadOnlyList<string>? forbiddenPlaceholders = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ["empty implementation content"];
        }

        var placeholders = forbiddenPlaceholders ?? DefaultForbiddenPlaceholders;
        return placeholders
            .Where(value => text.Contains(value, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static ImplementationPreviewVerificationResult VerifyPreviewEvidence(
        string htmlOrDomText,
        ImplementationContract contract,
        IReadOnlyList<string>? consoleErrors = null,
        IReadOnlyList<string>? visualFindings = null,
        string url = "",
        string screenshotDirectory = "")
    {
        if (!contract.RequiresRuntimePreview && !contract.RequiresVisualEvidence)
        {
            return new ImplementationPreviewVerificationResult
            {
                Succeeded = true,
                RequiresPreviewEvidence = false,
                RootRendered = true,
                MissingDomRequirements = [],
                ConsoleErrors = [],
                VisualFindings = [],
                Url = url,
                ScreenshotDirectory = screenshotDirectory
            };
        }

        var text = htmlOrDomText ?? string.Empty;
        var rootRendered = HasMeaningfulRootRender(text);
        var missing = FindMissingRequirements(text, contract.Requirements);
        var errors = (consoleErrors ?? [])
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var visuals = (visualFindings ?? [])
            .Where(finding => !string.IsNullOrWhiteSpace(finding))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ImplementationPreviewVerificationResult
        {
            Succeeded = rootRendered && missing.Count == 0 && errors.Count == 0 && visuals.Count == 0,
            RequiresPreviewEvidence = true,
            RootRendered = rootRendered,
            MissingDomRequirements = missing,
            ConsoleErrors = errors,
            VisualFindings = visuals,
            Url = url,
            ScreenshotDirectory = screenshotDirectory
        };
    }

    public static string BuildImplementationInstruction(ImplementationContract contract, ImplementationVerificationResult verification)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ScaffoldReady is not task completion. Continue with the implementation phase now.");
        builder.AppendLine($"Implementation goal: {contract.Goal}");
        builder.AppendLine("Update the scaffolded source files with a real application, then run build/preview verification when available.");
        builder.AppendLine("Required implementation evidence:");
        foreach (var requirement in contract.Requirements)
        {
            builder.AppendLine($"- {requirement.Id}: {requirement.Description}");
        }

        builder.AppendLine("Forbidden placeholders:");
        foreach (var placeholder in contract.ForbiddenPlaceholders)
        {
            builder.AppendLine($"- {placeholder}");
        }

        if (verification.PlaceholderFindings.Count > 0)
        {
            builder.AppendLine("Current placeholder findings:");
            foreach (var finding in verification.PlaceholderFindings)
            {
                builder.AppendLine($"- {finding}");
            }
        }

        if (verification.MissingRequirements.Count > 0)
        {
            builder.AppendLine("Current missing requirements:");
            foreach (var missing in verification.MissingRequirements)
            {
                builder.AppendLine($"- {missing}");
            }
        }

        if (contract.RequiresRuntimePreview)
        {
            builder.AppendLine("After implementation, provide build, localhost preview, DOM, and screenshot/visual evidence; do not claim completion from scaffold/build alone.");
        }

        return builder.ToString();
    }

    private static bool IsFrontendScaffold(ProjectScaffoldPlanningResult plan) =>
        string.Equals(plan.Intent?.Framework, "vite-react", StringComparison.OrdinalIgnoreCase) ||
        (plan.Plan?.Files.Any(file => file.EndsWith("src/App.jsx", StringComparison.OrdinalIgnoreCase) ||
                                      file.EndsWith("src/App.tsx", StringComparison.OrdinalIgnoreCase)) == true);

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> BuildTokens(string value) =>
        WordSplitter.Split(value.ToLowerInvariant())
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<string> FindMissingRequirements(
        string text,
        IReadOnlyList<ImplementationRequirement> requirements) =>
        requirements
            .Where(requirement => requirement.AnyKeywords.Count > 0 &&
                                  !requirement.AnyKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .Select(requirement => $"{requirement.Id}: {requirement.Description}")
            .ToList();

    private static IReadOnlyList<string> DiscoverFrontendImplementationFiles(string workspaceRoot)
    {
        if (!Directory.Exists(workspaceRoot))
        {
            return [];
        }

        var candidates = new[] { "App.jsx", "App.tsx", "styles.css", "style.css", "index.css" };
        return Directory
            .EnumerateFiles(workspaceRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => candidates.Any(candidate => path.EndsWith(candidate, StringComparison.OrdinalIgnoreCase)))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(workspaceRoot, path).Replace('\\', '/'))
            .Take(12)
            .ToList();
    }

    private static bool HasMeaningfulRootRender(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (DetectPlaceholders(text).Count > 0)
        {
            return false;
        }

        return text.Contains("id=\"root\"", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("id='root'", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("data-agentq-root", StringComparison.OrdinalIgnoreCase) ||
               text.Length >= 250;
    }
}
