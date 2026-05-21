using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace AgentQ.Desktop.Services;

public sealed class WorkspaceAnalysisService
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
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
            return analysis;
        }

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
        DetectUnity(analysis, detectedTypes, frameworks);
        DetectUnreal(analysis, detectedTypes, frameworks);
        DetectDocker(analysis, detectedTypes, frameworks);
        DetectDatabaseTooling(analysis, frameworks);
        DetectMonorepoShape(analysis);
        DetectProjectMap(analysis, detectedTypes);
        DetectSymbols(analysis);
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

        return analysis;
    }

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
                    if (ExcludedDirectories.Contains(Path.GetFileName(directory)))
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
        AddUnique(analysis.VerificationCommands, "Unity Test Runner");
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
        AddUnique(analysis.VerificationCommands, "docker compose up --build");
        analysis.Hints.Add("Docker Compose file detected.");
    }

    private static void DetectDatabaseTooling(WorkspaceAnalysis analysis, List<string> frameworks)
    {
        var alembicIni = FindProjectFile(analysis.WorkspaceRoot, "alembic.ini");
        var hasAlembicDirectory = Directory.Exists(Path.Combine(analysis.WorkspaceRoot, "alembic")) ||
                                  Directory.EnumerateDirectories(analysis.WorkspaceRoot)
                                      .Where(directory => !ExcludedDirectories.Contains(Path.GetFileName(directory)))
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
            ["Configuration"] = [".github", ".agentq", "config", "settings"]
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
                analysis.ProjectMap.Add($"{role}: {string.Join(", ", matches)}");
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
            "ProjectSettings/ProjectVersion.txt",
            ".agentq/config.json",
            ".agentq/memory.shared.json"
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

    private static void DetectSymbols(WorkspaceAnalysis analysis)
    {
        var symbolIndex = new WorkspaceSymbolIndexService().Build(analysis.WorkspaceRoot);
        analysis.SymbolCount = symbolIndex.SymbolCount;

        foreach (var symbol in symbolIndex.Symbols
                     .Where(symbol => symbol.Kind is "class" or "record" or "interface" or "struct")
                     .Concat(symbolIndex.Symbols.Where(symbol => symbol.Kind == "method"))
                     .Take(8))
        {
            analysis.KeySymbols.Add(symbol.DisplayName);
        }

        if (symbolIndex.SymbolCount > 0)
        {
            analysis.Hints.Add($"C# symbol index: {symbolIndex.SymbolCount:0} symbols in {symbolIndex.FilesIndexed:0} files.");
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
                files = Directory.EnumerateFiles(current, pattern);
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
                if (!ExcludedDirectories.Contains(Path.GetFileName(directory)))
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
            AddUnique(analysis.VerificationCommands, command);
        }
    }

    private static void AddNodeCommand(WorkspaceAnalysis analysis, string projectRelativePath, string command)
    {
        AddUnique(analysis.VerificationCommands, PrefixCommand(projectRelativePath, command));
    }

    private static void AddPythonCommand(WorkspaceAnalysis analysis, string projectRelativePath, string command)
    {
        AddUnique(analysis.VerificationCommands, PrefixCommand(projectRelativePath, command));
    }

    private static string PrefixCommand(string projectRelativePath, string command)
    {
        return string.IsNullOrWhiteSpace(projectRelativePath) || projectRelativePath == "."
            ? command
            : $"cmd /c cd {projectRelativePath} && {command}";
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
                .Where(directory => !ExcludedDirectories.Contains(Path.GetFileName(directory)));
        }
        catch
        {
            yield break;
        }

        foreach (var directory in directories)
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static void AddUnique(List<string> values, string value)
    {
        if (!values.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(value);
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
}
