namespace AgentQ.Desktop.Services;

public sealed class NativeWorkerResult
{
    public string Worker { get; set; } = string.Empty;

    public int Version { get; set; }

    public string Root { get; set; } = string.Empty;

    public NativeCppInfo Cpp { get; set; } = new();

    public NativeGoInfo Go { get; set; } = new();

    public NativeRustInfo Rust { get; set; } = new();

    public List<NativeProjectMapEntry> ProjectMap { get; set; } = [];

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

public sealed class NativeProjectMapEntry
{
    public string Role { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;
}
