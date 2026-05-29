namespace AgentQ.Desktop.Services;

public sealed class NativeWorkerResult
{
    public string Worker { get; set; } = string.Empty;

    public int Version { get; set; }

    public string Root { get; set; } = string.Empty;

    public NativeCppInfo Cpp { get; set; } = new();

    public NativeGoInfo Go { get; set; } = new();

    public NativeRustInfo Rust { get; set; } = new();

    public NativeJavaInfo Java { get; set; } = new();

    public NativeSqlInfo Sql { get; set; } = new();

    public NativePhpInfo Php { get; set; } = new();

    public NativeKotlinInfo Kotlin { get; set; } = new();

    public NativeSwiftInfo Swift { get; set; } = new();

    public NativeScriptInfo Scripts { get; set; } = new();

    public NativeRInfo R { get; set; } = new();

    public List<NativeProjectMapEntry> ProjectMap { get; set; } = [];

    public List<WorkerCapability> Capabilities { get; set; } = [];

    public List<WorkerScaffoldRecommendation> ScaffoldRecommendations { get; set; } = [];

    public List<string> Warnings { get; set; } = [];
}

public sealed class NativeCppInfo
{
    public List<NativeCMakeProject> CmakeProjects { get; set; } = [];

    public List<NativeCompileCommands> CompileCommands { get; set; } = [];

    public int CompileCommandCount { get; set; }

    public List<NativeVcxProject> Vcxprojects { get; set; } = [];

    public List<string> SourceFiles { get; set; } = [];

    public List<string> HeaderFiles { get; set; } = [];

    public List<string> Tooling { get; set; } = [];
}

public sealed class NativeCMakeProject
{
    public string Path { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

public sealed class NativeCompileCommands
{
    public string Path { get; set; } = string.Empty;

    public int Count { get; set; }
}

public sealed class NativeVcxProject
{
    public string Path { get; set; } = string.Empty;
}

public sealed class NativeGoInfo
{
    public List<NativeGoModule> Modules { get; set; } = [];

    public List<NativeGoPackage> Packages { get; set; } = [];

    public List<string> SourceFiles { get; set; } = [];

    public List<string> Tooling { get; set; } = [];
}

public sealed class NativeGoModule
{
    public string Path { get; set; } = string.Empty;

    public string Module { get; set; } = string.Empty;

    public string GoVersion { get; set; } = string.Empty;
}

public sealed class NativeGoPackage
{
    public string ImportPath { get; set; } = string.Empty;

    public string Directory { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

public sealed class NativeRustInfo
{
    public List<NativeCargoManifest> Manifests { get; set; } = [];

    public List<NativeRustPackage> Packages { get; set; } = [];

    public List<NativeRustTarget> Targets { get; set; } = [];

    public List<string> SourceFiles { get; set; } = [];

    public List<string> Tooling { get; set; } = [];
}

public sealed class NativeCargoManifest
{
    public string Path { get; set; } = string.Empty;

    public string PackageName { get; set; } = string.Empty;

    public bool IsWorkspace { get; set; }
}

public sealed class NativeRustPackage
{
    public string Name { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string ManifestPath { get; set; } = string.Empty;
}

public sealed class NativeRustTarget
{
    public string PackageName { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;
}

public sealed class NativeJavaInfo
{
    public List<NativePathItem> BuildFiles { get; set; } = [];

    public List<string> SourceFiles { get; set; } = [];

    public List<string> TestFiles { get; set; } = [];

    public List<NativeLanguageSymbol> Symbols { get; set; } = [];

    public List<string> Frameworks { get; set; } = [];

    public List<string> Tooling { get; set; } = [];
}

public sealed class NativeSqlInfo
{
    public List<string> Files { get; set; } = [];

    public List<string> Migrations { get; set; } = [];

    public List<NativeLanguageSymbol> Tables { get; set; } = [];

    public List<string> Tooling { get; set; } = [];
}

public sealed class NativePhpInfo
{
    public List<NativePathItem> ComposerFiles { get; set; } = [];

    public List<string> SourceFiles { get; set; } = [];

    public List<string> TestFiles { get; set; } = [];

    public List<NativeLanguageSymbol> Symbols { get; set; } = [];

    public List<string> Frameworks { get; set; } = [];

    public List<string> Tooling { get; set; } = [];
}

public sealed class NativeKotlinInfo
{
    public List<NativePathItem> BuildFiles { get; set; } = [];

    public List<string> SourceFiles { get; set; } = [];

    public List<string> TestFiles { get; set; } = [];

    public List<NativeLanguageSymbol> Symbols { get; set; } = [];

    public List<string> Frameworks { get; set; } = [];

    public List<string> Tooling { get; set; } = [];
}

public sealed class NativeSwiftInfo
{
    public List<NativePathItem> PackageFiles { get; set; } = [];

    public List<NativePathItem> ProjectFiles { get; set; } = [];

    public List<string> SourceFiles { get; set; } = [];

    public List<string> TestFiles { get; set; } = [];

    public List<NativeLanguageSymbol> Symbols { get; set; } = [];

    public List<string> Frameworks { get; set; } = [];

    public List<string> Tooling { get; set; } = [];
}

public sealed class NativeScriptInfo
{
    public List<string> ShellFiles { get; set; } = [];

    public List<string> PowerShellFiles { get; set; } = [];

    public List<NativeLanguageSymbol> Commands { get; set; } = [];

    public List<string> Tooling { get; set; } = [];
}

public sealed class NativeRInfo
{
    public List<NativePathItem> ProjectFiles { get; set; } = [];

    public List<string> SourceFiles { get; set; } = [];

    public List<string> ReportFiles { get; set; } = [];

    public List<NativeLanguageSymbol> Symbols { get; set; } = [];

    public List<string> Tooling { get; set; } = [];
}

public sealed class NativePathItem
{
    public string Path { get; set; } = string.Empty;
}

public sealed class NativeLanguageSymbol
{
    public string Path { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

public sealed class NativeProjectMapEntry
{
    public string Role { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;
}
