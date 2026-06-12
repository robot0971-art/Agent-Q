using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AgentQ.Core.Models;
using AgentQ.Core.Providers;
using AgentQ.Tools;

namespace AgentQ.Desktop.Services;

public sealed class MultiAgentOrchestrator
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
    private readonly RecoveryStrategyRouter _recoveryRouter = new();

    public MultiAgentOrchestrator(
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

    public async Task<TaskExecutionResult> OrchestrateAsync(
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

        callbacks?.OnRunStep?.Invoke(AgentRunState.Planning, "Multi-Agent Orchestration Start", $"Running multi-agent plan for goal: {plan.Goal}");

        // Step 1: Planner verifies/enhances the plan
        var plannerRole = AgentRoleCatalog.Planner;
        var plannerConfig = CopyConfig(config, plannerRole.AllowedTools);
        callbacks?.OnRunStep?.Invoke(AgentRunState.Planning, "Planner Reviewing", "Planner agent is reviewing the plan.");

        // Step 2: Coder & Reviewer loop for each step
        foreach (var step in plan.Steps)
        {
            callbacks?.OnRunStep?.Invoke(
                AgentRunState.Planning,
                $"Orchestrating Step {step.Order}/{plan.Steps.Count}",
                step.Description);

            var context = await _contextSelector.BuildTaskContextAsync(step, workspaceAnalysis, symbolIndex, workspaceRoot, ct);

            // Run Coder Agent
            var coderRole = AgentRoleCatalog.Coder;
            var coderConfig = CopyConfig(config, coderRole.AllowedTools);
            var coderAgent = CreateAgent();

            var coderPrompt = $"""
[ROLE: CODER]
{coderRole.SystemPromptOverride}

Task to execute:
"{step.Description}"

Context:
{context}
""";

            var coderOutput = await coderAgent.SendAsync(
                coderConfig,
                coderPrompt,
                workspaceRoot: workspaceRoot,
                permissionEnforcer: enforcer,
                toolCallbacks: callbacks,
                ct: ct);

            // Run Reviewer Agent to verify Coder's changes
            var reviewerRole = AgentRoleCatalog.Reviewer;
            var reviewerConfig = CopyConfig(config, reviewerRole.AllowedTools);
            var reviewerAgent = CreateAgent();

            var reviewerPrompt = $"""
[ROLE: REVIEWER]
{reviewerRole.SystemPromptOverride}

Please review the following implementation and outputs for Step {step.Order}:
"{step.Description}"

Coder Output/Summary:
{coderOutput}
""";

            var reviewerOutput = await reviewerAgent.SendAsync(
                reviewerConfig,
                reviewerPrompt,
                workspaceRoot: workspaceRoot,
                permissionEnforcer: enforcer,
                toolCallbacks: callbacks,
                ct: ct);

            // Run Tester Agent to verify step if VerificationCommand is provided
            var stepSucceeded = true;
            var verificationCommand = TaskExecutor.GetAllowedVerificationCommand(step.VerificationCommand);
            if (!string.IsNullOrWhiteSpace(verificationCommand))
            {
                var testerRole = AgentRoleCatalog.Tester;
                var testerConfig = CopyConfig(config, testerRole.AllowedTools);
                var testerAgent = CreateAgent();

                var testerPrompt = $"""
[ROLE: TESTER]
{testerRole.SystemPromptOverride}

Run this verification command to confirm the step:
Command: {verificationCommand}
""";
                var testerOutput = await testerAgent.SendAsync(
                    testerConfig,
                    testerPrompt,
                    workspaceRoot: workspaceRoot,
                    permissionEnforcer: enforcer,
                    toolCallbacks: callbacks,
                    ct: ct);

                stepSucceeded = TaskExecutor.IsStepOutputSuccessful(testerOutput) &&
                                !testerOutput.Contains("failed", StringComparison.OrdinalIgnoreCase) &&
                                !testerOutput.Contains("error", StringComparison.OrdinalIgnoreCase);
            }

            var stepResult = new TaskStepResult
            {
                Step = step,
                Succeeded = stepSucceeded,
                Summary = $"Coder: {coderOutput}\n\nReviewer: {reviewerOutput}"
            };

            result.StepResults.Add(stepResult);

            if (!stepSucceeded)
            {
                result.AllSucceeded = false;
                callbacks?.OnRunStep?.Invoke(AgentRunState.Failed, $"Orchestration Step {step.Order} Failed", "Aborting orchestration.");
                break;
            }
        }

        return result;
    }

    private DesktopAgentService CreateAgent()
    {
        return new DesktopAgentService(
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
    }

    private ProviderConfiguration CopyConfig(ProviderConfiguration config, IReadOnlyList<string> allowedTools)
    {
        var copy = new ProviderConfiguration
        {
            Provider = config.Provider,
            Model = config.Model,
            BaseUrl = config.BaseUrl,
            ApiKey = config.ApiKey,
            TimeoutSeconds = config.TimeoutSeconds,
            MaxTokens = config.MaxTokens,
            DesktopWorkMode = config.DesktopWorkMode
        };
        copy.AllowedToolNames.AddRange(allowedTools);
        return copy;
    }
}
