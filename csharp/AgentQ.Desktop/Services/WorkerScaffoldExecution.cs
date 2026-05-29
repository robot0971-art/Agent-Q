namespace AgentQ.Desktop.Services;

public sealed class WorkerScaffoldExecutionRequest
{
    public required WorkerPlan Plan { get; init; }

    public required string WorkspaceRoot { get; init; }

    public string FeatureName { get; init; } = "Feature";

    public bool OverwriteExistingFiles { get; init; }

    public bool EnableAutoWiring { get; init; } = true;

    public WorkerScaffoldContext? ScaffoldContext { get; init; }
}

public sealed class WorkerScaffoldExecutionResult
{
    public bool Succeeded { get; set; }

    public List<string> CreatedFiles { get; init; } = [];

    public List<string> SkippedFiles { get; init; } = [];

    public List<string> WiredFiles { get; init; } = [];

    public List<string> Issues { get; init; } = [];

    public List<string> VerificationCommands { get; init; } = [];
}
