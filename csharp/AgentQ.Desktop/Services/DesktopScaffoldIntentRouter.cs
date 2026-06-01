using System.IO;

namespace AgentQ.Desktop.Services;

public sealed class DesktopScaffoldIntentRouter
{
    public DesktopScaffoldIntent Analyze(string userText, string workspaceRoot)
    {
        var normalized = Normalize(userText);
        return new DesktopScaffoldIntent
        {
            Kind = ClassifyIntent(normalized),
            WorkspaceState = ClassifyWorkspace(workspaceRoot)
        };
    }

    public bool ShouldHandleLocally(string userText, string workspaceRoot)
    {
        var intent = Analyze(userText, workspaceRoot);
        return intent.Kind == DesktopScaffoldIntentKind.CreateProject &&
               intent.WorkspaceState is DesktopWorkspaceScaffoldState.Empty
                   or DesktopWorkspaceScaffoldState.PackageOnly;
    }

    public bool ShouldAskForProjectBrief(string userText, string workspaceRoot)
    {
        var intent = Analyze(userText, workspaceRoot);
        if (intent.Kind != DesktopScaffoldIntentKind.CreateProject ||
            intent.WorkspaceState is not (DesktopWorkspaceScaffoldState.Empty or DesktopWorkspaceScaffoldState.PackageOnly))
        {
            return false;
        }

        var normalized = Normalize(userText);
        if (!ContainsAny(
                normalized,
                "\uD3EC\uD2B8\uD3F4\uB9AC\uC624",
                "\uD648\uD398\uC774\uC9C0",
                "\uC6F9\uC0AC\uC774\uD2B8",
                "\uC6F9\uD398\uC774\uC9C0",
                "portfolio",
                "homepage",
                "website"))
        {
            return false;
        }

        return !ContainsAny(
            normalized,
            "\uCEE8\uC149",
            "\uB514\uC790\uC778",
            "\uC139\uC158",
            "\uAE30\uB2A5",
            "\uC18C\uAC1C",
            "\uD504\uB85C\uC81D\uD2B8",
            "\uC2A4\uD0DD",
            "\uC2A4\uD0C0\uC77C",
            "\uCEEC\uB7EC",
            "concept",
            "design",
            "section",
            "feature",
            "about",
            "project",
            "stack",
            "style",
            "color");
    }

    public WorkerScaffoldRecommendation SelectRecommendation(
        IReadOnlyList<WorkerScaffoldRecommendation> recommendations,
        string? userText,
        string workspaceRoot)
    {
        if (recommendations.Count == 0)
        {
            throw new ArgumentException("At least one scaffold recommendation is required.", nameof(recommendations));
        }

        var intent = Analyze(userText ?? string.Empty, workspaceRoot);
        return recommendations
            .OrderByDescending(recommendation => Score(recommendation, intent))
            .ThenBy(recommendation => recommendation.Name, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static int Score(WorkerScaffoldRecommendation recommendation, DesktopScaffoldIntent intent)
    {
        var isProject = IsProjectScaffold(recommendation);
        var isFeature = IsFeatureScaffold(recommendation);
        var score = 0;

        score += intent.Kind switch
        {
            DesktopScaffoldIntentKind.CreateProject => isProject ? 120 : -80,
            DesktopScaffoldIntentKind.CreateFeature => isFeature ? 80 : 0,
            _ => 0
        };

        score += intent.WorkspaceState switch
        {
            DesktopWorkspaceScaffoldState.Empty => isProject ? 80 : -40,
            DesktopWorkspaceScaffoldState.PackageOnly => isProject ? 70 : -30,
            DesktopWorkspaceScaffoldState.RunnableApp => isFeature ? 50 : -50,
            DesktopWorkspaceScaffoldState.ExistingProject => isFeature ? 35 : 0,
            _ => 0
        };

        if (recommendation.VerificationCommands.Any(command =>
                command.Contains("npm run build", StringComparison.OrdinalIgnoreCase)))
        {
            score += 5;
        }

        return score;
    }

    private static DesktopScaffoldIntentKind ClassifyIntent(string normalized)
    {
        var create = ContainsAny(
            normalized,
            "\uC0DD\uC131",
            "\uB9CC\uB4E4",
            "\uB9CC\uB4E4\uC5B4",
            "create",
            "generate",
            "scaffold",
            "build");
        if (!create)
        {
            return DesktopScaffoldIntentKind.None;
        }

        if (ContainsAny(
                normalized,
                "\uD3EC\uD2B8\uD3F4\uB9AC\uC624",
                "\uD648\uD398\uC774\uC9C0",
                "\uC6F9\uC0AC\uC774\uD2B8",
                "\uC6F9\uD398\uC774\uC9C0",
                "\uD504\uB85C\uC81D\uD2B8",
                "\uC571",
                "\uC0C8\uD504\uB85C\uC81D\uD2B8",
                "\uC0C8\uC571",
                "portfolio",
                "homepage",
                "website",
                "project",
                "app",
                "newproject",
                "newapp"))
        {
            return DesktopScaffoldIntentKind.CreateProject;
        }

        if (ContainsAny(
                normalized,
                "feature",
                "\uAE30\uB2A5",
                "\uD398\uC774\uC9C0",
                "\uCEF4\uD3EC\uB10C\uD2B8",
                "component",
                "route"))
        {
            return DesktopScaffoldIntentKind.CreateFeature;
        }

        return DesktopScaffoldIntentKind.None;
    }

    private static DesktopWorkspaceScaffoldState ClassifyWorkspace(string workspaceRoot)
    {
        var root = string.IsNullOrWhiteSpace(workspaceRoot)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(workspaceRoot);

        if (!Directory.Exists(root))
        {
            return DesktopWorkspaceScaffoldState.Empty;
        }

        var packageJson = File.Exists(Path.Combine(root, "package.json"));
        var hasRunnableEntry =
            File.Exists(Path.Combine(root, "index.html")) &&
            (File.Exists(Path.Combine(root, "src", "main.tsx")) ||
             File.Exists(Path.Combine(root, "src", "main.jsx")) ||
             File.Exists(Path.Combine(root, "src", "main.ts")) ||
             File.Exists(Path.Combine(root, "src", "main.js")) ||
             File.Exists(Path.Combine(root, "src", "App.tsx")) ||
             File.Exists(Path.Combine(root, "src", "App.jsx")));

        if (hasRunnableEntry)
        {
            return DesktopWorkspaceScaffoldState.RunnableApp;
        }

        if (packageJson)
        {
            return DesktopWorkspaceScaffoldState.PackageOnly;
        }

        var hasFiles = Directory.EnumerateFileSystemEntries(root)
            .Any(path => !string.Equals(Path.GetFileName(path), ".git", StringComparison.OrdinalIgnoreCase));
        return hasFiles ? DesktopWorkspaceScaffoldState.ExistingProject : DesktopWorkspaceScaffoldState.Empty;
    }

    private static bool IsProjectScaffold(WorkerScaffoldRecommendation recommendation)
    {
        return recommendation.Files.Any(file => file.Equals("package.json", StringComparison.OrdinalIgnoreCase)) &&
               recommendation.Files.Any(file => file.Equals("index.html", StringComparison.OrdinalIgnoreCase)) &&
               recommendation.Files.Any(file => file.Equals("src/main.tsx", StringComparison.OrdinalIgnoreCase) ||
                                                file.Equals("src/main.jsx", StringComparison.OrdinalIgnoreCase) ||
                                                file.Equals("src/main.ts", StringComparison.OrdinalIgnoreCase) ||
                                                file.Equals("src/main.js", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsFeatureScaffold(WorkerScaffoldRecommendation recommendation)
    {
        return recommendation.Files.Any(file =>
            file.Contains("<feature>", StringComparison.OrdinalIgnoreCase) ||
            file.Contains("<feature_dir>", StringComparison.OrdinalIgnoreCase) ||
            file.Contains("features/", StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string value)
    {
        return value.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class DesktopScaffoldIntent
{
    public DesktopScaffoldIntentKind Kind { get; init; }

    public DesktopWorkspaceScaffoldState WorkspaceState { get; init; }
}

public enum DesktopScaffoldIntentKind
{
    None,
    CreateProject,
    CreateFeature
}

public enum DesktopWorkspaceScaffoldState
{
    Empty,
    PackageOnly,
    RunnableApp,
    ExistingProject
}
