using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AgentQ.Core.Models;
using AgentQ.Core.Providers;
using AgentQ.Tools;

namespace AgentQ.Desktop.Services;

public sealed class TaskStepResult
{
    public TaskStep Step { get; set; } = new();
    public bool Succeeded { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<FileChangeRecord> FileChanges { get; set; } = [];
    public VerificationFailureAnalysis? FailureAnalysis { get; set; }
}

public sealed class TaskExecutionResult
{
    public bool AllSucceeded { get; set; }
    public List<TaskStepResult> StepResults { get; set; } = [];
    public List<FileChangeRecord> AllFileChanges { get; set; } = [];
}

public sealed class TaskExecutor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LinkContentFetcher _linkContentFetcher;
    private readonly ProjectMemoryService _projectMemoryService;
    private readonly WorkspaceIndexer _workspaceIndexer;
    private readonly EmbeddingIndexStore _embeddingIndexStore;
    private readonly DesktopEmbeddingClientFactory _embeddingClientFactory;
    private readonly FileMutationSnapshotService _fileMutationSnapshotService;
    private readonly ToolReplayService _toolReplayService;
    private readonly WorkspaceSymbolIndexService _symbolIndexService;
    private readonly WorkspaceAnalysisService _workspaceAnalysisService;
    private readonly TaskContextSelector _contextSelector = new();

    public TaskExecutor(
        IHttpClientFactory httpClientFactory,
        LinkContentFetcher linkContentFetcher,
        ProjectMemoryService projectMemoryService,
        WorkspaceIndexer workspaceIndexer,
        EmbeddingIndexStore embeddingIndexStore,
        DesktopEmbeddingClientFactory embeddingClientFactory,
        FileMutationSnapshotService fileMutationSnapshotService,
        ToolReplayService toolReplayService,
        WorkspaceSymbolIndexService symbolIndexService,
        WorkspaceAnalysisService workspaceAnalysisService)
    {
        _httpClientFactory = httpClientFactory;
        _linkContentFetcher = linkContentFetcher;
        _projectMemoryService = projectMemoryService;
        _workspaceIndexer = workspaceIndexer;
        _embeddingIndexStore = embeddingIndexStore;
        _embeddingClientFactory = embeddingClientFactory;
        _fileMutationSnapshotService = fileMutationSnapshotService;
        _toolReplayService = toolReplayService;
        _symbolIndexService = symbolIndexService;
        _workspaceAnalysisService = workspaceAnalysisService;
    }

    public async Task<TaskExecutionResult> ExecuteAsync(
        TaskPlan plan,
        ProviderConfiguration config,
        string workspaceRoot,
        IPermissionEnforcer enforcer,
        DesktopToolCallbacks? callbacks,
        CancellationToken ct)
    {
        var result = new TaskExecutionResult { AllSucceeded = true };
        var workspaceAnalysis = await _workspaceAnalysisService.AnalyzeAsync(workspaceRoot, ct);
        var symbolIndex = _symbolIndexService.Build(workspaceRoot);

        callbacks?.OnRunStep?.Invoke(AgentRunState.Planning, "Execution start", $"Executing {plan.Steps.Count} steps...");

        foreach (var step in plan.Steps)
        {
            callbacks?.OnRunStep?.Invoke(
                AgentRunState.Planning,
                $"Step {step.Order}/{plan.Steps.Count}",
                step.Description);

            // Construct minimal task context
            var taskContext = await _contextSelector.BuildTaskContextAsync(
                step,
                workspaceAnalysis,
                symbolIndex,
                workspaceRoot,
                ct);

            // Create a new DesktopAgentService for an independent session
            var agent = new DesktopAgentService(
                _httpClientFactory,
                _linkContentFetcher,
                _projectMemoryService,
                _workspaceIndexer,
                _embeddingIndexStore,
                _embeddingClientFactory,
                _fileMutationSnapshotService,
                _toolReplayService,
                _symbolIndexService,
                _workspaceAnalysisService);

            var userPrompt = $"""
Please perform this specific task:
"{step.Description}"

Context from files and symbols:
{taskContext}

Verification Command to run if applicable: {step.VerificationCommand}
""";

            try
            {
                var stepOutput = await agent.SendAsync(
                    config,
                    userPrompt,
                    workspaceRoot: workspaceRoot,
                    permissionEnforcer: enforcer,
                    toolCallbacks: callbacks,
                    ct: ct);

                var succeeded = IsStepOutputSuccessful(stepOutput);

                var stepResult = new TaskStepResult
                {
                    Step = step,
                    Succeeded = succeeded,
                    Summary = stepOutput
                };

                result.StepResults.Add(stepResult);

                if (!succeeded)
                {
                    result.AllSucceeded = false;
                    callbacks?.OnRunStep?.Invoke(
                        AgentRunState.Failed,
                        $"Step {step.Order} Failed",
                        "Stopping plan execution due to step failure.");
                    break;
                }
            }
            catch (Exception ex)
            {
                result.AllSucceeded = false;
                result.StepResults.Add(new TaskStepResult
                {
                    Step = step,
                    Succeeded = false,
                    Summary = $"Exception occurred: {ex.Message}"
                });
                break;
            }
        }

        return result;
    }

    public static bool IsStepOutputSuccessful(string stepOutput) =>
        DesktopAgentRunWorkflowService.BuildRunCompletionOutcome(stepOutput).Succeeded;
}
