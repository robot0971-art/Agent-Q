namespace AgentQ.Desktop.Services;

public sealed class PythonWorkerResult
{
    public string Worker { get; set; } = string.Empty;

    public int Version { get; set; }

    public string Root { get; set; } = string.Empty;

    public List<PythonProjectInfo> Pyprojects { get; set; } = [];

    public List<PythonRequirementsInfo> Requirements { get; set; } = [];

    public List<PythonImportInfo> Imports { get; set; } = [];

    public List<PythonCallSiteInfo> CallSites { get; set; } = [];

    public List<PythonWorkerSymbol> Symbols { get; set; } = [];

    public List<PythonFastApiRoute> FastApiRoutes { get; set; } = [];

    public List<PythonWebRoute> WebRoutes { get; set; } = [];

    public List<PythonSqlAlchemyModel> SqlAlchemyModels { get; set; } = [];

    public List<PythonCeleryTask> CeleryTasks { get; set; } = [];

    public List<PythonCliCommand> CliCommands { get; set; } = [];

    public List<PythonPytestTarget> PytestTargets { get; set; } = [];

    public List<PythonProjectMapEntry> ProjectMap { get; set; } = [];

    public List<WorkerCapability> Capabilities { get; set; } = [];

    public List<WorkerScaffoldRecommendation> ScaffoldRecommendations { get; set; } = [];

    public List<string> FailureHints { get; set; } = [];

    public List<string> Warnings { get; set; } = [];
}

public sealed class PythonProjectInfo
{
    public string Path { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<string> Dependencies { get; set; } = [];
}

public sealed class PythonRequirementsInfo
{
    public string Path { get; set; } = string.Empty;

    public List<string> Dependencies { get; set; } = [];
}

public sealed class PythonImportInfo
{
    public string Path { get; set; } = string.Empty;

    public int Line { get; set; }

    public string Module { get; set; } = string.Empty;

    public string ImportedName { get; set; } = string.Empty;

    public int Level { get; set; }

    public string ResolvedPath { get; set; } = string.Empty;
}

public sealed class PythonCallSiteInfo
{
    public string Path { get; set; } = string.Empty;

    public int Line { get; set; }

    public string Name { get; set; } = string.Empty;

    public string EnclosingSymbol { get; set; } = string.Empty;
}

public sealed class PythonWorkerSymbol
{
    public string Path { get; set; } = string.Empty;

    public int Line { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Language { get; set; } = "Python";
}

public sealed class PythonFastApiRoute
{
    public string Path { get; set; } = string.Empty;

    public int Line { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Method { get; set; } = string.Empty;

    public string Route { get; set; } = string.Empty;
}

public sealed class PythonWebRoute
{
    public string Path { get; set; } = string.Empty;

    public int Line { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Framework { get; set; } = string.Empty;

    public string Method { get; set; } = string.Empty;

    public string Route { get; set; } = string.Empty;
}

public sealed class PythonSqlAlchemyModel
{
    public string Path { get; set; } = string.Empty;

    public int Line { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class PythonCeleryTask
{
    public string Path { get; set; } = string.Empty;

    public int Line { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class PythonCliCommand
{
    public string Path { get; set; } = string.Empty;

    public int Line { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Framework { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;
}

public sealed class PythonPytestTarget
{
    public string Path { get; set; } = string.Empty;

    public int Line { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

public sealed class PythonProjectMapEntry
{
    public string Role { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;
}
