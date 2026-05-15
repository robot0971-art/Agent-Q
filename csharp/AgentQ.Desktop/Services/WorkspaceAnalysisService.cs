using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;

namespace AgentQ.Desktop.Services;

public sealed class WorkspaceAnalysisService
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", "bin", "obj", "node_modules", "artifacts", ".codex-build"
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
        DetectUnity(analysis, detectedTypes, frameworks);
        DetectPython(analysis, detectedTypes, frameworks);

        analysis.ProjectType = detectedTypes.Count > 0
            ? string.Join(", ", detectedTypes.Distinct(StringComparer.OrdinalIgnoreCase))
            : "Unknown";
        analysis.Framework = frameworks.Count > 0
            ? string.Join(", ", frameworks.Distinct(StringComparer.OrdinalIgnoreCase).Take(4))
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
        var packageJson = Path.Combine(analysis.WorkspaceRoot, "package.json");
        if (!File.Exists(packageJson))
        {
            return;
        }

        detectedTypes.Add("Node");
        analysis.Hints.Add("package.json detected.");

        try
        {
            await using var stream = File.OpenRead(packageJson);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = document.RootElement;
            DetectNodeFrameworks(root, frameworks);
            DetectNodeScripts(root, analysis);
        }
        catch
        {
            frameworks.Add("Node");
            AddUnique(analysis.VerificationCommands, "npm test");
            AddUnique(analysis.VerificationCommands, "npm run build");
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

    private static void DetectNodeScripts(JsonElement root, WorkspaceAnalysis analysis)
    {
        if (!root.TryGetProperty("scripts", out var scripts) || scripts.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (scripts.TryGetProperty("test", out _))
        {
            AddUnique(analysis.VerificationCommands, "npm test");
        }

        if (scripts.TryGetProperty("build", out _))
        {
            AddUnique(analysis.VerificationCommands, "npm run build");
        }

        if (scripts.TryGetProperty("lint", out _))
        {
            AddUnique(analysis.VerificationCommands, "npm run lint");
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
    }

    private static void DetectPython(
        WorkspaceAnalysis analysis,
        List<string> detectedTypes,
        List<string> frameworks)
    {
        if (!File.Exists(Path.Combine(analysis.WorkspaceRoot, "pyproject.toml")) &&
            !File.Exists(Path.Combine(analysis.WorkspaceRoot, "requirements.txt")))
        {
            return;
        }

        detectedTypes.Add("Python");
        frameworks.Add("Python");
        analysis.Hints.Add("Python project files detected.");
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
