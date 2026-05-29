namespace AgentQ.Desktop.Services;

public sealed class TypeScriptWorkerResult
{
    public string Worker { get; set; } = string.Empty;

    public int Version { get; set; }

    public string Root { get; set; } = string.Empty;

    public List<string> PackageManagers { get; set; } = [];

    public List<TypeScriptPackageInfo> Packages { get; set; } = [];

    public List<TypeScriptConfigInfo> Tsconfigs { get; set; } = [];

    public List<TypeScriptNpmScript> NpmScripts { get; set; } = [];

    public List<TypeScriptImportInfo> Imports { get; set; } = [];

    public List<TypeScriptExportInfo> Exports { get; set; } = [];

    public List<TypeScriptReactComponent> ReactComponents { get; set; } = [];

    public List<TypeScriptReactHook> ReactHooks { get; set; } = [];

    public List<TypeScriptApiEndpoint> ApiEndpoints { get; set; } = [];

    public List<TypeScriptTestTarget> TestTargets { get; set; } = [];

    public TypeScriptPlaywrightInfo Playwright { get; set; } = new();

    public List<TypeScriptRouteInfo> Routes { get; set; } = [];

    public List<TypeScriptWorkerSymbol> Symbols { get; set; } = [];

    public List<TypeScriptProjectMapEntry> ProjectMap { get; set; } = [];

    public List<WorkerCapability> Capabilities { get; set; } = [];

    public List<WorkerScaffoldRecommendation> ScaffoldRecommendations { get; set; } = [];

    public List<string> Warnings { get; set; } = [];
}

public sealed class TypeScriptPackageInfo
{
    public string Path { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public List<string> Dependencies { get; set; } = [];

    public List<string> DevDependencies { get; set; } = [];
}

public sealed class TypeScriptConfigInfo
{
    public string Path { get; set; } = string.Empty;

    public string Extends { get; set; } = string.Empty;

    public string Jsx { get; set; } = string.Empty;

    public string Module { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public Dictionary<string, List<string>> Paths { get; set; } = [];
}

public sealed class TypeScriptNpmScript
{
    public string PackagePath { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;
}

public sealed class TypeScriptImportInfo
{
    public string Path { get; set; } = string.Empty;

    public int Line { get; set; }

    public string Source { get; set; } = string.Empty;

    public string ResolvedPath { get; set; } = string.Empty;
}

public sealed class TypeScriptExportInfo
{
    public string Path { get; set; } = string.Empty;

    public int Line { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;
}

public sealed class TypeScriptReactComponent
{
    public string Path { get; set; } = string.Empty;

    public int Line { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class TypeScriptReactHook
{
    public string Path { get; set; } = string.Empty;

    public int Line { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class TypeScriptApiEndpoint
{
    public string Path { get; set; } = string.Empty;

    public int Line { get; set; }

    public string Method { get; set; } = string.Empty;

    public string Route { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;
}

public sealed class TypeScriptTestTarget
{
    public string Path { get; set; } = string.Empty;

    public int Line { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

public sealed class TypeScriptPlaywrightInfo
{
    public bool HasDependency { get; set; }

    public List<string> Configs { get; set; } = [];

    public List<TypeScriptNpmScript> Scripts { get; set; } = [];

    public List<string> ReportPaths { get; set; } = [];
}

public sealed class TypeScriptRouteInfo
{
    public string Path { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;
}

public sealed class TypeScriptWorkerSymbol
{
    public string Path { get; set; } = string.Empty;

    public int Line { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;
}

public sealed class TypeScriptProjectMapEntry
{
    public string Role { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;
}

public sealed class WorkerCapability
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}

public sealed class WorkerScaffoldRecommendation
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public List<string> Files { get; set; } = [];

    public List<string> VerificationCommands { get; set; } = [];
}
