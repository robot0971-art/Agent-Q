namespace AgentQ.Desktop.Services;

public sealed class ProjectMemoryGcReport
{
    public int BeforeCount { get; init; }

    public int AfterCount { get; init; }

    public int RemovedCount => BeforeCount - AfterCount;

    public List<ProjectMemoryGcItem> RemovedLessons { get; init; } = [];
}

public sealed class ProjectMemoryGcItem
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string Reason { get; init; }
}

public sealed class ProjectMemoryGcOptions
{
    public int ExpireUnusedAfterDays { get; init; } = 180;

    public double MinimumConfidence { get; init; } = 0.2;

    public int MaxLessons { get; init; } = 120;
}
