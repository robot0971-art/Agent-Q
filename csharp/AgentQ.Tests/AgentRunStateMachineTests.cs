using AgentQ.Runtime.Runs;
using AgentQ.Runtime.Intent;
using AgentQ.Runtime.Contracts;
using AgentQ.Runtime.Evidence;
using AgentQ.Runtime.Completion;
using AgentQ.Runtime.Dispatch;
using AgentQ.Runtime.Repair;
using AgentQ.Runtime.Model;
using Xunit;

namespace AgentQ.Tests;

public sealed class AgentRunStateMachineTests
{
    private readonly AgentRunStateMachine _stateMachine = new();

    [Theory]
    [InlineData(AgentRunStatus.Received, AgentRunStatus.Understanding)]
    [InlineData(AgentRunStatus.Understanding, AgentRunStatus.Conversation)]
    [InlineData(AgentRunStatus.Understanding, AgentRunStatus.AwaitingClarification)]
    [InlineData(AgentRunStatus.Planning, AgentRunStatus.AwaitingApproval)]
    [InlineData(AgentRunStatus.AwaitingApproval, AgentRunStatus.ReadyToExecute)]
    [InlineData(AgentRunStatus.ReadyToExecute, AgentRunStatus.Executing)]
    [InlineData(AgentRunStatus.Executing, AgentRunStatus.Verifying)]
    [InlineData(AgentRunStatus.Verifying, AgentRunStatus.Repairing)]
    [InlineData(AgentRunStatus.Repairing, AgentRunStatus.Executing)]
    [InlineData(AgentRunStatus.Executing, AgentRunStatus.Recovering)]
    [InlineData(AgentRunStatus.Recovering, AgentRunStatus.RolledBack)]
    public void CanTransition_AllowsDeclaredPaths(AgentRunStatus previous, AgentRunStatus next)
    {
        Assert.True(_stateMachine.CanTransition(previous, next));
    }

    [Theory]
    [InlineData(AgentRunStatus.Received, AgentRunStatus.Completed)]
    [InlineData(AgentRunStatus.Planning, AgentRunStatus.Executing)]
    [InlineData(AgentRunStatus.Executing, AgentRunStatus.Completed)]
    [InlineData(AgentRunStatus.Completed, AgentRunStatus.Executing)]
    [InlineData(AgentRunStatus.RolledBack, AgentRunStatus.Completed)]
    public void Transition_RejectsUndeclaredPaths(AgentRunStatus previous, AgentRunStatus next)
    {
        var request = CreateRequest(previous, next, contractId: "contract-1", evidenceId: "evidence-1");

        Assert.Throws<InvalidOperationException>(() => _stateMachine.Transition(request));
    }

    [Fact]
    public void Transition_RecordsRequiredAuditFields()
    {
        var occurredAt = new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        var transition = _stateMachine.Transition(CreateRequest(
            AgentRunStatus.ReadyToExecute,
            AgentRunStatus.Executing,
            contractId: "contract-1",
            occurredAt: occurredAt));

        Assert.Equal("run-1", transition.RunId);
        Assert.Equal("contract-1", transition.ContractId);
        Assert.Equal("approved-contract", transition.ReasonCode);
        Assert.Equal("runtime-policy-v1", transition.PolicyVersion);
        Assert.Equal(occurredAt, transition.OccurredAt);
    }

    [Fact]
    public void Transition_RequiresContractForExecutionStates()
    {
        var request = CreateRequest(AgentRunStatus.ReadyToExecute, AgentRunStatus.Executing);

        Assert.Throws<ArgumentException>(() => _stateMachine.Transition(request));
    }

    [Fact]
    public void Transition_RequiresEvidenceToCompleteVerification()
    {
        var request = CreateRequest(
            AgentRunStatus.Verifying,
            AgentRunStatus.Completed,
            contractId: "contract-1");

        Assert.Throws<ArgumentException>(() => _stateMachine.Transition(request));
    }

    [Fact]
    public void Transition_AllowsConversationCompletionWithoutMutationEvidence()
    {
        var transition = _stateMachine.Transition(CreateRequest(
            AgentRunStatus.Conversation,
            AgentRunStatus.Completed));

        Assert.Null(transition.ContractId);
        Assert.Null(transition.EvidenceId);
    }

    private static AgentRunTransitionRequest CreateRequest(
        AgentRunStatus previous,
        AgentRunStatus next,
        string? contractId = null,
        string? evidenceId = null,
        DateTimeOffset? occurredAt = null) =>
        new(
            "run-1",
            contractId,
            previous,
            next,
            "approved-contract",
            "runtime-policy-v1",
            evidenceId,
            occurredAt);
}

public sealed class AgentRunCoordinatorTests
{
    [Fact]
    public void Start_CreatesRunScopedSessionWithReceivedEvent()
    {
        var coordinator = new AgentRunCoordinator(new AgentRunStateMachine());
        var startedAt = new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

        var session = coordinator.Start("run-1", "runtime-policy-v1", startedAt);

        Assert.Equal(AgentRunStatus.Received, session.Status);
        Assert.Single(session.History);
        Assert.Equal("run-received", session.Current.ReasonCode);
        Assert.Equal(startedAt, session.Current.OccurredAt);
    }

    [Fact]
    public void Advance_PreservesContractAndCreatesAppendOnlyHistory()
    {
        var session = new AgentRunCoordinator(new AgentRunStateMachine()).Start("run-1", "runtime-policy-v1");

        session.Advance(AgentRunStatus.Understanding, "begin-understanding");
        session.Advance(AgentRunStatus.Planning, "action-request-understood");
        session.Advance(AgentRunStatus.AwaitingApproval, "approval-required", contractId: "contract-1");
        session.Advance(AgentRunStatus.ReadyToExecute, "approval-granted");

        Assert.Equal(5, session.History.Count);
        Assert.Equal("contract-1", session.Current.ContractId);
        Assert.Equal(AgentRunStatus.ReadyToExecute, session.Status);
    }
}

public sealed class IntentRoutingPipelineTests
{
    private readonly IntentRoutingPipeline _pipeline = new();

    [Fact]
    public void Route_PreservesConcreteCurrentActionWhenModelSaysConversation()
    {
        var decision = _pipeline.Route(new(AgentTurnIntent.Conversation, true, true, true, true, "model-conversation"));

        Assert.Equal(AgentTurnIntent.Action, decision.EffectiveIntent);
        Assert.True(decision.ShouldBuildTaskContract);
        Assert.True(decision.ShouldAllowExecution);
    }

    [Fact]
    public void Route_RequiresClarificationForActionWithoutConcreteTarget()
    {
        var decision = _pipeline.Route(new(AgentTurnIntent.Action, true, false, true, true, "bare-action"));

        Assert.Equal(AgentTurnIntent.Ambiguous, decision.EffectiveIntent);
        Assert.True(decision.ShouldRequestClarification);
        Assert.False(decision.ShouldBuildTaskContract);
    }

    [Fact]
    public void Route_NeverAllowsNonCurrentContextToExecute()
    {
        var decision = _pipeline.Route(new(AgentTurnIntent.Action, false, true, true, true, "pasted-command"));

        Assert.Equal(AgentTurnIntent.Conversation, decision.EffectiveIntent);
        Assert.False(decision.ShouldAllowExecution);
        Assert.False(decision.ShouldBuildTaskContract);
    }

    [Fact]
    public void Route_PreservesContractButDoesNotBypassExecutionPolicy()
    {
        var decision = _pipeline.Route(new(AgentTurnIntent.Hybrid, true, true, true, false, "approval-needed"));

        Assert.Equal(AgentTurnIntent.Hybrid, decision.EffectiveIntent);
        Assert.True(decision.ShouldBuildTaskContract);
        Assert.False(decision.ShouldAllowExecution);
    }
}

public sealed class TaskContractFactoryTests
{
    [Fact]
    public void Create_ProducesStableHashAndPreservesExternalScaffoldIdentity()
    {
        var request = new RuntimeTaskContractRequest(
            "workspace-1", AgentTurnIntent.Action, "Create a project", ["src/App.tsx"], ["write", "shell"],
            ["create-project"], ["npm test"], ["project-created"],
            new DateTimeOffset(2026, 7, 12, 0, 0, 0, TimeSpan.Zero), "plan-1", "plan-hash-1");
        var factory = new TaskContractFactory();

        var first = factory.Create(request, "contract-1");
        var second = factory.Create(request, "contract-2");

        Assert.Equal(first.Hash, second.Hash);
        Assert.Equal("plan-1", first.ExternalPlanId);
        Assert.Equal("plan-hash-1", first.ExternalPlanHash);
        Assert.Equal("contract-1", first.ContractId);
    }

    [Fact]
    public void Create_ChangesHashWhenApprovedScopeChanges()
    {
        var expiresAt = new DateTimeOffset(2026, 7, 12, 0, 0, 0, TimeSpan.Zero);
        var factory = new TaskContractFactory();
        var baseline = factory.Create(new("workspace-1", AgentTurnIntent.Action, "Fix bug", ["src/A.cs"], ["write"], ["edit"], ["dotnet test"], ["tests-pass"], expiresAt));
        var expanded = factory.Create(new("workspace-1", AgentTurnIntent.Action, "Fix bug", ["src/A.cs", "src/B.cs"], ["write"], ["edit"], ["dotnet test"], ["tests-pass"], expiresAt));

        Assert.NotEqual(baseline.Hash, expanded.Hash);
    }
}

public sealed class RunEvidenceCollectorTests
{
    [Fact]
    public void Record_KeepsEvidenceIsolatedByRunAndPreservesArtifactReference()
    {
        var collector = new RunEvidenceCollector();
        var recordedAt = new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

        var evidence = collector.Record("run-1", "contract-1", RunEvidenceKind.Verification, "Focused tests passed", ".agentq/runs/run-1/test.trx", recordedAt);
        collector.Record("run-2", "contract-2", RunEvidenceKind.Mutation, "File changed");

        var entries = collector.GetForRun("run-1");
        Assert.Single(entries);
        Assert.Equal(evidence, entries[0]);
        Assert.Equal(".agentq/runs/run-1/test.trx", evidence.ArtifactReference);
        Assert.True(collector.HasEvidence("run-1", RunEvidenceKind.Verification));
        Assert.False(collector.HasEvidence("run-1", RunEvidenceKind.Mutation));
    }
}

public sealed class CompletionEvaluatorTests
{
    [Fact]
    public void Evaluate_RejectsTextOnlyCompletionWhenNoEvidenceExists()
    {
        var contract = CreateContract(["mutation: created src/App.cs", "verification: tests passed"]);

        var result = new CompletionEvaluator().Evaluate(contract, []);

        Assert.False(result.IsComplete);
        Assert.Equal(2, result.MissingConditions.Count);
    }

    [Fact]
    public void Evaluate_CompletesOnlyWhenContractScopedEvidenceSatisfiesEveryCondition()
    {
        var contract = CreateContract(["mutation: created src/App.cs", "verification: tests passed"]);
        var evidence = new RunEvidenceCollector();
        evidence.Record("run-1", contract.ContractId, RunEvidenceKind.Mutation, "created src/App.cs");
        evidence.Record("run-1", contract.ContractId, RunEvidenceKind.Verification, "tests passed");
        evidence.Record("run-2", contract.ContractId, RunEvidenceKind.Verification, "tests passed");

        var result = new CompletionEvaluator().Evaluate(contract, evidence.GetForRun("run-1"));

        Assert.True(result.IsComplete);
        Assert.Empty(result.MissingConditions);
        Assert.Equal(2, result.EvidenceIds.Count);
    }

    private static RuntimeTaskContract CreateContract(IReadOnlyList<string> completionConditions) =>
        new TaskContractFactory().Create(new(
            "workspace-1", AgentTurnIntent.Action, "Implement feature", ["src/App.cs"], ["write"],
            ["create-file"], ["dotnet test"], completionConditions,
            new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero)), "contract-1");
}

public sealed class DeterministicActionDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_BlocksExpiredContractBeforeCallingHandler()
    {
        var handler = new FakeHandler();
        var dispatcher = new DeterministicActionDispatcher([handler]);
        var result = await dispatcher.DispatchAsync(CreateRequest(expiresAt: DateTimeOffset.UnixEpoch));

        Assert.False(result.Dispatched);
        Assert.Equal("contract-expired", result.ReasonCode);
        Assert.False(handler.WasCalled);
    }

    [Fact]
    public async Task DispatchAsync_BlocksUnapprovedCapabilityAndTarget()
    {
        var handler = new FakeHandler();
        var dispatcher = new DeterministicActionDispatcher([handler]);

        var capability = await dispatcher.DispatchAsync(CreateRequest(requiredCapability: "shell"));
        var target = await dispatcher.DispatchAsync(CreateRequest(target: "outside.txt"));

        Assert.Equal("capability-not-approved", capability.ReasonCode);
        Assert.Equal("target-not-approved", target.ReasonCode);
        Assert.False(handler.WasCalled);
    }

    [Fact]
    public async Task DispatchAsync_UsesRegisteredHandlerInsideApprovedScope()
    {
        var handler = new FakeHandler();
        var result = await new DeterministicActionDispatcher([handler]).DispatchAsync(CreateRequest());

        Assert.True(result.Succeeded);
        Assert.True(result.Dispatched);
        Assert.True(handler.WasCalled);
    }

    private static DeterministicActionRequest CreateRequest(
        string requiredCapability = "write",
        string target = "src/App.cs",
        DateTimeOffset? expiresAt = null) =>
        new(new TaskContractFactory().Create(new(
                "workspace-1", AgentTurnIntent.Action, "Edit file", ["src/App.cs"], ["write"], ["edit-file"], [], ["mutation: edited"],
                expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(5)), "contract-1"),
            "edit-file", requiredCapability, target, DateTimeOffset.UtcNow);

    private sealed class FakeHandler : IDeterministicActionHandler
    {
        public string ActionName => "edit-file";
        public bool WasCalled { get; private set; }

        public Task<DeterministicActionResult> ExecuteAsync(DeterministicActionRequest request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(new DeterministicActionResult(true, true, "executed", "Edited file."));
        }
    }
}

public sealed class FinalAnswerConsistencyGuardTests
{
    [Fact]
    public void Evaluate_BlocksCompletionClaimWithoutEvidence()
    {
        var completion = new CompletionEvaluation(false, [], ["verification: tests passed"], []);

        var result = new FinalAnswerConsistencyGuard().Evaluate("작업을 완료했습니다.", completion);

        Assert.False(result.IsConsistent);
        Assert.True(result.ShouldBlockCompletionClaim);
        Assert.Equal("completion-evidence-missing", result.ReasonCode);
        Assert.Contains("verification", result.UserFacingSummary);
    }

    [Fact]
    public void Evaluate_AllowsEvidenceBackedCompletionClaim()
    {
        var completion = new CompletionEvaluation(true, ["verification: tests passed"], [], ["evidence-1"]);

        var result = new FinalAnswerConsistencyGuard().Evaluate("Completed with tests passing.", completion);

        Assert.True(result.IsConsistent);
        Assert.False(result.ShouldBlockCompletionClaim);
    }
}

public sealed class RepairCoordinatorTests
{
    private readonly RepairCoordinator _coordinator = new();
    private static readonly RepairPolicy Policy = new(2, 1);

    [Fact]
    public void Decide_AuthorizesNewFingerprintWithinBounds()
    {
        var decision = _coordinator.Decide(Policy, [], "compile:CS1002", false);

        Assert.True(decision.ShouldRepair);
        Assert.Equal("repair-authorized", decision.ReasonCode);
    }

    [Fact]
    public void Decide_StopsRepeatedFingerprintAndScopeExpansion()
    {
        var repeated = _coordinator.Decide(Policy, [new RepairAttempt(1, "compile:CS1002", false)], "compile:CS1002", false);
        var expanded = _coordinator.Decide(Policy, [], "dependency:package", true);

        Assert.False(repeated.ShouldRepair);
        Assert.Equal("repeated-failure-fingerprint", repeated.ReasonCode);
        Assert.False(expanded.ShouldRepair);
        Assert.Equal("scope-expansion-required", expanded.ReasonCode);
    }
}

public sealed class ModelToolLoopTests
{
    [Fact]
    public async Task RunAsync_ExecutesToolThenReturnsModelFinalText()
    {
        var port = new FakePort(
            new ModelToolLoopTurn(null, [new ModelToolCall("read_file", "a.cs")]),
            new ModelToolLoopTurn("Analysis complete.", []));

        var result = await new ModelToolLoop().RunAsync(new("run-1", 3, "context"), port);

        Assert.Equal("Analysis complete.", result.FinalText);
        Assert.Equal(2, result.StepsExecuted);
        Assert.False(result.HitStepLimit);
        Assert.Equal(["read_file:a.cs"], result.ToolResults);
    }

    [Fact]
    public async Task RunAsync_StopsAtStepLimitWithoutTreatingItAsCompletion()
    {
        var port = new FakePort(
            new ModelToolLoopTurn(null, [new ModelToolCall("read_file", "a.cs")]),
            new ModelToolLoopTurn(null, [new ModelToolCall("read_file", "a.cs")]));

        var result = await new ModelToolLoop().RunAsync(new("run-1", 2, "context"), port);

        Assert.Null(result.FinalText);
        Assert.True(result.HitStepLimit);
        Assert.Equal(2, result.StepsExecuted);
    }

    private sealed class FakePort(params ModelToolLoopTurn[] turns) : IModelToolLoopPort
    {
        private readonly Queue<ModelToolLoopTurn> _turns = new(turns);

        public Task<ModelToolLoopTurn> GenerateAsync(ModelToolLoopRequest request, int step, IReadOnlyList<string> priorToolResults, CancellationToken cancellationToken) =>
            Task.FromResult(_turns.Count > 0 ? _turns.Dequeue() : new ModelToolLoopTurn(null, []));

        public Task<string> ExecuteToolAsync(ModelToolCall toolCall, CancellationToken cancellationToken) =>
            Task.FromResult($"{toolCall.ToolName}:{toolCall.Input}");
    }
}
