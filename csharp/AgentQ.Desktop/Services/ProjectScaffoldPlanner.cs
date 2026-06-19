using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using AgentQ.Api;

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

        var intent = BuildIntent(normalized);
        if (string.IsNullOrWhiteSpace(intent.ProjectType))
        {
            intent.ProjectType = "generic";
            intent.Language = string.IsNullOrWhiteSpace(intent.Language) ? "javascript" : intent.Language;
            intent.Framework = string.IsNullOrWhiteSpace(intent.Framework) ? DefaultFrameworkForLanguage(intent.Language) : intent.Framework;
        }

        if (IsBareNewProjectWish(normalized, intent))
        {
            return new ProjectScaffoldPlanningResult
            {
                IsGreenfieldRequest = true,
                CanProceed = false,
                ClarifyingQuestion = "What kind of project should AgentQ create? Examples: React stock analysis site, portfolio homepage, Python data analysis tool, API server. (어떤 프로젝트를 만들까요? 예: React 주식 분석 사이트, 포트폴리오 홈페이지, Python 데이터 분석 도구, API 서버)",
                Intent = intent,
                Reasons = ["The request asks about creating a new project, but does not yet specify a project type, framework, or app form."]
            };
        }

        var plan = BuildPlan(intent);
        if (plan == null)
        {
            return new ProjectScaffoldPlanningResult
            {
                IsGreenfieldRequest = true,
                CanProceed = false,
                ClarifyingQuestion = $"A {intent.ProjectType} project is possible. Please tell me more about the framework or app form you'd like. ({intent.ProjectType} \uD504\uB85C\uC81D\uD2B8\uB85C \uC9C4\uD589\uD560 \uC218 \uC788\uC2B5\uB2C8\uB2E4. \uC0AC\uC6A9\uD560 \uD504\uB808\uC784\uC6CC\uD06C\uB098 \uC571 \uD615\uD0DC\uB97C \uC880 \uB354 \uC54C\uB824\uC8FC\uC138\uC694.)",
                Intent = intent,
                Reasons = ["Project type was detected, but no deterministic scaffold plan matched it."]
            };
        }

        if (workspaceState is DesktopWorkspaceScaffoldState.RunnableApp or DesktopWorkspaceScaffoldState.ExistingProject)
        {
            var projectDirectory = BuildSafeProjectDirectoryName(intent);
            plan = PrefixPlan(projectDirectory, plan);
            return new ProjectScaffoldPlanningResult
            {
                IsGreenfieldRequest = true,
                CanProceed = true,
                Intent = intent,
                Plan = plan,
                PlanHash = ComputePlanHash(intent, plan),
                Reasons =
                [
                    $"{intent.ProjectType} scaffold plan matched deterministic rules.",
                    $"Workspace state is {workspaceState}, so AgentQ will create the new project under {projectDirectory}/ instead of writing into the existing project root."
                ]
            };
        }

        return new ProjectScaffoldPlanningResult
        {
            IsGreenfieldRequest = true,
            CanProceed = true,
            Intent = intent,
            Plan = plan,
            PlanHash = ComputePlanHash(intent, plan),
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
        var createInput = JsonSerializer.Serialize(new
        {
            planId = result.PlanId,
            planHash = result.PlanHash,
            intent = new
            {
                projectType = result.Intent.ProjectType,
                language = result.Intent.Language,
                framework = result.Intent.Framework,
                style = result.Intent.Style
            },
            plan = new
            {
                name = result.Plan.Name,
                files = result.Plan.Files,
                verificationCommands = result.Plan.VerificationCommands
            },
            overwriteExistingFiles = false
        }, AgentQJsonOptions.Indented);
        var verifyInput = JsonSerializer.Serialize(new
        {
            planId = result.PlanId,
            planHash = result.PlanHash,
            plan = new
            {
                name = result.Plan.Name,
                files = result.Plan.Files,
                verificationCommands = result.Plan.VerificationCommands
            }
        }, AgentQJsonOptions.Indented);
        return
            "Project scaffold preflight plan:\n" +
            $"- planId: {result.PlanId}\n" +
            $"- projectType: {result.Intent.ProjectType}\n" +
            $"- language: {result.Intent.Language}\n" +
            $"- framework: {result.Intent.Framework}\n" +
            $"- style: {result.Intent.Style}\n" +
            $"- files: {files}\n" +
            $"- verificationCommands: {commands}\n" +
            $"- planHash: {result.PlanHash}\n" +
            "- Treat this plan as the deterministic scaffold direction. User-stated language/framework choices override worker recommendations.\n" +
            "- The JSON below is internal tool input. Do not show it to the user as the answer.\n" +
            "Use this exact tool sequence:\n" +
            "1. Call create_project_scaffold with:\n" +
            createInput + "\n" +
            "2. If create_project_scaffold returns succeeded=true, call verify_project_scaffold with:\n" +
            verifyInput + "\n" +
            "If create_project_scaffold reports existing file collisions, report the collision and ask before overwrite.";
    }

    public static string ComputePlanHash(ProjectScaffoldIntentModel intent, ProjectScaffoldPlanModel plan)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            intent = new
            {
                projectType = NormalizeHashValue(intent.ProjectType),
                language = NormalizeHashValue(intent.Language),
                framework = NormalizeHashValue(intent.Framework),
                style = NormalizeHashValue(intent.Style)
            },
            plan = new
            {
                name = NormalizeHashValue(plan.Name),
                files = plan.Files.Select(NormalizePathValue).ToArray(),
                verificationCommands = plan.VerificationCommands.Select(command => command.Trim()).ToArray()
            }
        });
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static bool VerifyPlanHash(
        ProjectScaffoldIntentModel intent,
        ProjectScaffoldPlanModel plan,
        string planHash) =>
        !string.IsNullOrWhiteSpace(planHash) &&
        string.Equals(ComputePlanHash(intent, plan), planHash.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeHashValue(string value) => value.Trim().ToLowerInvariant();

    private static string NormalizePathValue(string value) => value.Trim().Replace('\\', '/');

    private static ProjectScaffoldIntentModel BuildIntent(string normalized)
    {
        var intent = new ProjectScaffoldIntentModel
        {
            Language = DetectLanguage(normalized),
            Style = DetectStyle(normalized)
        };

        if (ContainsAny(normalized, "glossary", "terminology", "dictionary", "terms",
                "\uC6A9\uC5B4", "\uC6A9\uC5B4\uC9D1", "\uC0AC\uC804"))
        {
            intent.ProjectType = "glossary";
            intent.Framework = "vite-react";
            if (string.IsNullOrWhiteSpace(intent.Language))
            {
                intent.Language = "javascript";
            }

            intent.Framework = DefaultFrameworkForWebProject(intent.Language);
        }
        else if (ContainsAny(normalized, "wordbook", "vocabulary", "flashcard", "\uB2E8\uC5B4\uC7A5", "\uB2E8\uC5B4"))
        {
            intent.ProjectType = "wordbook";
            intent.Framework = "vite-react";
            if (string.IsNullOrWhiteSpace(intent.Language))
            {
                intent.Language = "javascript";
            }

            intent.Framework = DefaultFrameworkForWebProject(intent.Language);
        }
        else if (ContainsAny(normalized, "shopping", "shop", "store", "cart", "mall", "\uC1FC\uD551", "\uC7A5\uBC14\uAD6C\uB2C8", "\uC0C1\uC810"))
        {
            intent.ProjectType = "shopping-cart";
            intent.Framework = "vite-react";
            if (string.IsNullOrWhiteSpace(intent.Language))
            {
                intent.Language = "javascript";
            }

            intent.Framework = DefaultFrameworkForWebProject(intent.Language);
        }
        else if (ContainsAny(normalized, "blog", "\uBE14\uB85C\uADF8"))
        {
            intent.ProjectType = "blog";
            intent.Framework = "vite-react";
            if (string.IsNullOrWhiteSpace(intent.Language))
            {
                intent.Language = "javascript";
            }

            intent.Framework = DefaultFrameworkForWebProject(intent.Language);
        }
        else if (ContainsAny(normalized, "stock", "stocks", "equity", "trading", "investment",
                     "\uC8FC\uC2DD", "\uD22C\uC790", "\uC885\uBAA9") &&
                 ContainsAny(normalized, "analysis", "analyzer", "dashboard", "site", "website", "app",
                     "\uBD84\uC11D", "\uB300\uC2DC\uBCF4\uB4DC", "\uC0AC\uC774\uD2B8", "\uC6F9", "\uC571"))
        {
            intent.ProjectType = "stock-analysis";
            if (string.IsNullOrWhiteSpace(intent.Language))
            {
                intent.Language = "javascript";
            }

            intent.Framework = DefaultFrameworkForWebProject(intent.Language);
        }
        else if ((string.IsNullOrWhiteSpace(intent.Language) || IsJavaScriptOrTypeScript(intent.Language)) &&
                 ContainsAny(normalized, "portfolio", "homepage", "website", "site", "landingpage", "webpage",
                "\uD3EC\uD2B8\uD3F4\uB9AC\uC624", "\uD648\uD398\uC774\uC9C0", "\uC6F9\uC0AC\uC774\uD2B8", "\uC0AC\uC774\uD2B8", "\uB79C\uB529"))
        {
            intent.ProjectType = ContainsAny(normalized, "landingpage", "\uB79C\uB529") ? "landing-page" : "portfolio";
            intent.Framework = "vite-react";
            if (string.IsNullOrWhiteSpace(intent.Language))
            {
                intent.Language = "javascript";
            }

            intent.Framework = DefaultFrameworkForWebProject(intent.Language);
        }
        else if (ContainsAny(normalized, "dataanalysis", "datatool", "streamlit", "\uB370\uC774\uD130\uBD84\uC11D", "\uBD84\uC11D\uB3C4\uAD6C"))
        {
            intent.ProjectType = "data-analysis-tool";
            if (string.IsNullOrWhiteSpace(intent.Language))
            {
                intent.Language = "python";
            }

            intent.Framework = intent.Language.Equals("python", StringComparison.OrdinalIgnoreCase)
                ? ContainsAny(normalized, "streamlit") ? "streamlit" : "python-cli"
                : DefaultFrameworkForLanguage(intent.Language);
        }
        else if (ContainsAny(normalized, "api", "fastapi"))
        {
            intent.ProjectType = "api-server";
            if (string.IsNullOrWhiteSpace(intent.Language))
            {
                intent.Language = "python";
            }

            intent.Framework = intent.Language.Equals("python", StringComparison.OrdinalIgnoreCase)
                ? "fastapi"
                : DefaultFrameworkForLanguage(intent.Language);
        }
        else if (ContainsGameProjectRequest(normalized))
        {
            intent.ProjectType = "game";
            intent.Framework = DetectGameFramework(normalized);
            if (string.IsNullOrWhiteSpace(intent.Framework))
            {
                intent.Framework = "game-project";
            }
        }
        else if (HasExplicitStackOrLanguage(normalized, intent))
        {
            intent.ProjectType = "generic";
            if (string.IsNullOrWhiteSpace(intent.Language))
            {
                intent.Language = "javascript";
            }

            intent.Framework = DefaultFrameworkForLanguage(intent.Language);
        }

        return intent;
    }

    private static bool HasExplicitStackOrLanguage(string normalized, ProjectScaffoldIntentModel intent)
    {
        if (!string.IsNullOrWhiteSpace(intent.Language))
        {
            return true;
        }

        return ContainsAny(normalized,
            "react", "vite", "nextjs", "next", "vue", "svelte", "angular",
            "django", "flask", "fastapi", "streamlit", "spring", "springboot",
            "rails", "laravel", "flutter", "electron", "tauri", "unity",
            "unreal", "godot", "aspnet", "aspnetcore", "dotnet",
            "\uB9AC\uC561\uD2B8", "\uBE44\uD2B8", "\uB125\uC2A4\uD2B8", "\uC575\uADE4\uB7EC", "\uBDF0", "\uC2A4\uBCA8\uD2B8",
            "\uC7A5\uACE0", "\uD50C\uB77C\uC2A4\uD06C", "\uD328\uC2A4\uD2B8api", "\uC2A4\uD2B8\uB9BC\uB9BF",
            "\uC2A4\uD504\uB9C1", "\uD50C\uB7EC\uD130", "\uC720\uB2C8\uD2F0", "\uC5B8\uB9AC\uC5BC", "\uACE0\uB3C4", "\uB2F7\uB137");
    }

    private static bool ContainsGameProjectRequest(string normalized)
    {
        if (ContainsAny(normalized, "game", "\uAC8C\uC784"))
        {
            return true;
        }

        return ContainsAny(normalized, "unity", "unity3d", "unreal", "godot",
                   "\uC720\uB2C8\uD2F0", "\uC5B8\uB9AC\uC5BC", "\uACE0\uB3C4") &&
               ContainsAny(normalized, "engine", "script", "controller", "playercontroller",
                   "\uC5D4\uC9C4", "\uC2A4\uD06C\uB9BD\uD2B8", "\uCEE8\uD2B8\uB864\uB7EC");
    }

    private static string DetectGameFramework(string normalized)
    {
        if (ContainsAny(normalized, "unity", "unity3d", "\uC720\uB2C8\uD2F0"))
        {
            return "unity";
        }

        if (ContainsAny(normalized, "unreal", "\uC5B8\uB9AC\uC5BC"))
        {
            return "unreal";
        }

        if (ContainsAny(normalized, "godot", "\uACE0\uB3C4"))
        {
            return "godot";
        }

        return string.Empty;
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

        if (intent.Framework == "cpp-cmake")
        {
            return new ProjectScaffoldPlanModel
            {
                Name = "C++ CMake scaffold",
                Files = ["CMakeLists.txt", "include/app/app.hpp", "src/app.cpp", "src/main.cpp", "tests/app_test.cpp"],
                VerificationCommands = ["cmake -S . -B build", "cmake --build build"]
            };
        }

        if (intent.Framework == "go-module")
        {
            return new ProjectScaffoldPlanModel
            {
                Name = "Go module scaffold",
                Files = ["go.mod", "cmd/app/main.go", "internal/app/service.go", "internal/app/service_test.go"],
                VerificationCommands = ["go test ./..."]
            };
        }

        if (intent.Framework == "rust-cargo")
        {
            return new ProjectScaffoldPlanModel
            {
                Name = "Rust Cargo scaffold",
                Files = ["Cargo.toml", "src/lib.rs", "src/main.rs", "tests/app_integration.rs"],
                VerificationCommands = ["cargo fmt --check", "cargo test"]
            };
        }

        if (intent.Framework == "java-maven")
        {
            return new ProjectScaffoldPlanModel
            {
                Name = "Java Maven scaffold",
                Files = ["pom.xml", "src/main/java/app/App.java", "src/test/java/app/AppTest.java"],
                VerificationCommands = ["mvn test"]
            };
        }

        if (intent.Framework == "sql-migrations")
        {
            return new ProjectScaffoldPlanModel
            {
                Name = "SQL migration scaffold",
                Files = ["migrations/001_initial_schema.sql", "README.md"],
                VerificationCommands = []
            };
        }

        if (intent.Framework == "php-composer")
        {
            return new ProjectScaffoldPlanModel
            {
                Name = "PHP Composer scaffold",
                Files = ["composer.json", "src/App.php", "tests/AppTest.php"],
                VerificationCommands = ["composer test"]
            };
        }

        if (intent.Framework == "kotlin-gradle")
        {
            return new ProjectScaffoldPlanModel
            {
                Name = "Kotlin Gradle scaffold",
                Files = ["settings.gradle.kts", "build.gradle.kts", "src/main/kotlin/app/App.kt", "src/test/kotlin/app/AppTest.kt"],
                VerificationCommands = ["gradle test"]
            };
        }

        if (intent.Framework == "swift-package")
        {
            return new ProjectScaffoldPlanModel
            {
                Name = "Swift Package scaffold",
                Files = ["Package.swift", "Sources/App/App.swift", "Tests/AppTests/AppTests.swift"],
                VerificationCommands = ["swift test"]
            };
        }

        if (intent.Framework == "powershell-script")
        {
            return new ProjectScaffoldPlanModel
            {
                Name = "PowerShell automation scaffold",
                Files = ["scripts/app.ps1", "README.md"],
                VerificationCommands = ["pwsh -File scripts/app.ps1 -DryRun"]
            };
        }

        if (intent.Framework == "shell-script")
        {
            return new ProjectScaffoldPlanModel
            {
                Name = "Shell automation scaffold",
                Files = ["scripts/app.sh", "README.md"],
                VerificationCommands = ["bash scripts/app.sh"]
            };
        }

        if (intent.Framework == "r-analysis")
        {
            return new ProjectScaffoldPlanModel
            {
                Name = "R analysis scaffold",
                Files = ["DESCRIPTION", "R/app.R", "tests/testthat/test_app.R"],
                VerificationCommands = ["Rscript -e \"testthat::test_dir('tests')\""]
            };
        }

        return null;
    }

    private static ProjectScaffoldPlanModel PrefixPlan(string projectDirectory, ProjectScaffoldPlanModel plan)
    {
        var directory = SanitizeProjectDirectoryName(projectDirectory);
        return new ProjectScaffoldPlanModel
        {
            Name = $"{plan.Name} in {directory}",
            Files = plan.Files
                .Select(file => $"{directory}/{NormalizePathValue(file)}")
                .ToList(),
            VerificationCommands = plan.VerificationCommands
                .Select(command => command.Trim())
                .Where(command => !string.IsNullOrWhiteSpace(command))
                .Select(command => $"cmd /c cd {directory} && {command}")
                .ToList()
        };
    }

    private static string BuildSafeProjectDirectoryName(ProjectScaffoldIntentModel intent)
    {
        var baseName = intent.ProjectType.ToLowerInvariant() switch
        {
            "portfolio" => "portfolio-site",
            "landing-page" => "landing-page",
            "wordbook" => "wordbook-app",
            "glossary" => "glossary-site",
            "shopping-cart" => "shopping-cart-app",
            "blog" => "blog-site",
            "stock-analysis" => "stock-analysis-site",
            "data-analysis-tool" => "data-analysis-tool",
            "api-server" => "api-server",
            "generic" => intent.Framework.Equals("vite-react", StringComparison.OrdinalIgnoreCase)
                ? "react-app"
                : $"{intent.Language}-project",
            _ => intent.ProjectType
        };

        return SanitizeProjectDirectoryName(baseName);
    }

    private static string SanitizeProjectDirectoryName(string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();
        var sanitized = new string(chars);
        while (sanitized.Contains("--", StringComparison.Ordinal))
        {
            sanitized = sanitized.Replace("--", "-", StringComparison.Ordinal);
        }

        sanitized = sanitized.Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "new-project" : sanitized;
    }

    private static bool IsBareNewProjectWish(string normalized, ProjectScaffoldIntentModel intent)
    {
        if (!intent.ProjectType.Equals("generic", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsConsultativeProjectWish(normalized))
        {
            return false;
        }

        return !ContainsAny(normalized,
            "react", "vite", "nextjs", "next", "vue", "svelte", "angular",
            "portfolio", "homepage", "website", "site", "landingpage", "webpage", "webapp",
            "api", "dashboard", "stock", "stocks", "dataanalysis", "datatool", "blog",
            "\uB9AC\uC561\uD2B8", "\uBE44\uD2B8", "\uB125\uC2A4\uD2B8", "\uBDF0", "\uC2A4\uBCA8\uD2B8",
            "\uD3EC\uD2B8\uD3F4\uB9AC\uC624", "\uD648\uD398\uC774\uC9C0", "\uC6F9\uC0AC\uC774\uD2B8", "\uC0AC\uC774\uD2B8", "\uC6F9", "\uB79C\uB529",
            "\uC8FC\uC2DD", "\uBD84\uC11D", "\uB300\uC2DC\uBCF4\uB4DC", "\uBE14\uB85C\uADF8",
            "\uB370\uC774\uD130\uBD84\uC11D", "\uBD84\uC11D\uB3C4\uAD6C");
    }

    private static bool IsConsultativeProjectWish(string normalized)
    {
        return ContainsAny(normalized,
            "wanttocreate", "wanttomake", "wanttobuild",
            "thinkingaboutcreating", "howshould", "whatwouldbegood", "possible",
            "\uB9CC\uB4E4\uACE0\uC2F6", "\uB9CC\uB4E4\uACE0\uC2F6\uC740\uB370",
            "\uB9CC\uB4E4\uC5B4\uBCF4\uACE0\uC2F6", "\uB9CC\uB4E4\uC5B4\uBCF4\uACE0\uC2F6\uC740\uB370",
            "\uC0DD\uC131\uD558\uACE0\uC2F6", "\uD558\uACE0\uC2F6",
            "\uC5B4\uB5BB\uAC8C\uC88B", "\uC5B4\uB5A4\uAC8C\uC88B", "\uC5B4\uB5BB\uAC8C\uD558\uBA74",
            "\uAC00\uB2A5\uD560\uAE4C", "\uB420\uAE4C", "\uD574\uBCF4\uACE0\uC2F6");
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

        if (ContainsAny(normalized, "cplusplus", "cpp", "cxx", "ccmake", "c++", "\uC528\uD50C\uD50C", "\uC528\uD50C\uB7EC\uC2A4\uD50C\uB7EC\uC2A4"))
        {
            return "cpp";
        }

        if (ContainsGoLanguage(normalized))
        {
            return "go";
        }

        if (ContainsAny(normalized, "rust", "\uB7EC\uC2A4\uD2B8"))
        {
            return "rust";
        }

        if (ContainsAny(normalized, "java", "\uC790\uBC14"))
        {
            return "java";
        }

        if (ContainsAny(normalized, "sql", "postgres", "postgresql", "mysql", "sqlite", "\uC5D0\uC2A4\uD050\uC5D8", "\uB370\uC774\uD130\uBCA0\uC774\uC2A4"))
        {
            return "sql";
        }

        if (ContainsAny(normalized, "php", "\uD53C\uC5D0\uC774\uCE58\uD53C"))
        {
            return "php";
        }

        if (ContainsAny(normalized, "kotlin", "\uCF54\uD2C0\uB9B0"))
        {
            return "kotlin";
        }

        if (ContainsAny(normalized, "swift", "\uC2A4\uC704\uD504\uD2B8"))
        {
            return "swift";
        }

        if (ContainsAny(normalized, "powershell", "pwsh", "\uD30C\uC6CC\uC258"))
        {
            return "powershell";
        }

        if (ContainsAny(normalized, "shellscript", "bashscript", "bash", "zsh", "\uC258\uC2A4\uD06C\uB9BD\uD2B8", "\uBC30\uC2DC"))
        {
            return "shell";
        }

        if (ContainsAny(normalized, "rlang", "rscript", "rstudio", "rproject", "ranalysis", "\uC54C\uC5B8\uC5B4", "r\uBD84\uC11D"))
        {
            return "r";
        }

        return string.Empty;
    }

    private static string DefaultFrameworkForLanguage(string language)
    {
        return language.ToLowerInvariant() switch
        {
            "typescript" or "javascript" => "vite-react",
            "python" => "python-cli",
            "cpp" => "cpp-cmake",
            "go" => "go-module",
            "rust" => "rust-cargo",
            "java" => "java-maven",
            "sql" => "sql-migrations",
            "php" => "php-composer",
            "kotlin" => "kotlin-gradle",
            "swift" => "swift-package",
            "powershell" => "powershell-script",
            "shell" => "shell-script",
            "r" => "r-analysis",
            _ => "vite-react"
        };
    }

    private static string DefaultFrameworkForWebProject(string language)
    {
        return language.Equals("javascript", StringComparison.OrdinalIgnoreCase) ||
               language.Equals("typescript", StringComparison.OrdinalIgnoreCase)
            ? "vite-react"
            : DefaultFrameworkForLanguage(language);
    }

    private static bool IsJavaScriptOrTypeScript(string language)
    {
        return language.Equals("javascript", StringComparison.OrdinalIgnoreCase) ||
               language.Equals("typescript", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsGoLanguage(string normalized)
    {
        return normalized.Equals("go", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains(" go ", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("go ", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(" go", StringComparison.OrdinalIgnoreCase) ||
               ContainsAny(normalized, "golang", "goapi", "goservice", "gomodule", "goproject", "gowebsite", "goweb", "withgo", "usinggo", "ingo", "go\uB85C", "\uACE0\uB7AD", "\uACE0\uC5B8\uC5B4");
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
        if (IsExplicitFileOrCodeMutationRequest(normalized))
        {
            return false;
        }

        if (IsFeasibilityQuestion(normalized) && !ContainsImmediateCreateRequest(normalized))
        {
            return false;
        }

        if (ContainsAny(normalized,
                "\uACE0\uBBFC", "\uC0C1\uB2F4", "\uAC71\uC815", "\uD574\uC57C\uD560\uC9C0",
                "shouldi", "whether") &&
            !ContainsCreate(normalized))
        {
            return false;
        }

        if (workspaceState is DesktopWorkspaceScaffoldState.RunnableApp or DesktopWorkspaceScaffoldState.ExistingProject)
        {
            return ContainsCreate(normalized) &&
                   (HasStandaloneNewProjectPattern(normalized) || HasProjectFormKeyword(normalized));
        }

        // Empty workspace: allow both explicit creation verbs and standalone "new project" patterns
        var hasCreateVerb = ContainsCreate(normalized);
        var hasGreenfieldKeyword = ContainsAny(normalized, GreenfieldProjectKeywords);
        var hasStandaloneNewProjectPattern = HasStandaloneNewProjectPattern(normalized);

        return (hasCreateVerb && hasGreenfieldKeyword) || hasStandaloneNewProjectPattern;
    }

    private static bool IsExplicitFileOrCodeMutationRequest(string normalized)
    {
        var hasExplicitFileTarget = ContainsAny(normalized,
            ".cs", ".js", ".jsx", ".ts", ".tsx", ".json", ".md", ".txt",
            "\uD30C\uC77C", "\uCF54\uB4DC");
        var hasMutationVerb = ContainsAny(normalized,
            "fix", "edit", "modify", "update", "change", "implement", "write",
            "\uC218\uC815", "\uACE0\uCCD0", "\uBCC0\uACBD", "\uBC14\uAFD4", "\uAD6C\uD604", "\uC791\uC131", "\uC4F0");

        return hasExplicitFileTarget && hasMutationVerb;
    }

    private static bool HasStandaloneNewProjectPattern(string normalized)
    {
        return ContainsAny(normalized,
            "newproject", "newapp", "newweb", "newwebsite",
            "\uC0C8\uD504\uB85C\uC81D\uD2B8", "\uC0C8\uB85C\uC6B4\uD504\uB85C\uC81D\uD2B8", "\uC0C8\uC571", "\uC0C8\uC6F9", "\uC0C8\uC6F9\uC0AC\uC774\uD2B8");
    }

    private static bool HasProjectFormKeyword(string normalized)
    {
        return ContainsAny(normalized,
            "project", "app", "website", "site", "homepage", "landingpage", "webpage", "webapp", "api", "dashboard",
            "\uD504\uB85C\uC81D\uD2B8", "\uC571", "\uC6F9\uC0AC\uC774\uD2B8", "\uC0AC\uC774\uD2B8", "\uD648\uD398\uC774\uC9C0", "\uC6F9", "\uB79C\uB529", "\uB300\uC2DC\uBCF4\uB4DC");
    }

    private static bool ContainsCreate(string normalized)
    {
        return ContainsAny(normalized, "create", "make", "build", "generate", "scaffold", "implement", "write", "proceed", "continue", "goahead",
            "\uB9CC\uB4E4", "\uC0DD\uC131", "\uC9DC", "\uAD6C\uD604", "\uC791\uC131", "\uC9C4\uD589");
    }

    private static bool IsFeasibilityQuestion(string normalized)
    {
        return ContainsAny(normalized,
            "possible", "isitpossible", "canwe", "cani",
            "\uAC00\uB2A5\uD55C\uAC00", "\uAC00\uB2A5\uD560\uAE4C", "\uC218\uC788\uB294\uC9C0", "\uC218\uC788\uC744\uAE4C");
    }

    private static bool ContainsImmediateCreateRequest(string normalized)
    {
        return ContainsAny(normalized,
            "createnow", "makeitnow", "builditnow", "pleasecreate", "pleasebuild",
            "\uBC14\uB85C\uB9CC\uB4E4", "\uBC14\uB85C\uC0DD\uC131", "\uC774\uB300\uB85C\uB9CC\uB4E4",
            "\uB9CC\uB4E4\uC5B4\uC918", "\uC0DD\uC131\uD574\uC918", "\uAD6C\uD604\uD574\uC918", "\uC9C4\uD589\uD574\uC918", "\uC9C4\uD589\uD574");
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
            .Any(IsUserProjectEntry);
        return hasFiles ? DesktopWorkspaceScaffoldState.ExistingProject : DesktopWorkspaceScaffoldState.Empty;
    }

    private static bool IsUserProjectEntry(string path)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(name) ||
            IgnoredWorkspaceEntryNames.Contains(name))
        {
            return false;
        }

        if (File.Exists(path))
        {
            var file = new FileInfo(path);
            if (file.Length == 0 && IgnoredEmptyWorkspaceFileNames.Contains(name))
            {
                return false;
            }
        }

        return true;
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

    private static readonly HashSet<string> IgnoredWorkspaceEntryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".agentq",
        ".agents",
        ".codex",
        ".codex-build",
        ".agentq-verify"
    };

    private static readonly HashSet<string> IgnoredEmptyWorkspaceFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "cd",
        "dotnet"
    };

    private static readonly string[] GreenfieldProjectKeywords =
    [
        "project", "app", "portfolio", "homepage", "website", "site", "landingpage", "webpage",
        "api", "dataanalysis", "datatool",
        "stock", "stocks", "equity", "trading", "investment",
        "wordbook", "vocabulary", "flashcard",
        "glossary", "terminology", "dictionary", "terms",
        "shopping", "shop", "store", "cart", "mall",
        "\uC1FC\uD551", "\uC1FC\uD551\uBAB0", "\uC0C1\uC810", "\uC7A5\uBC14\uAD6C\uB2C8", "\uC758\uB958", "\uD328\uC158",
        "blog",
        "newproject", "newapp", "newweb", "newwebsite",
        "react", "nextjs", "next", "angular", "vue", "svelte",
        "webapp", "mobileapp", "mobile", "desktopapp", "desktop",
        "game", "unity", "unity3d", "unreal", "godot",
        "dotnet", "csharp", "c#", "aspnet", "aspnetcore",
        "spring", "springboot", "rails", "laravel", "django", "flask",
        "flutter", "ionic", "electron", "tauri", "wasm",
        "\uD504\uB85C\uC81D\uD2B8", "\uC571", "\uD3EC\uD2B8\uD3F4\uB9AC\uC624", "\uD648\uD398\uC774\uC9C0", "\uC6F9\uC0AC\uC774\uD2B8", "\uC0AC\uC774\uD2B8", "\uC6F9", "\uB79C\uB529",
        "\uC8FC\uC2DD", "\uD22C\uC790", "\uC885\uBAA9",
        "\uB370\uC774\uD130\uBD84\uC11D", "\uBD84\uC11D\uB3C4\uAD6C",
        "\uB2E8\uC5B4\uC7A5", "\uC6A9\uC5B4", "\uC6A9\uC5B4\uC9D1", "\uC0AC\uC804",
        "\uC1FC\uD551", "\uC7A5\uBC14\uAD6C\uB2C8", "\uC0C1\uC810",
        "\uBE14\uB85C\uADF8",
        "\uC0C8\uD504\uB85C\uC81D\uD2B8", "\uC0C8\uB85C\uC6B4\uD504\uB85C\uC81D\uD2B8", "\uC0C8\uC571", "\uC0C8\uC6F9", "\uC0C8\uC6F9\uC0AC\uC774\uD2B8",
        "\uB9AC\uC561\uD2B8", "\uB125\uC2A4\uD2B8", "\uC575\uADE4\uB7EC", "\uBDF0", "\uC2A4\uBCA8\uD2B8",
        "\uAC8C\uC784", "\uC720\uB2C8\uD2F0", "\uC5B8\uB9AC\uC5BC", "\uACE0\uB3C4",
        "\uB2F7\uB128", "\uC528\uC0E4\uD504", "\uC2A4\uD504\uB9C1", "\uD50C\uB7EC\uD130",
        "\uC804\uC790", "\uD0C0\uC6B0\uB9AC", "\uBAA8\uBC14\uC77C", "\uBAA8\uBC14\uC77C\uC571", "\uBAA8\uBC14\uC77C\uC571",
        "\uB370\uC2A4\uD06C\uD0D1", "\uC708\uB3C4\uC6B0\uC571", "\uB9E5\uC571", "\uB9AC\uB205\uC2A4\uC571"
    ];
}

public sealed class ProjectScaffoldPlanningResult
{
    public bool IsGreenfieldRequest { get; init; }

    public bool CanProceed { get; init; }

    public string ClarifyingQuestion { get; init; } = string.Empty;

    public ProjectScaffoldIntentModel? Intent { get; init; }

    public ProjectScaffoldPlanModel? Plan { get; init; }

    public string PlanHash { get; init; } = string.Empty;

    public string PlanId { get; init; } = string.Empty;

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
