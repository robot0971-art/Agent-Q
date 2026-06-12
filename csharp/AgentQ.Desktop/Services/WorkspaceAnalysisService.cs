using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace AgentQ.Desktop.Services;

public sealed class WorkspaceAnalysisService
{
    private readonly WorkspaceSymbolIndexService _symbolIndexService;
    private readonly WorkspaceDependencyGraphService _dependencyGraphService;
    private readonly CSharpRoslynAnalysisService _csharpRoslynAnalysisService;
    private readonly DesktopDiagnosticsService? _diagnosticsService;

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".agentq",
        ".agents",
        ".codex",
        ".agentq-verify",
        ".vs",
        "bin",
        "obj",
        "node_modules",
        "artifacts",
        ".codex-build",
        ".venv",
        "venv",
        "env",
        "__pycache__"
    };

    public WorkspaceAnalysisService(
        WorkspaceSymbolIndexService? symbolIndexService = null,
        WorkspaceDependencyGraphService? dependencyGraphService = null,
        CSharpRoslynAnalysisService? csharpRoslynAnalysisService = null,
        DesktopDiagnosticsService? diagnosticsService = null)
    {
        _symbolIndexService = symbolIndexService ?? new WorkspaceSymbolIndexService();
        _dependencyGraphService = dependencyGraphService ?? new WorkspaceDependencyGraphService();
        _csharpRoslynAnalysisService = csharpRoslynAnalysisService ?? new CSharpRoslynAnalysisService();
        _diagnosticsService = diagnosticsService;
    }

    public async Task<WorkspaceAnalysis> AnalyzeAsync(string workspaceRoot, CancellationToken ct = default)
    {
        var analysis = new WorkspaceAnalysis
        {
            WorkspaceRoot = string.IsNullOrWhiteSpace(workspaceRoot)
                ? Environment.CurrentDirectory
                : Path.GetFullPath(workspaceRoot)
        };

        if (!Directory.Exists(analysis.WorkspaceRoot))
        {
            analysis.ProjectType = "Missing folder";
            analysis.Framework = "Unavailable";
            analysis.Hints.Add("Workspace folder does not exist.");
            RecordAnalysisEvent("workspace_analysis_missing", analysis, "Workspace folder does not exist.");
            return analysis;
        }

        RecordAnalysisEvent("workspace_analysis_started", analysis, "Starting workspace analysis.");
        var detectedTypes = new List<string>();
        var frameworks = new List<string>();

        CountWorkspaceItems(analysis);
        DetectGit(analysis);
        DetectCommandFiles(analysis);
        DetectDotNet(analysis, detectedTypes, frameworks);
        await DetectNodeAsync(analysis, detectedTypes, frameworks, ct);
        DetectPython(analysis, detectedTypes, frameworks);
        DetectCpp(analysis, detectedTypes, frameworks);
        DetectGo(analysis, detectedTypes, frameworks);
        DetectRust(analysis, detectedTypes, frameworks);
        await DetectNativeWorkerAsync(analysis, detectedTypes, frameworks, ct);
        DetectUnity(analysis, detectedTypes, frameworks);
        DetectUnreal(analysis, detectedTypes, frameworks);
        DetectDocker(analysis, detectedTypes, frameworks);
        DetectDatabaseTooling(analysis, frameworks);
        DetectMonorepoShape(analysis);
        DetectProjectMap(analysis, detectedTypes);
        DetectSymbols(analysis);
        DetectDependencyGraph(analysis);
        DetectCSharpRoslyn(analysis);
        await DetectTypeScriptWorkerAsync(analysis, frameworks, ct);
        await DetectPythonWorkerAsync(analysis, frameworks, ct);
        DetectKeyFiles(analysis);

        analysis.ProjectType = detectedTypes.Count > 0
            ? string.Join(", ", detectedTypes.Distinct(StringComparer.OrdinalIgnoreCase))
            : "Unknown";
        analysis.Framework = frameworks.Count > 0
            ? string.Join(", ", frameworks.Distinct(StringComparer.OrdinalIgnoreCase).Take(8))
            : "Unknown";

        if (analysis.VerificationCommands.Count == 0)
        {
            analysis.Hints.Add("No obvious build or test command detected.");
        }

        await CheckSystemDependenciesAsync(analysis, ct);

        RecordAnalysisEvent(
            "workspace_analysis_completed",
            analysis,
            $"projectType={analysis.ProjectType}; framework={analysis.Framework}; files={analysis.FileCount:0}; directories={analysis.DirectoryCount:0}; hints={analysis.Hints.Count:0}; scaffoldRecommendations={analysis.ScaffoldRecommendations.Count:0}");

        return analysis;
    }

    private void RecordAnalysisEvent(string eventType, WorkspaceAnalysis analysis, string detail) =>
        _diagnosticsService?.Record(eventType, detail, analysis.WorkspaceRoot);

    private void RecordWorkerEvent(
        string eventType,
        WorkspaceAnalysis analysis,
        string workerName,
        string detail,
        Exception? exception = null) =>
        _diagnosticsService?.Record(
            eventType,
            $"worker={workerName}; {detail}",
            analysis.WorkspaceRoot,
            exception: exception);

    private static void CountWorkspaceItems(WorkspaceAnalysis analysis)
    {
        var pending = new Stack<string>();
        pending.Push(analysis.WorkspaceRoot);

        while (pending.Count > 0 && analysis.FileCount < 5000)
        {
            var current = pending.Pop();

            try
            {
                foreach (var file in Directory.EnumerateFiles(current))
                {
                    analysis.FileCount++;
                    if (analysis.FileCount >= 5000)
                    {
                        break;
                    }
                }

                foreach (var directory in Directory.EnumerateDirectories(current))
                {
                    if (!CanTraverseDirectory(analysis.WorkspaceRoot, directory))
                    {
                        continue;
                    }

                    analysis.DirectoryCount++;
                    pending.Push(directory);
                }
            }
            catch
            {
                // Partial counts are still useful for a dashboard.
            }
        }

        if (analysis.FileCount >= 5000)
        {
            analysis.Hints.Add("Workspace scan stopped at 5000 files.");
        }
    }

    private static void DetectGit(WorkspaceAnalysis analysis)
    {
        if (!Directory.Exists(Path.Combine(analysis.WorkspaceRoot, ".git")))
        {
            return;
        }

        var branch = RunGit(analysis.WorkspaceRoot, "branch --show-current");
        if (string.IsNullOrWhiteSpace(branch))
        {
            branch = RunGit(analysis.WorkspaceRoot, "rev-parse --short HEAD");
        }

        analysis.GitBranch = string.IsNullOrWhiteSpace(branch)
            ? "Git repository"
            : branch.Trim();
        analysis.Hints.Add("Git repository detected.");
    }

    private static void DetectCommandFiles(WorkspaceAnalysis analysis)
    {
        AddCommandIfExists(analysis, "build.desktop.cmd", "cmd /c build.desktop.cmd");
        AddCommandIfExists(analysis, "build.cmd", "cmd /c build.cmd");
        AddCommandIfExists(analysis, "test.cmd", "cmd /c test.cmd");
    }

    private static void DetectDotNet(
        WorkspaceAnalysis analysis,
        List<string> detectedTypes,
        List<string> frameworks)
    {
        var solution = Directory.EnumerateFiles(analysis.WorkspaceRoot, "*.sln", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(analysis.WorkspaceRoot, "*.slnx", SearchOption.TopDirectoryOnly))
            .Select(Path.GetFileName)
            .FirstOrDefault();
        var projects = SafeEnumerateFiles(analysis.WorkspaceRoot, "*.csproj").Take(12).ToList();

        if (solution == null && projects.Count == 0)
        {
            return;
        }

        detectedTypes.Add(".NET");
        if (!string.IsNullOrWhiteSpace(solution))
        {
            analysis.Hints.Add($"Solution file: {solution}");
            AddUnique(analysis.VerificationCommands, "dotnet build");
            AddUnique(analysis.VerificationCommands, "dotnet test");
        }

        foreach (var project in projects)
        {
            foreach (var target in ReadTargetFrameworks(project))
            {
                frameworks.Add(target);
            }
        }

        if (frameworks.Count == 0)
        {
            frameworks.Add(".NET");
        }
    }

    private static async Task DetectNodeAsync(
        WorkspaceAnalysis analysis,
        List<string> detectedTypes,
        List<string> frameworks,
        CancellationToken ct)
    {
        var packageJson = FindProjectFile(analysis.WorkspaceRoot, "package.json");
        if (packageJson == null)
        {
            return;
        }

        detectedTypes.Add("Node");
        var projectDirectory = Path.GetDirectoryName(packageJson) ?? analysis.WorkspaceRoot;
        var projectRelativePath = Path.GetRelativePath(analysis.WorkspaceRoot, projectDirectory);
        analysis.Hints.Add(projectDirectory.Equals(analysis.WorkspaceRoot, StringComparison.OrdinalIgnoreCase)
            ? "package.json detected."
            : $"package.json detected in {projectRelativePath}.");

        try
        {
            await using var stream = File.OpenRead(packageJson);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = document.RootElement;
            DetectNodeFrameworks(root, frameworks);
            DetectNodeScripts(root, analysis, projectRelativePath);
        }
        catch
        {
            frameworks.Add("Node");
            AddNodeCommand(analysis, projectRelativePath, "npm test");
            AddNodeCommand(analysis, projectRelativePath, "npm run build");
        }
    }

    private static void DetectNodeFrameworks(JsonElement root, List<string> frameworks)
    {
        var dependencyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddDependencyNames(root, "dependencies", dependencyNames);
        AddDependencyNames(root, "devDependencies", dependencyNames);

        if (dependencyNames.Contains("next"))
        {
            frameworks.Add("Next.js");
        }
        else if (dependencyNames.Contains("@vitejs/plugin-react") || dependencyNames.Contains("vite"))
        {
            frameworks.Add("Vite");
        }

        if (dependencyNames.Contains("react"))
        {
            frameworks.Add("React");
        }

        if (dependencyNames.Contains("typescript"))
        {
            frameworks.Add("TypeScript");
        }

        if (frameworks.Count == 0)
        {
            frameworks.Add("Node");
        }
    }

    private static void DetectPythonFrameworks(string root, List<string> frameworks)
    {
        var requirementsPath = Path.Combine(root, "requirements.txt");
        var pyprojectPath = Path.Combine(root, "pyproject.toml");
        var text = new StringBuilder();

        TryAppendFileText(requirementsPath, text);
        TryAppendFileText(pyprojectPath, text);

        var content = text.ToString();
        if (content.Contains("fastapi", StringComparison.OrdinalIgnoreCase))
        {
            frameworks.Add("FastAPI");
        }

        if (content.Contains("django", StringComparison.OrdinalIgnoreCase))
        {
            frameworks.Add("Django");
        }

        if (content.Contains("flask", StringComparison.OrdinalIgnoreCase))
        {
            frameworks.Add("Flask");
        }

        if (content.Contains("sqlalchemy", StringComparison.OrdinalIgnoreCase))
        {
            frameworks.Add("SQLAlchemy");
        }

        if (content.Contains("pytest", StringComparison.OrdinalIgnoreCase))
        {
            frameworks.Add("pytest");
        }

        if (content.Contains("celery", StringComparison.OrdinalIgnoreCase))
        {
            frameworks.Add("Celery");
        }

        if (content.Contains("click", StringComparison.OrdinalIgnoreCase))
        {
            frameworks.Add("Click");
        }
    }

    private static void DetectNodeScripts(JsonElement root, WorkspaceAnalysis analysis, string projectRelativePath)
    {
        if (!root.TryGetProperty("scripts", out var scripts) || scripts.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (scripts.TryGetProperty("test", out _))
        {
            AddNodeCommand(analysis, projectRelativePath, "npm test");
        }

        if (scripts.TryGetProperty("build", out _))
        {
            AddNodeCommand(analysis, projectRelativePath, "npm run build");
        }

        if (scripts.TryGetProperty("lint", out _))
        {
            AddNodeCommand(analysis, projectRelativePath, "npm run lint");
        }
    }

    private static void DetectUnity(
        WorkspaceAnalysis analysis,
        List<string> detectedTypes,
        List<string> frameworks)
    {
        var projectSettings = Path.Combine(analysis.WorkspaceRoot, "ProjectSettings");
        var assets = Path.Combine(analysis.WorkspaceRoot, "Assets");
        if (!Directory.Exists(projectSettings) || !Directory.Exists(assets))
        {
            return;
        }

        detectedTypes.Add("Unity");
        var versionPath = Path.Combine(projectSettings, "ProjectVersion.txt");
        var version = ReadUnityVersion(versionPath);
        frameworks.Add(string.IsNullOrWhiteSpace(version) ? "Unity" : $"Unity {version}");
        analysis.Hints.Add("Unity project structure detected.");
        DetectUnityDetails(analysis, frameworks);
    }

    private static void DetectUnityDetails(WorkspaceAnalysis analysis, List<string> frameworks)
    {
        var assetsRoot = Path.Combine(analysis.WorkspaceRoot, "Assets");
        var packagesRoot = Path.Combine(analysis.WorkspaceRoot, "Packages");
        var projectSettingsRoot = Path.Combine(analysis.WorkspaceRoot, "ProjectSettings");

        AddUnityProjectMapEntry(analysis, "Unity assets", "Assets");
        AddUnityProjectMapEntry(analysis, "Unity packages", "Packages");
        AddUnityProjectMapEntry(analysis, "Unity project settings", "ProjectSettings");

        var scenes = SafeEnumerateFiles(assetsRoot, "*.unity")
            .Select(path => NormalizeRelativePath(Path.GetRelativePath(analysis.WorkspaceRoot, path)))
            .Take(8)
            .ToList();
        var prefabs = SafeEnumerateFiles(assetsRoot, "*.prefab")
            .Select(path => NormalizeRelativePath(Path.GetRelativePath(analysis.WorkspaceRoot, path)))
            .Take(8)
            .ToList();
        var scripts = SafeEnumerateFiles(assetsRoot, "*.cs")
            .Select(path => NormalizeRelativePath(Path.GetRelativePath(analysis.WorkspaceRoot, path)))
            .Take(8)
            .ToList();
        var asmdefs = SafeEnumerateFiles(assetsRoot, "*.asmdef")
            .Select(path => NormalizeRelativePath(Path.GetRelativePath(analysis.WorkspaceRoot, path)))
            .Take(8)
            .ToList();

        AddUnityRole(analysis, "Unity scenes", scenes);
        AddUnityRole(analysis, "Unity prefabs", prefabs);
        AddUnityRole(analysis, "Unity scripts", scripts);
        AddUnityRole(analysis, "Unity asmdefs", asmdefs);

        AddUniqueRange(analysis.KeyFiles, scenes.Take(4));
        AddUniqueRange(analysis.KeyFiles, prefabs.Take(4));
        AddUniqueRange(analysis.KeyFiles, scripts.Take(4));
        AddUniqueRange(analysis.KeyFiles, asmdefs.Take(4));

        foreach (var asmdef in asmdefs.Take(6))
        {
            var name = ReadUnityAsmdefName(Path.Combine(analysis.WorkspaceRoot, asmdef));
            if (!string.IsNullOrWhiteSpace(name))
            {
                AddUnique(analysis.KeySymbols, $"unity assembly {name} ({asmdef})");
            }
        }

        var manifestPath = Path.Combine(packagesRoot, "manifest.json");
        if (File.Exists(manifestPath))
        {
            AddUnique(analysis.KeyFiles, "Packages/manifest.json");
            foreach (var packageName in ReadUnityPackageNames(manifestPath).Take(8))
            {
                AddUnique(analysis.KeyDependencies, $"Packages/manifest.json -> {packageName} (Unity package)");
                AddUnityFramework(frameworks, packageName);
            }
        }

        var buildSettingsPath = Path.Combine(projectSettingsRoot, "EditorBuildSettings.asset");
        var buildScenes = ReadUnityBuildSettingsScenes(buildSettingsPath).Take(8).ToList();
        if (buildScenes.Count > 0)
        {
            AddUnique(analysis.KeyFiles, "ProjectSettings/EditorBuildSettings.asset");
            foreach (var scene in buildScenes)
            {
                AddUnique(analysis.KeyDependencies, $"ProjectSettings/EditorBuildSettings.asset -> {scene} (build scene)");
            }
        }

        if (scenes.Count > 0 || prefabs.Count > 0 || scripts.Count > 0 || asmdefs.Count > 0)
        {
            analysis.Hints.Add($"Unity assets indexed: {scenes.Count:0} scenes, {prefabs.Count:0} prefabs, {scripts.Count:0} scripts, {asmdefs.Count:0} asmdefs.");
        }

        analysis.Hints.Add("Unity verification hint: run EditMode/PlayMode tests from Unity Test Runner or Unity batchmode when configured.");
    }

    private static void DetectUnreal(
        WorkspaceAnalysis analysis,
        List<string> detectedTypes,
        List<string> frameworks)
    {
        var uproject = Directory.EnumerateFiles(analysis.WorkspaceRoot, "*.uproject", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(uproject))
        {
            return;
        }

        detectedTypes.Add("Unreal");
        frameworks.Add("Unreal Engine");
        analysis.Hints.Add($"Unreal project file: {uproject}");
    }

    private static void DetectPython(
        WorkspaceAnalysis analysis,
        List<string> detectedTypes,
        List<string> frameworks)
    {
        var pyproject = FindProjectFile(analysis.WorkspaceRoot, "pyproject.toml");
        var requirements = FindProjectFile(analysis.WorkspaceRoot, "requirements.txt");
        if (pyproject == null && requirements == null)
        {
            return;
        }

        detectedTypes.Add("Python");
        var projectFile = pyproject ?? requirements!;
        var projectDirectory = Path.GetDirectoryName(projectFile) ?? analysis.WorkspaceRoot;
        var projectRelativePath = Path.GetRelativePath(analysis.WorkspaceRoot, projectDirectory);
        var frameworkCountBeforePython = frameworks.Count;
        DetectPythonFrameworks(projectDirectory, frameworks);
        if (frameworks.Count == frameworkCountBeforePython)
        {
            frameworks.Add("Python");
        }

        if (File.Exists(Path.Combine(projectDirectory, "pytest.ini")) ||
            File.Exists(Path.Combine(projectDirectory, "pyproject.toml")) ||
            Directory.Exists(Path.Combine(projectDirectory, "tests")) ||
            Directory.Exists(Path.Combine(projectDirectory, "test")))
        {
            AddPythonCommand(analysis, projectRelativePath, "python -m pytest");
        }

        analysis.Hints.Add(projectDirectory.Equals(analysis.WorkspaceRoot, StringComparison.OrdinalIgnoreCase)
            ? "Python project files detected."
            : $"Python project files detected in {projectRelativePath}.");
    }

    private static void DetectCpp(
        WorkspaceAnalysis analysis,
        List<string> detectedTypes,
        List<string> frameworks)
    {
        var hasCMake = File.Exists(Path.Combine(analysis.WorkspaceRoot, "CMakeLists.txt"));
        var vcxproj = SafeEnumerateFiles(analysis.WorkspaceRoot, "*.vcxproj").Take(2).ToList();
        var cppFiles = SafeEnumerateFiles(analysis.WorkspaceRoot, "*.cpp")
            .Concat(SafeEnumerateFiles(analysis.WorkspaceRoot, "*.cc"))
            .Concat(SafeEnumerateFiles(analysis.WorkspaceRoot, "*.cxx"))
            .Take(2)
            .ToList();
        var headerFiles = SafeEnumerateFiles(analysis.WorkspaceRoot, "*.h")
            .Concat(SafeEnumerateFiles(analysis.WorkspaceRoot, "*.hpp"))
            .Take(2)
            .ToList();

        if (!hasCMake && vcxproj.Count == 0 && cppFiles.Count == 0 && headerFiles.Count == 0)
        {
            return;
        }

        detectedTypes.Add("C++");
        if (hasCMake)
        {
            frameworks.Add("CMake");
            AddUnique(analysis.VerificationCommands, "cmake -S . -B build");
            AddUnique(analysis.VerificationCommands, "cmake --build build");
            AddUnique(analysis.VerificationCommands, "ctest --test-dir build");
            analysis.Hints.Add("CMakeLists.txt detected.");
        }

        if (vcxproj.Count > 0)
        {
            frameworks.Add("MSBuild");
            analysis.Hints.Add("Visual C++ project detected.");
        }

        if (!hasCMake && vcxproj.Count == 0)
        {
            frameworks.Add("C++");
        }
    }

    private static void DetectGo(
        WorkspaceAnalysis analysis,
        List<string> detectedTypes,
        List<string> frameworks)
    {
        if (!File.Exists(Path.Combine(analysis.WorkspaceRoot, "go.mod")))
        {
            return;
        }

        detectedTypes.Add("Go");
        frameworks.Add("Go modules");
        AddUnique(analysis.VerificationCommands, "go test ./...");
        analysis.Hints.Add("go.mod detected.");
    }

    private static void DetectRust(
        WorkspaceAnalysis analysis,
        List<string> detectedTypes,
        List<string> frameworks)
    {
        if (!File.Exists(Path.Combine(analysis.WorkspaceRoot, "Cargo.toml")))
        {
            return;
        }

        detectedTypes.Add("Rust");
        frameworks.Add("Cargo");
        AddUnique(analysis.VerificationCommands, "cargo build");
        AddUnique(analysis.VerificationCommands, "cargo test");
        analysis.Hints.Add("Cargo.toml detected.");
    }

    private async Task DetectNativeWorkerAsync(
        WorkspaceAnalysis analysis,
        List<string> detectedTypes,
        List<string> frameworks,
        CancellationToken ct)
    {
        RecordWorkerEvent("worker_started", analysis, "native-worker", "Starting native worker analysis.");
        NativeWorkerResult? result;
        try
        {
            result = await new NativeWorkerHost().AnalyzeAsync(analysis.WorkspaceRoot, ct);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException)
        {
            RecordWorkerEvent("worker_failed", analysis, "native-worker", ex.Message, ex);
            analysis.Hints.Add($"Native worker failed: {ex.Message}");
            return;
        }

        if (result == null)
        {
            RecordWorkerEvent("worker_skipped", analysis, "native-worker", "Worker returned no result. The script may be missing or the workspace path was unavailable.");
            return;
        }

        RecordWorkerEvent(
            "worker_completed",
            analysis,
            "native-worker",
            $"warnings={result.Warnings.Count:0}; scaffoldRecommendations={result.ScaffoldRecommendations.Count:0}; projectMap={result.ProjectMap.Count:0}");

        if (result.Warnings.Count > 0)
        {
            analysis.Hints.AddRange(result.Warnings.Select(warning => $"Native worker: {warning}").Take(4));
        }

        var cppSignalCount = result.Cpp.CmakeProjects.Count +
                             result.Cpp.CompileCommands.Count +
                             result.Cpp.Vcxprojects.Count +
                             result.Cpp.SourceFiles.Count +
                             result.Cpp.HeaderFiles.Count;
        var goSignalCount = result.Go.Modules.Count + result.Go.Packages.Count + result.Go.SourceFiles.Count;
        var rustSignalCount = result.Rust.Manifests.Count + result.Rust.Packages.Count + result.Rust.Targets.Count + result.Rust.SourceFiles.Count;
        var javaSignalCount = result.Java.BuildFiles.Count + result.Java.SourceFiles.Count + result.Java.Symbols.Count;
        var sqlSignalCount = result.Sql.Files.Count + result.Sql.Tables.Count;
        var phpSignalCount = result.Php.ComposerFiles.Count + result.Php.SourceFiles.Count + result.Php.Symbols.Count;
        var kotlinSignalCount = result.Kotlin.BuildFiles.Count + result.Kotlin.SourceFiles.Count + result.Kotlin.Symbols.Count;
        var swiftSignalCount = result.Swift.PackageFiles.Count + result.Swift.ProjectFiles.Count + result.Swift.SourceFiles.Count + result.Swift.Symbols.Count;
        var scriptSignalCount = result.Scripts.ShellFiles.Count + result.Scripts.PowerShellFiles.Count + result.Scripts.Commands.Count;
        var rSignalCount = result.R.ProjectFiles.Count + result.R.SourceFiles.Count + result.R.ReportFiles.Count + result.R.Symbols.Count;

        if (cppSignalCount == 0 && goSignalCount == 0 && rustSignalCount == 0 &&
            javaSignalCount == 0 && sqlSignalCount == 0 && phpSignalCount == 0 &&
            kotlinSignalCount == 0 && swiftSignalCount == 0 && scriptSignalCount == 0 && rSignalCount == 0)
        {
            return;
        }

        analysis.Hints.Add($"Native worker indexed C++ {cppSignalCount:0}, Go {goSignalCount:0}, Rust {rustSignalCount:0}, Java {javaSignalCount:0}, SQL {sqlSignalCount:0}, PHP {phpSignalCount:0}, Kotlin {kotlinSignalCount:0}, Swift {swiftSignalCount:0}, scripts {scriptSignalCount:0}, R {rSignalCount:0} signals.");
        AddWorkerGenerationGuidance(analysis, "Native worker", result.Capabilities, result.ScaffoldRecommendations);

        if (cppSignalCount > 0)
        {
            detectedTypes.Add("C++");
            AddUniqueRange(frameworks, result.Cpp.Tooling);
        }

        if (goSignalCount > 0)
        {
            detectedTypes.Add("Go");
            AddUniqueRange(frameworks, result.Go.Tooling.Count > 0 ? result.Go.Tooling : ["Go"]);
            AddUnique(analysis.VerificationCommands, "go test ./...");
        }

        if (rustSignalCount > 0)
        {
            detectedTypes.Add("Rust");
            AddUniqueRange(frameworks, result.Rust.Tooling.Count > 0 ? result.Rust.Tooling : ["Cargo"]);
            AddUnique(analysis.VerificationCommands, "cargo build");
            AddUnique(analysis.VerificationCommands, "cargo test");
        }

        foreach (var entry in result.ProjectMap.Take(12))
        {
            AddUnique(analysis.ProjectMap, FormatProjectMapEntry(entry.Role, [entry.Path], [entry.Path]));
        }

        foreach (var project in result.Cpp.CmakeProjects.Take(4))
        {
            AddUnique(analysis.KeyFiles, project.Path);
            if (!string.IsNullOrWhiteSpace(project.Name))
            {
                AddUnique(analysis.KeySymbols, $"cmake project {project.Name} ({project.Path})");
            }
        }

        foreach (var compileCommands in result.Cpp.CompileCommands.Take(4))
        {
            AddUnique(analysis.KeyFiles, compileCommands.Path);
            AddUnique(analysis.KeyDependencies, $"{compileCommands.Path} -> {compileCommands.Count:0} compile command(s) (native worker)");
        }

        foreach (var file in result.Cpp.Vcxprojects.Select(item => item.Path)
                     .Concat(result.Cpp.SourceFiles)
                     .Concat(result.Cpp.HeaderFiles)
                     .Take(8))
        {
            AddUnique(analysis.KeyFiles, file);
        }

        foreach (var module in result.Go.Modules.Take(4))
        {
            AddUnique(analysis.KeyFiles, module.Path);
            AddUnique(analysis.KeySymbols, string.IsNullOrWhiteSpace(module.GoVersion)
                ? $"go module {module.Module} ({module.Path})"
                : $"go module {module.Module} go {module.GoVersion} ({module.Path})");
        }

        foreach (var package in result.Go.Packages.Take(8))
        {
            AddUnique(analysis.KeySymbols, $"go package {package.ImportPath} ({package.Directory})");
        }

        foreach (var file in result.Go.SourceFiles.Take(6))
        {
            AddUnique(analysis.KeyFiles, file);
        }

        foreach (var manifest in result.Rust.Manifests.Take(4))
        {
            AddUnique(analysis.KeyFiles, manifest.Path);
            var name = string.IsNullOrWhiteSpace(manifest.PackageName)
                ? manifest.IsWorkspace ? "workspace" : "manifest"
                : manifest.PackageName;
            AddUnique(analysis.KeySymbols, $"cargo {name} ({manifest.Path})");
        }

        foreach (var package in result.Rust.Packages.Take(8))
        {
            AddUnique(analysis.KeySymbols, $"rust package {package.Name} {package.Version} ({package.ManifestPath})");
        }

        foreach (var target in result.Rust.Targets.Take(8))
        {
            AddUnique(analysis.KeyDependencies, $"{target.PackageName} -> {target.Name} ({target.Kind}) {target.SourcePath}");
        }

        foreach (var file in result.Rust.SourceFiles.Take(6))
        {
            AddUnique(analysis.KeyFiles, file);
        }

        AddNativeLanguageSignals(analysis, detectedTypes, frameworks, "Java", result.Java.Tooling, result.Java.Frameworks, result.Java.BuildFiles.Select(item => item.Path).Concat(result.Java.SourceFiles).Concat(result.Java.TestFiles), result.Java.Symbols, "java");
        AddNativeLanguageSignals(analysis, detectedTypes, frameworks, "SQL", result.Sql.Tooling, [], result.Sql.Files.Concat(result.Sql.Migrations), result.Sql.Tables.Select(table => new NativeLanguageSymbol { Path = table.Path, Kind = "table", Name = table.Name }), "sql");
        AddNativeLanguageSignals(analysis, detectedTypes, frameworks, "PHP", result.Php.Tooling, result.Php.Frameworks, result.Php.ComposerFiles.Select(item => item.Path).Concat(result.Php.SourceFiles).Concat(result.Php.TestFiles), result.Php.Symbols, "php");
        AddNativeLanguageSignals(analysis, detectedTypes, frameworks, "Kotlin", result.Kotlin.Tooling, result.Kotlin.Frameworks, result.Kotlin.BuildFiles.Select(item => item.Path).Concat(result.Kotlin.SourceFiles).Concat(result.Kotlin.TestFiles), result.Kotlin.Symbols, "kotlin");
        AddNativeLanguageSignals(analysis, detectedTypes, frameworks, "Swift", result.Swift.Tooling, result.Swift.Frameworks, result.Swift.PackageFiles.Select(item => item.Path).Concat(result.Swift.ProjectFiles.Select(item => item.Path)).Concat(result.Swift.SourceFiles).Concat(result.Swift.TestFiles), result.Swift.Symbols, "swift");
        AddNativeLanguageSignals(analysis, detectedTypes, frameworks, "Scripts", result.Scripts.Tooling, [], result.Scripts.ShellFiles.Concat(result.Scripts.PowerShellFiles), result.Scripts.Commands.Select(command => new NativeLanguageSymbol { Path = command.Path, Kind = "command", Name = command.Name }), "script");
        AddNativeLanguageSignals(analysis, detectedTypes, frameworks, "R", result.R.Tooling, [], result.R.ProjectFiles.Select(item => item.Path).Concat(result.R.SourceFiles).Concat(result.R.ReportFiles), result.R.Symbols, "r");

        if (javaSignalCount > 0)
        {
            AddUnique(analysis.VerificationCommands, result.Java.Tooling.Contains("Maven", StringComparer.OrdinalIgnoreCase) ? "mvn test" : "gradle test");
        }

        if (phpSignalCount > 0)
        {
            AddUnique(analysis.VerificationCommands, "composer test");
        }

        if (kotlinSignalCount > 0)
        {
            AddUnique(analysis.VerificationCommands, "./gradlew test");
        }

        if (swiftSignalCount > 0)
        {
            AddUnique(analysis.VerificationCommands, "swift test");
        }
    }

    private static void DetectDocker(
        WorkspaceAnalysis analysis,
        List<string> detectedTypes,
        List<string> frameworks)
    {
        if (!File.Exists(Path.Combine(analysis.WorkspaceRoot, "docker-compose.yml")) &&
            !File.Exists(Path.Combine(analysis.WorkspaceRoot, "docker-compose.yaml")) &&
            !File.Exists(Path.Combine(analysis.WorkspaceRoot, "compose.yml")) &&
            !File.Exists(Path.Combine(analysis.WorkspaceRoot, "compose.yaml")))
        {
            return;
        }

        detectedTypes.Add("Docker");
        frameworks.Add("Docker Compose");
        AddUnique(analysis.VerificationCommands, "docker compose config");
        analysis.Hints.Add("Docker Compose file detected.");
    }

    private static void AddNativeLanguageSignals(
        WorkspaceAnalysis analysis,
        List<string> detectedTypes,
        List<string> frameworks,
        string language,
        IEnumerable<string> tooling,
        IEnumerable<string> detectedFrameworks,
        IEnumerable<string> files,
        IEnumerable<NativeLanguageSymbol> symbols,
        string symbolPrefix)
    {
        var fileList = files.Where(file => !string.IsNullOrWhiteSpace(file)).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();
        var symbolList = symbols.Where(symbol => !string.IsNullOrWhiteSpace(symbol.Name)).Take(10).ToList();
        if (fileList.Count == 0 && symbolList.Count == 0)
        {
            return;
        }

        detectedTypes.Add(language);
        AddUniqueRange(frameworks, tooling);
        AddUniqueRange(frameworks, detectedFrameworks);
        foreach (var file in fileList)
        {
            AddUnique(analysis.KeyFiles, file);
        }

        foreach (var symbol in symbolList)
        {
            AddUnique(analysis.KeySymbols, $"{symbolPrefix} {symbol.Kind} {symbol.Name} ({symbol.Path})");
        }
    }

    private static void DetectDatabaseTooling(WorkspaceAnalysis analysis, List<string> frameworks)
    {
        var alembicIni = FindProjectFile(analysis.WorkspaceRoot, "alembic.ini");
        var hasAlembicDirectory = Directory.Exists(Path.Combine(analysis.WorkspaceRoot, "alembic")) ||
                                  Directory.EnumerateDirectories(analysis.WorkspaceRoot)
                                      .Where(directory => CanTraverseDirectory(analysis.WorkspaceRoot, directory))
                                      .Any(directory => Directory.Exists(Path.Combine(directory, "alembic")));
        if (alembicIni != null || hasAlembicDirectory)
        {
            frameworks.Add("Alembic");
            analysis.Hints.Add(alembicIni == null
                ? "Alembic migration directory detected."
                : $"Alembic config detected: {Path.GetRelativePath(analysis.WorkspaceRoot, alembicIni)}");
        }
    }

    private static void DetectMonorepoShape(WorkspaceAnalysis analysis)
    {
        var frontend = Directory.Exists(Path.Combine(analysis.WorkspaceRoot, "frontend"));
        var backend = Directory.Exists(Path.Combine(analysis.WorkspaceRoot, "backend"));
        var apps = Directory.Exists(Path.Combine(analysis.WorkspaceRoot, "apps"));
        var packages = Directory.Exists(Path.Combine(analysis.WorkspaceRoot, "packages"));

        if (frontend && backend)
        {
            analysis.Hints.Add("Frontend/backend workspace detected.");
        }

        if (apps || packages)
        {
            analysis.Hints.Add("Monorepo-style apps/packages layout detected.");
        }
    }

    private static void DetectProjectMap(WorkspaceAnalysis analysis, IReadOnlyCollection<string> detectedTypes)
    {
        var roles = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["UI layer"] = ["src", "app", "pages", "components", "views", "ui"],
            ["Frontend"] = ["frontend", "client", "web"],
            ["Backend"] = ["backend", "server"],
            ["API layer"] = ["api", "controllers", "routes", "endpoints"],
            ["Database layer"] = ["db", "database", "data", "migrations", "repositories"],
            ["Database models"] = ["models", "schemas", "backend/app/models", "backend/app/schemas", "server/app/models", "server/app/schemas"],
            ["Database migrations"] = ["migrations", "alembic", "backend/alembic", "server/alembic"],
            ["Domain logic"] = ["domain", "core", "services", "features", "logic"],
            ["Tests"] = ["test", "tests", "__tests__", "spec", "specs"],
            ["Assets"] = ["assets", "public", "static", "wwwroot"],
            ["Configuration"] = [".github", "config", "settings"]
        };

        if (detectedTypes.Contains("C++", StringComparer.OrdinalIgnoreCase))
        {
            roles["C++ source"] = ["src", "source", "Source"];
            roles["C++ headers"] = ["include", "Includes"];
        }

        if (detectedTypes.Contains("Go", StringComparer.OrdinalIgnoreCase))
        {
            roles["Go packages"] = ["cmd", "pkg", "internal"];
        }

        if (detectedTypes.Contains("Rust", StringComparer.OrdinalIgnoreCase))
        {
            roles["Rust crates"] = ["crates"];
        }

        if (detectedTypes.Contains("Python", StringComparer.OrdinalIgnoreCase))
        {
            roles["Python packages"] = ["src", "scripts"];
        }

        if (detectedTypes.Contains("Unity", StringComparer.OrdinalIgnoreCase))
        {
            roles["Unity assets"] = ["Assets", "ProjectSettings", "Packages"];
        }

        if (detectedTypes.Contains("Unreal", StringComparer.OrdinalIgnoreCase))
        {
            roles["Unreal project"] = ["Source", "Content", "Config", "Plugins"];
        }

        foreach (var (role, names) in roles)
        {
            var matches = names
                .Select(name => Path.Combine(analysis.WorkspaceRoot, name))
                .Where(Directory.Exists)
                .Select(path => Path.GetRelativePath(analysis.WorkspaceRoot, path))
                .Take(4)
                .ToList();

            if (matches.Count > 0)
            {
                analysis.ProjectMap.Add(FormatProjectMapEntry(role, matches, matches));
            }
        }

        if (analysis.ProjectMap.Count == 0)
        {
            analysis.Hints.Add("No obvious project map folders detected yet.");
        }
    }

    private static void DetectKeyFiles(WorkspaceAnalysis analysis)
    {
        var fileNames = new[]
        {
            "README.md",
            "package.json",
            "pyproject.toml",
            "requirements.txt",
            "pytest.ini",
            "go.mod",
            "Cargo.toml",
            "CMakeLists.txt",
            "docker-compose.yml",
            "Dockerfile",
            "Program.cs",
            "App.xaml",
            "ProjectSettings/ProjectVersion.txt"
        };

        foreach (var fileName in fileNames)
        {
            var path = Path.Combine(analysis.WorkspaceRoot, fileName);
            if (File.Exists(path))
            {
                analysis.KeyFiles.Add(fileName.Replace('/', Path.DirectorySeparatorChar));
            }
        }

        foreach (var solution in Directory.EnumerateFiles(analysis.WorkspaceRoot, "*.sln", SearchOption.TopDirectoryOnly)
                     .Concat(Directory.EnumerateFiles(analysis.WorkspaceRoot, "*.slnx", SearchOption.TopDirectoryOnly))
                     .Select(Path.GetFileName)
                     .Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            AddUnique(analysis.KeyFiles, solution!);
        }

        foreach (var project in Directory.EnumerateFiles(analysis.WorkspaceRoot, "*.uproject", SearchOption.TopDirectoryOnly)
                     .Concat(Directory.EnumerateFiles(analysis.WorkspaceRoot, "*.vcxproj", SearchOption.TopDirectoryOnly))
                     .Select(Path.GetFileName)
                     .Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            AddUnique(analysis.KeyFiles, project!);
        }

        foreach (var projectFile in FindProjectFiles(analysis.WorkspaceRoot, "package.json")
                     .Concat(FindProjectFiles(analysis.WorkspaceRoot, "requirements.txt"))
                     .Concat(FindProjectFiles(analysis.WorkspaceRoot, "pyproject.toml"))
                     .Take(8))
        {
            AddUnique(analysis.KeyFiles, Path.GetRelativePath(analysis.WorkspaceRoot, projectFile));
        }
    }

    private static string FormatProjectMapEntry(
        string role,
        IReadOnlyCollection<string> paths,
        IReadOnlyCollection<string> evidencePaths)
    {
        var displayPaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizeRelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();
        var evidence = evidencePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizeRelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();

        var entry = $"{role}: {string.Join(", ", displayPaths)}";
        return evidence.Count == 0
            ? entry
            : $"{entry} (evidence: {string.Join(", ", evidence)})";
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace('\\', '/').Trim();
    }

    private void DetectSymbols(WorkspaceAnalysis analysis)
    {
        var symbolIndex = _symbolIndexService.Build(analysis.WorkspaceRoot);
        analysis.SymbolCount = symbolIndex.SymbolCount;

        foreach (var symbol in symbolIndex.Symbols
                     .Where(symbol => symbol.Kind is "class" or "record" or "interface" or "struct")
                     .Concat(symbolIndex.Symbols.Where(symbol => symbol.Kind is "method" or "function"))
                     .Take(8))
        {
            analysis.KeySymbols.Add(symbol.DisplayName);
        }

        if (symbolIndex.SymbolCount > 0)
        {
            analysis.Hints.Add($"C# symbol index: {symbolIndex.SymbolCount:0} symbols in {symbolIndex.FilesIndexed:0} files.");
        }
    }

    private void DetectDependencyGraph(WorkspaceAnalysis analysis)
    {
        var graph = _dependencyGraphService.Build(analysis.WorkspaceRoot);
        analysis.DependencyEdgeCount = graph.EdgeCount;

        foreach (var edge in graph.Edges
                     .Where(edge => !edge.IsExternal)
                     .Concat(graph.Edges.Where(edge => edge.IsExternal))
                     .Take(10))
        {
            AddUnique(analysis.KeyDependencies, edge.DisplayText);
        }

        if (graph.EdgeCount > 0)
        {
            analysis.Hints.Add($"Dependency graph: {graph.EdgeCount:0} edge(s) across {graph.FilesIndexed:0} files.");
        }
    }

    private void DetectCSharpRoslyn(WorkspaceAnalysis analysis)
    {
        var result = _csharpRoslynAnalysisService.Analyze(analysis.WorkspaceRoot);
        if (result.FilesIndexed == 0 &&
            result.Projects.Count == 0 &&
            result.Symbols.Count == 0 &&
            result.Usings.Count == 0)
        {
            return;
        }

        analysis.SymbolCount = Math.Max(analysis.SymbolCount, result.Symbols.Count);
        analysis.Hints.Add($"Roslyn C# analysis: {result.Symbols.Count:0} symbols, {result.Usings.Count:0} usings, {result.ProjectReferences.Count:0} project references.");

        foreach (var project in result.Projects.Take(8))
        {
            AddUnique(analysis.ProjectMap, FormatProjectMapEntry("C# projects", [project], [project]));
            AddUnique(analysis.KeyFiles, project);
        }

        foreach (var item in result.Namespaces.Take(6))
        {
            AddUnique(analysis.KeySymbols, $"namespace {item.Name} ({item.Path}:{item.Line:0})");
        }

        foreach (var symbol in result.Symbols
                     .Where(symbol => symbol.Kind is "class" or "record" or "interface" or "struct" or "enum")
                     .Concat(result.Symbols.Where(symbol => symbol.Kind is "method" or "constructor"))
                     .Take(12))
        {
            AddUnique(analysis.KeySymbols, symbol.DisplayName);
        }

        foreach (var usingDirective in result.Usings.Take(12))
        {
            AddUnique(analysis.KeyDependencies, $"{usingDirective.Path}:{usingDirective.Line:0} -> {usingDirective.Namespace} (roslyn using)");
        }

        foreach (var reference in result.ProjectReferences.Take(8))
        {
            AddUnique(analysis.KeyDependencies, $"{reference.Path} -> {reference.Target} (roslyn project-reference)");
        }

        foreach (var diagnostic in result.Diagnostics.Take(5))
        {
            analysis.Hints.Add($"Roslyn diagnostic {diagnostic.Id} at {diagnostic.Path}:{diagnostic.Line:0}: {diagnostic.Message}");
        }
    }

    private async Task DetectTypeScriptWorkerAsync(
        WorkspaceAnalysis analysis,
        List<string> frameworks,
        CancellationToken ct)
    {
        RecordWorkerEvent("worker_started", analysis, "typescript-worker", "Starting JavaScript/TypeScript worker analysis.");
        TypeScriptWorkerResult? result;
        try
        {
            result = await new TypeScriptWorkerHost().AnalyzeAsync(analysis.WorkspaceRoot, ct);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException)
        {
            RecordWorkerEvent("worker_failed", analysis, "typescript-worker", ex.Message, ex);
            analysis.Hints.Add($"JavaScript/TypeScript worker failed: {ex.Message}");
            return;
        }

        if (result == null)
        {
            RecordWorkerEvent("worker_skipped", analysis, "typescript-worker", "Worker returned no result. The script may be missing, node may be unavailable, or the workspace path was unavailable.");
            return;
        }

        RecordWorkerEvent(
            "worker_completed",
            analysis,
            "typescript-worker",
            $"packages={result.Packages.Count:0}; symbols={result.Symbols.Count:0}; imports={result.Imports.Count:0}; npmScripts={result.NpmScripts.Count:0}; warnings={result.Warnings.Count:0}; scaffoldRecommendations={result.ScaffoldRecommendations.Count:0}");

        if (result.Warnings.Count > 0)
        {
            analysis.Hints.AddRange(result.Warnings.Select(warning => $"JavaScript/TypeScript worker: {warning}").Take(3));
        }

        if (result.Packages.Count == 0 &&
            result.Tsconfigs.Count == 0 &&
            result.Symbols.Count == 0 &&
            result.Imports.Count == 0 &&
            result.Playwright.Configs.Count == 0 &&
            !result.Playwright.HasDependency &&
            result.ScaffoldRecommendations.Count == 0)
        {
            return;
        }

        analysis.Hints.Add($"JavaScript/TypeScript worker indexed {result.Symbols.Count:0} symbols, {result.Imports.Count:0} imports, {result.ReactComponents.Count:0} React components, {result.ReactHooks.Count:0} hooks, {result.ApiEndpoints.Count:0} API handlers.");
        AddWorkerGenerationGuidance(analysis, "JavaScript/TypeScript worker", result.Capabilities, result.ScaffoldRecommendations);

        foreach (var manager in result.PackageManagers)
        {
            analysis.Hints.Add($"Package manager lockfile detected: {manager}");
        }

        foreach (var tsconfig in result.Tsconfigs.Take(4))
        {
            AddUnique(analysis.KeyFiles, tsconfig.Path);
            frameworks.Add("TypeScript");
        }

        foreach (var package in result.Packages.Take(8))
        {
            AddUnique(analysis.KeyFiles, package.Path);
            AddNodeWorkerFrameworks(package, frameworks);
        }

        foreach (var script in result.NpmScripts.Where(script => script.Name is "build" or "test" or "lint").Take(8))
        {
            var packageDirectory = Path.GetDirectoryName(script.PackagePath)?.Replace('\\', '/') ?? string.Empty;
            AddNodeCommand(analysis, packageDirectory, $"npm run {script.Name}");
        }

        if (result.Playwright.HasDependency || result.Playwright.Configs.Count > 0 || result.Playwright.Scripts.Count > 0)
        {
            frameworks.Add("Playwright");
            analysis.Hints.Add($"Playwright detected with {result.Playwright.Configs.Count:0} config file(s) and {result.Playwright.Scripts.Count:0} script(s).");
        }

        foreach (var config in result.Playwright.Configs.Take(4))
        {
            AddUnique(analysis.KeyFiles, config);
            AddUnique(analysis.ProjectMap, FormatProjectMapEntry("Playwright config", [config], [config]));
        }

        foreach (var reportPath in result.Playwright.ReportPaths.Take(3))
        {
            AddUnique(analysis.ProjectMap, FormatProjectMapEntry("Playwright reports", [reportPath], [reportPath]));
        }

        foreach (var script in result.Playwright.Scripts.Take(4))
        {
            var packageDirectory = Path.GetDirectoryName(script.PackagePath)?.Replace('\\', '/') ?? string.Empty;
            AddNodeCommand(analysis, packageDirectory, $"npm run {script.Name}");
        }

        if (result.Playwright.Scripts.Count == 0 && (result.Playwright.HasDependency || result.Playwright.Configs.Count > 0))
        {
            var packageDirectory = result.Playwright.Configs
                .Select(config => Path.GetDirectoryName(config)?.Replace('\\', '/') ?? string.Empty)
                .FirstOrDefault(directory => !string.IsNullOrWhiteSpace(directory)) ?? string.Empty;
            AddNodeCommand(analysis, packageDirectory, "npx playwright test");
        }

        foreach (var entry in result.ProjectMap.Take(8))
        {
            AddUnique(analysis.ProjectMap, FormatProjectMapEntry(entry.Role, [entry.Path], [entry.Path]));
        }

        foreach (var component in result.ReactComponents.Take(8))
        {
            AddUnique(analysis.KeySymbols, $"component {component.Name} ({component.Path}:{component.Line:0})");
        }

        foreach (var hook in result.ReactHooks.Take(8))
        {
            AddUnique(analysis.KeySymbols, $"hook {hook.Name} ({hook.Path}:{hook.Line:0})");
        }

        foreach (var endpoint in result.ApiEndpoints.Take(8))
        {
            AddUnique(analysis.KeySymbols, $"api {endpoint.Method} {endpoint.Route} ({endpoint.Path}:{endpoint.Line:0})");
            AddUnique(analysis.ProjectMap, FormatProjectMapEntry("API route handlers", [endpoint.Path], [endpoint.Path]));
        }

        foreach (var target in result.TestTargets.Take(8))
        {
            AddUnique(analysis.KeySymbols, $"{target.Kind} {target.Name} ({target.Path}:{target.Line:0})");
            AddUnique(analysis.ProjectMap, FormatProjectMapEntry("JavaScript tests", [target.Path], [target.Path]));
        }

        foreach (var exported in result.Exports.Take(8))
        {
            AddUnique(analysis.KeySymbols, $"{exported.Kind} {exported.Name} ({exported.Path}:{exported.Line:0})");
        }

        foreach (var import in result.Imports
                     .Where(import => !string.IsNullOrWhiteSpace(import.ResolvedPath))
                     .Take(8))
        {
            AddUnique(
                analysis.KeyDependencies,
                $"{import.Path}:{import.Line:0} -> {import.ResolvedPath} (worker import)");
        }

        foreach (var route in result.Routes.Take(8))
        {
            AddUnique(analysis.ProjectMap, FormatProjectMapEntry("Route files", [route.Path], [route.Path]));
        }
    }

    private static void AddNodeWorkerFrameworks(TypeScriptPackageInfo package, List<string> frameworks)
    {
        var dependencies = package.Dependencies.Concat(package.DevDependencies).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (dependencies.Contains("next"))
        {
            frameworks.Add("Next.js");
        }

        if (dependencies.Contains("vite"))
        {
            frameworks.Add("Vite");
        }

        if (dependencies.Contains("react"))
        {
            frameworks.Add("React");
        }

        if (dependencies.Contains("typescript"))
        {
            frameworks.Add("TypeScript");
        }
    }

    private async Task DetectPythonWorkerAsync(
        WorkspaceAnalysis analysis,
        List<string> frameworks,
        CancellationToken ct)
    {
        RecordWorkerEvent("worker_started", analysis, "python-worker", "Starting Python worker analysis.");
        PythonWorkerResult? result;
        try
        {
            result = await new PythonWorkerHost().AnalyzeAsync(analysis.WorkspaceRoot, ct);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException)
        {
            RecordWorkerEvent("worker_failed", analysis, "python-worker", ex.Message, ex);
            analysis.Hints.Add($"Python worker failed: {ex.Message}");
            return;
        }

        if (result == null)
        {
            RecordWorkerEvent("worker_skipped", analysis, "python-worker", "Worker returned no result. The script may be missing, python may be unavailable, or the workspace path was unavailable.");
            return;
        }

        RecordWorkerEvent(
            "worker_completed",
            analysis,
            "python-worker",
            $"pyprojects={result.Pyprojects.Count:0}; requirements={result.Requirements.Count:0}; symbols={result.Symbols.Count:0}; imports={result.Imports.Count:0}; warnings={result.Warnings.Count:0}; failureHints={result.FailureHints.Count:0}; scaffoldRecommendations={result.ScaffoldRecommendations.Count:0}");

        if (result.Warnings.Count > 0)
        {
            analysis.Hints.AddRange(result.Warnings.Select(warning => $"Python worker: {warning}").Take(3));
        }

        if (result.FailureHints.Count > 0)
        {
            analysis.Hints.AddRange(result.FailureHints.Select(hint => $"Python worker hint: {hint}").Take(3));
        }

        if (result.Pyprojects.Count == 0 &&
            result.Requirements.Count == 0 &&
            result.Symbols.Count == 0 &&
            result.Imports.Count == 0)
        {
            return;
        }

        analysis.Hints.Add($"Python worker indexed {result.Symbols.Count:0} symbols, {result.Imports.Count:0} imports, {result.CallSites.Count:0} calls, {result.FastApiRoutes.Count:0} FastAPI routes, {result.WebRoutes.Count:0} web routes.");
        AddWorkerGenerationGuidance(analysis, "Python worker", result.Capabilities, result.ScaffoldRecommendations);

        foreach (var pyproject in result.Pyprojects.Take(6))
        {
            AddUnique(analysis.KeyFiles, pyproject.Path);
            AddPythonWorkerFrameworks(pyproject.Dependencies, frameworks);
        }

        foreach (var requirements in result.Requirements.Take(6))
        {
            AddUnique(analysis.KeyFiles, requirements.Path);
            AddPythonWorkerFrameworks(requirements.Dependencies, frameworks);
        }

        if (result.PytestTargets.Count > 0)
        {
            AddPythonCommand(analysis, ".", "python -m pytest");
        }

        foreach (var entry in result.ProjectMap.Take(8))
        {
            AddUnique(analysis.ProjectMap, FormatProjectMapEntry(entry.Role, [entry.Path], [entry.Path]));
        }

        foreach (var import in result.Imports.Where(import => !string.IsNullOrWhiteSpace(import.ResolvedPath)).Take(12))
        {
            AddUnique(analysis.KeyDependencies, $"{import.Path}:{import.Line:0} -> {import.ResolvedPath} (python import {import.Module})");
        }

        foreach (var route in result.FastApiRoutes.Take(10))
        {
            AddUnique(analysis.KeySymbols, $"route {route.Method} {route.Route} -> {route.Name} ({route.Path}:{route.Line:0})");
            frameworks.Add("FastAPI");
        }

        foreach (var route in result.WebRoutes.Take(10))
        {
            AddUnique(analysis.KeySymbols, $"route {route.Framework} {route.Method} {route.Route} -> {route.Name} ({route.Path}:{route.Line:0})");
            if (!string.IsNullOrWhiteSpace(route.Framework))
            {
                frameworks.Add(route.Framework);
                AddUnique(analysis.ProjectMap, FormatProjectMapEntry($"{route.Framework} routes", [route.Path], [route.Path]));
            }
        }

        foreach (var model in result.SqlAlchemyModels.Take(10))
        {
            AddUnique(analysis.KeySymbols, $"model {model.Name} ({model.Path}:{model.Line:0})");
            frameworks.Add("SQLAlchemy");
        }

        foreach (var task in result.CeleryTasks.Take(8))
        {
            AddUnique(analysis.KeySymbols, $"celery task {task.Name} ({task.Path}:{task.Line:0})");
            frameworks.Add("Celery");
        }

        foreach (var command in result.CliCommands.Take(8))
        {
            AddUnique(analysis.KeySymbols, $"cli {command.Command} -> {command.Name} ({command.Path}:{command.Line:0})");
            frameworks.Add(command.Framework);
        }

        foreach (var symbol in result.Symbols.Take(10))
        {
            AddUnique(analysis.KeySymbols, $"{symbol.Kind} {symbol.Name} ({symbol.Path}:{symbol.Line:0})");
        }

        foreach (var callSite in result.CallSites.Take(8))
        {
            var scope = string.IsNullOrWhiteSpace(callSite.EnclosingSymbol) ? string.Empty : $" in {callSite.EnclosingSymbol}";
            AddUnique(analysis.KeySymbols, $"call {callSite.Name}{scope} ({callSite.Path}:{callSite.Line:0})");
        }
    }

    private static void AddPythonWorkerFrameworks(IEnumerable<string> dependencies, List<string> frameworks)
    {
        var joined = string.Join('\n', dependencies);
        if (joined.Contains("fastapi", StringComparison.OrdinalIgnoreCase))
        {
            frameworks.Add("FastAPI");
        }

        if (joined.Contains("django", StringComparison.OrdinalIgnoreCase))
        {
            frameworks.Add("Django");
        }

        if (joined.Contains("flask", StringComparison.OrdinalIgnoreCase))
        {
            frameworks.Add("Flask");
        }

        if (joined.Contains("sqlalchemy", StringComparison.OrdinalIgnoreCase))
        {
            frameworks.Add("SQLAlchemy");
        }

        if (joined.Contains("pytest", StringComparison.OrdinalIgnoreCase))
        {
            frameworks.Add("pytest");
        }

        if (joined.Contains("celery", StringComparison.OrdinalIgnoreCase))
        {
            frameworks.Add("Celery");
        }

        if (joined.Contains("click", StringComparison.OrdinalIgnoreCase))
        {
            frameworks.Add("Click");
        }
    }

    private static void AddWorkerGenerationGuidance(
        WorkspaceAnalysis analysis,
        string workerName,
        IEnumerable<WorkerCapability> capabilities,
        IEnumerable<WorkerScaffoldRecommendation> recommendations)
    {
        foreach (var capability in capabilities.Take(6))
        {
            if (!string.IsNullOrWhiteSpace(capability.Name))
            {
                analysis.Hints.Add($"{workerName} capability: {capability.Name} - {capability.Description}");
            }
        }

        foreach (var recommendation in recommendations.Take(4))
        {
            if (string.IsNullOrWhiteSpace(recommendation.Name))
            {
                continue;
            }

            if (!analysis.ScaffoldRecommendations.Any(existing =>
                    string.Equals(existing.Name, recommendation.Name, StringComparison.OrdinalIgnoreCase)))
            {
                analysis.ScaffoldRecommendations.Add(recommendation);
            }

            analysis.Hints.Add($"{workerName} scaffold: {recommendation.Name} - {recommendation.Description}");
            foreach (var command in recommendation.VerificationCommands.Take(3))
            {
                AddVerificationCommandIfAllowed(analysis, command);
            }

            foreach (var file in recommendation.Files.Take(4))
            {
                AddUnique(analysis.ProjectMap, FormatProjectMapEntry($"Suggested scaffold: {recommendation.Name}", [file], []));
            }
        }
    }

    private static IEnumerable<string> ReadTargetFrameworks(string projectPath)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(projectPath);
        }
        catch
        {
            yield break;
        }

        foreach (var element in document.Descendants().Where(e =>
                     e.Name.LocalName is "TargetFramework" or "TargetFrameworks"))
        {
            foreach (var value in element.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return value;
            }
        }
    }

    private static void AddDependencyNames(JsonElement root, string propertyName, HashSet<string> names)
    {
        if (!root.TryGetProperty(propertyName, out var dependencies) ||
            dependencies.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var dependency in dependencies.EnumerateObject())
        {
            names.Add(dependency.Name);
        }
    }

    private static void TryAppendFileText(string path, StringBuilder builder)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            builder.AppendLine(File.ReadAllText(path));
        }
        catch
        {
            // Best effort framework detection only.
        }
    }

    private static string ReadUnityVersion(string path)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        try
        {
            var line = File.ReadLines(path).FirstOrDefault(value =>
                value.StartsWith("m_EditorVersion:", StringComparison.OrdinalIgnoreCase));
            return line?.Split(':', 2).LastOrDefault()?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void AddUnityProjectMapEntry(WorkspaceAnalysis analysis, string role, string path)
    {
        if (Directory.Exists(Path.Combine(analysis.WorkspaceRoot, path)))
        {
            AddUnique(analysis.ProjectMap, FormatProjectMapEntry(role, [path], [path]));
        }
    }

    private static void AddUnityRole(WorkspaceAnalysis analysis, string role, IReadOnlyCollection<string> paths)
    {
        if (paths.Count > 0)
        {
            AddUnique(analysis.ProjectMap, FormatProjectMapEntry(role, paths, paths));
        }
    }

    private static string ReadUnityAsmdefName(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("name", out var name) &&
                   name.ValueKind == JsonValueKind.String
                ? name.GetString() ?? string.Empty
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static IReadOnlyList<string> ReadUnityPackageNames(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("dependencies", out var dependencies) ||
                dependencies.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            return dependencies.EnumerateObject()
                .Select(item => item.Name)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ReadUnityBuildSettingsScenes(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return File.ReadLines(path)
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("path:", StringComparison.OrdinalIgnoreCase))
                .Select(line => line.Split(':', 2).LastOrDefault()?.Trim() ?? string.Empty)
                .Where(line => line.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                               line.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static void AddUnityFramework(List<string> frameworks, string packageName)
    {
        if (packageName.Contains("inputsystem", StringComparison.OrdinalIgnoreCase))
        {
            AddUnique(frameworks, "Unity Input System");
        }

        if (packageName.Contains("render-pipelines.universal", StringComparison.OrdinalIgnoreCase))
        {
            AddUnique(frameworks, "Unity URP");
        }

        if (packageName.Contains("render-pipelines.high-definition", StringComparison.OrdinalIgnoreCase))
        {
            AddUnique(frameworks, "Unity HDRP");
        }

        if (packageName.Contains("test-framework", StringComparison.OrdinalIgnoreCase))
        {
            AddUnique(frameworks, "Unity Test Framework");
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root, string pattern)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current, pattern)
                    .Where(file => WorkspacePathResolver.IsResolvedInsideWorkspace(root, file));
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (var directory in directories)
            {
                if (CanTraverseDirectory(root, directory))
                {
                    pending.Push(directory);
                }
            }
        }
    }

    private static void AddCommandIfExists(WorkspaceAnalysis analysis, string fileName, string command)
    {
        if (File.Exists(Path.Combine(analysis.WorkspaceRoot, fileName)))
        {
            AddVerificationCommandIfAllowed(analysis, command);
        }
    }

    private static void AddNodeCommand(WorkspaceAnalysis analysis, string projectRelativePath, string command)
    {
        AddVerificationCommandIfAllowed(analysis, PrefixCommand(projectRelativePath, command));
    }

    private static void AddPythonCommand(WorkspaceAnalysis analysis, string projectRelativePath, string command)
    {
        AddVerificationCommandIfAllowed(analysis, PrefixCommand(projectRelativePath, command));
    }

    private static string PrefixCommand(string projectRelativePath, string command)
    {
        if (string.IsNullOrWhiteSpace(projectRelativePath) || projectRelativePath == ".")
        {
            return command;
        }

        var normalizedPath = NormalizeRelativePath(projectRelativePath);
        var escapedPath = normalizedPath.Replace("\"", "\"\"");
        return $"cmd /c cd /d \"{escapedPath}\" && {command}";
    }

    private static void AddVerificationCommandIfAllowed(WorkspaceAnalysis analysis, string command)
    {
        if (VerificationCommandPolicy.IsAllowed(command))
        {
            AddUnique(analysis.VerificationCommands, command);
        }
    }

    private static string? FindProjectFile(string root, string fileName) =>
        FindProjectFiles(root, fileName).FirstOrDefault();

    private static IEnumerable<string> FindProjectFiles(string root, string fileName)
    {
        var topLevel = Path.Combine(root, fileName);
        if (File.Exists(topLevel))
        {
            yield return topLevel;
        }

        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(root)
                .Where(directory => CanTraverseDirectory(root, directory));
        }
        catch
        {
            yield break;
        }

        foreach (var directory in directories)
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate) &&
                WorkspacePathResolver.IsResolvedInsideWorkspace(root, candidate))
            {
                yield return candidate;
            }
        }
    }

    private static bool CanTraverseDirectory(string root, string directory)
    {
        if (ExcludedDirectories.Contains(Path.GetFileName(directory)))
        {
            return false;
        }

        try
        {
            return !new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint) &&
                   WorkspacePathResolver.IsResolvedInsideWorkspace(root, directory);
        }
        catch
        {
            return false;
        }
    }

    private static void AddUnique(List<string> values, string value)
    {
        if (!values.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(value);
        }
    }

    private static void AddUniqueRange(List<string> values, IEnumerable<string> items)
    {
        foreach (var item in items.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            AddUnique(values, item);
        }
    }

    private static string RunGit(string root, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"-C \"{root}\" {arguments}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            if (process == null)
            {
                return string.Empty;
            }

            if (!process.WaitForExit(1500))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best effort cleanup for a dashboard-only process.
                }

                return string.Empty;
            }

            return process.ExitCode == 0 ? process.StandardOutput.ReadToEnd().Trim() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static async Task CheckSystemDependenciesAsync(WorkspaceAnalysis analysis, CancellationToken ct)
    {
        // 1. Check Git
        if (!IsExecutableAvailable("git", "--version"))
        {
            analysis.Hints.Add("Diagnostic Warning: 'git' is not installed or not in PATH. Version control features and analysis will be limited.");
        }

        // 2. Check Node
        if (!IsExecutableAvailable("node", "-v"))
        {
            analysis.Hints.Add("Diagnostic Warning: 'node' (Node.js) is not in PATH. TypeScript/JavaScript code structure worker is disabled.");
        }

        // 3. Check Python
        if (!IsExecutableAvailable("python", "--version"))
        {
            analysis.Hints.Add("Diagnostic Warning: 'python' is not in PATH. Python AST analysis worker is disabled.");
        }

        // 4. Check FFmpeg (AgentQ has video attachment support)
        if (!IsExecutableAvailable("ffmpeg", "-version"))
        {
            analysis.Hints.Add("Diagnostic Warning: 'ffmpeg' is not in PATH. Video frame extraction for attachments is disabled.");
        }
    }

    private static bool IsExecutableAvailable(string fileName, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            if (process == null) return false;
            return process.WaitForExit(1500);
        }
        catch
        {
            return false;
        }
    }
}
