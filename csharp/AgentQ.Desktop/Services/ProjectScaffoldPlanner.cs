using System.IO;

namespace AgentQ.Desktop.Services;

public sealed class ProjectScaffoldPlanner
{
    public ProjectScaffoldPlanningResult Plan(string userText, string workspaceRoot)
    {
        var normalized = Normalize(userText);
        var workspaceState = ClassifyWorkspace(workspaceRoot);
        var isGreenfield = IsGreenfieldRequest(normalized, workspaceState);
        if (!isGreenfield)
        {
            return new ProjectScaffoldPlanningResult
            {
                IsGreenfieldRequest = false,
                CanProceed = false,
                Reasons = ["Request is not a greenfield project scaffold request."]
            };
        }

        if (workspaceState is DesktopWorkspaceScaffoldState.RunnableApp or DesktopWorkspaceScaffoldState.ExistingProject)
        {
            return new ProjectScaffoldPlanningResult
            {
                IsGreenfieldRequest = true,
                CanProceed = false,
                ClarifyingQuestion = "현재 폴더에 이미 프로젝트 파일이 있습니다. 새 프로젝트를 덮어 만들지, 기존 프로젝트에 기능을 추가할지 알려주세요.",
                Reasons = [$"Workspace state is {workspaceState}, so project scaffold execution needs user direction."]
            };
        }

        var intent = BuildIntent(normalized);
        if (string.IsNullOrWhiteSpace(intent.ProjectType))
        {
            return new ProjectScaffoldPlanningResult
            {
                IsGreenfieldRequest = true,
                CanProceed = false,
                ClarifyingQuestion = "어떤 종류의 프로젝트를 원하시나요? 예: 포트폴리오 홈페이지, Python 데이터 분석 도구, 게임, API 서버, 단어장 웹앱.",
                Reasons = ["Project type is missing."]
            };
        }

        var plan = BuildPlan(intent);
        if (plan == null)
        {
            return new ProjectScaffoldPlanningResult
            {
                IsGreenfieldRequest = true,
                CanProceed = false,
                ClarifyingQuestion = $"{intent.ProjectType} 프로젝트로 진행할 수 있습니다. 사용할 프레임워크나 앱 형태를 조금 더 알려주세요.",
                Intent = intent,
                Reasons = ["Project type was detected, but no deterministic scaffold plan matched it."]
            };
        }

        return new ProjectScaffoldPlanningResult
        {
            IsGreenfieldRequest = true,
            CanProceed = true,
            Intent = intent,
            Plan = plan,
            Reasons = [$"{intent.ProjectType} scaffold plan matched deterministic rules."]
        };
    }

    public static string BuildPlanContext(ProjectScaffoldPlanningResult result)
    {
        if (!result.CanProceed || result.Intent == null || result.Plan == null)
        {
            return string.Empty;
        }

        var files = string.Join(", ", result.Plan.Files);
        var commands = result.Plan.VerificationCommands.Count == 0
            ? "none"
            : string.Join(", ", result.Plan.VerificationCommands);
        return
            "Project scaffold preflight plan:\n" +
            $"- projectType: {result.Intent.ProjectType}\n" +
            $"- language: {result.Intent.Language}\n" +
            $"- framework: {result.Intent.Framework}\n" +
            $"- style: {result.Intent.Style}\n" +
            $"- files: {files}\n" +
            $"- verificationCommands: {commands}\n" +
            "- Treat this plan as the deterministic scaffold direction. User-stated language/framework choices override worker recommendations.";
    }

    private static ProjectScaffoldIntentModel BuildIntent(string normalized)
    {
        var intent = new ProjectScaffoldIntentModel
        {
            Language = DetectLanguage(normalized),
            Style = DetectStyle(normalized)
        };

        if (ContainsAny(normalized, "portfolio", "homepage", "website", "landingpage", "webpage",
                "\uD3EC\uD2B8\uD3F4\uB9AC\uC624", "\uD648\uD398\uC774\uC9C0", "\uC6F9\uC0AC\uC774\uD2B8", "\uB79C\uB529"))
        {
            intent.ProjectType = ContainsAny(normalized, "landingpage", "\uB79C\uB529") ? "landing-page" : "portfolio";
            intent.Framework = "vite-react";
            if (string.IsNullOrWhiteSpace(intent.Language))
            {
                intent.Language = "javascript";
            }
        }
        else if (ContainsAny(normalized, "dataanalysis", "datatool", "streamlit", "\uB370\uC774\uD130\uBD84\uC11D", "\uBD84\uC11D\uB3C4\uAD6C"))
        {
            intent.ProjectType = "data-analysis-tool";
            intent.Language = "python";
            intent.Framework = ContainsAny(normalized, "streamlit") ? "streamlit" : "python-cli";
        }
        else if (ContainsAny(normalized, "api", "fastapi"))
        {
            intent.ProjectType = "api-server";
            intent.Language = "python";
            intent.Framework = "fastapi";
        }

        return intent;
    }

    private static ProjectScaffoldPlanModel? BuildPlan(ProjectScaffoldIntentModel intent)
    {
        if (intent.Framework == "vite-react")
        {
            var useTypeScript = intent.Language.Equals("typescript", StringComparison.OrdinalIgnoreCase);
            return new ProjectScaffoldPlanModel
            {
                Name = $"{intent.ProjectType} {intent.Framework} scaffold",
                Files = useTypeScript
                    ? ["package.json", "index.html", "vite.config.ts", "tsconfig.json", "src/main.tsx", "src/App.tsx", "src/styles.css"]
                    : ["package.json", "index.html", "vite.config.js", "src/main.jsx", "src/App.jsx", "src/styles.css"],
                VerificationCommands = ["npm install", "npm run build"]
            };
        }

        if (intent.Framework == "python-cli")
        {
            return new ProjectScaffoldPlanModel
            {
                Name = "Python data analysis CLI scaffold",
                Files = ["README.md", "requirements.txt", "src/main.py", "src/analyzer.py", "data/.gitkeep", "tests/test_analyzer.py"],
                VerificationCommands = ["python -m pytest"]
            };
        }

        if (intent.Framework == "streamlit")
        {
            return new ProjectScaffoldPlanModel
            {
                Name = "Streamlit data analysis scaffold",
                Files = ["README.md", "requirements.txt", "app.py", "data/.gitkeep"],
                VerificationCommands = ["python -m streamlit run app.py --server.headless true"]
            };
        }

        if (intent.Framework == "fastapi")
        {
            return new ProjectScaffoldPlanModel
            {
                Name = "FastAPI service scaffold",
                Files = ["README.md", "requirements.txt", "app/main.py", "app/routes.py", "tests/test_app.py"],
                VerificationCommands = ["python -m pytest"]
            };
        }

        return null;
    }

    private static string DetectLanguage(string normalized)
    {
        if (ContainsAny(normalized, "typescript", "\uD0C0\uC785\uC2A4\uD06C\uB9BD\uD2B8"))
        {
            return "typescript";
        }

        if (ContainsAny(normalized, "javascript", "\uC790\uBC14\uC2A4\uD06C\uB9BD\uD2B8"))
        {
            return "javascript";
        }

        if (ContainsAny(normalized, "python", "\uD30C\uC774\uC36C"))
        {
            return "python";
        }

        return string.Empty;
    }

    private static string DetectStyle(string normalized)
    {
        if (ContainsAny(normalized, "minimal", "\uBBF8\uB2C8\uBA40"))
        {
            return "minimal-modern";
        }

        if (ContainsAny(normalized, "future", "futuristic", "\uBBF8\uB798", "\uC0AC\uC774\uBC84"))
        {
            return "futuristic";
        }

        if (ContainsAny(normalized, "dark", "\uB2E4\uD06C"))
        {
            return "dark";
        }

        return "unspecified";
    }

    private static bool IsGreenfieldRequest(string normalized, DesktopWorkspaceScaffoldState workspaceState)
    {
        if (workspaceState is DesktopWorkspaceScaffoldState.RunnableApp or DesktopWorkspaceScaffoldState.ExistingProject)
        {
            return ContainsCreate(normalized) &&
                   ContainsAny(normalized, "newproject", "newapp", "\uC0C8\uB85C\uC6B4\uD504\uB85C\uC81D\uD2B8", "\uC0C8\uD504\uB85C\uC81D\uD2B8");
        }

        return ContainsCreate(normalized) &&
               ContainsAny(normalized,
                   "project", "app", "portfolio", "homepage", "website", "landingpage", "api", "dataanalysis", "datatool",
                   "\uD504\uB85C\uC81D\uD2B8", "\uC571", "\uD3EC\uD2B8\uD3F4\uB9AC\uC624", "\uD648\uD398\uC774\uC9C0", "\uC6F9\uC0AC\uC774\uD2B8",
                   "\uB370\uC774\uD130\uBD84\uC11D", "\uBD84\uC11D\uB3C4\uAD6C");
    }

    private static bool ContainsCreate(string normalized)
    {
        return ContainsAny(normalized, "create", "make", "build", "generate", "scaffold",
            "\uB9CC\uB4E4", "\uC0DD\uC131", "\uC9DC", "\uD558\uC790");
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

    private static string Normalize(string value)
    {
        return value.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class ProjectScaffoldPlanningResult
{
    public bool IsGreenfieldRequest { get; init; }

    public bool CanProceed { get; init; }

    public string ClarifyingQuestion { get; init; } = string.Empty;

    public ProjectScaffoldIntentModel? Intent { get; init; }

    public ProjectScaffoldPlanModel? Plan { get; init; }

    public IReadOnlyList<string> Reasons { get; init; } = [];
}

public sealed class ProjectScaffoldIntentModel
{
    public string ProjectType { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public string Framework { get; set; } = string.Empty;

    public string Style { get; set; } = string.Empty;
}

public sealed class ProjectScaffoldPlanModel
{
    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<string> Files { get; init; } = [];

    public IReadOnlyList<string> VerificationCommands { get; init; } = [];
}
