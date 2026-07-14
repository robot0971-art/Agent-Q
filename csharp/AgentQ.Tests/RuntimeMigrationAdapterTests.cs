using AgentQ.Desktop.Services;
using AgentQ.Runtime.Completion;
using AgentQ.Runtime.Intent;
using Xunit;

namespace AgentQ.Tests;

public sealed class RuntimeMigrationAdapterTests
{
    [Fact]
    public void IntentAdapter_RuntimeVeto_NarrowsLegacyExecutionWithoutCreatingContract()
    {
        const string userText = "logs 폴더 만들어줘";
        var understanding = UserTurnUnderstandingService.Understand(userText);
        var rule = TurnIntentClassifier.Classify(userText);
        var adapter = new DesktopIntentRoutingAdapter(new VetoPipeline());

        var route = adapter.Route(userText, understanding, rule);

        Assert.Equal(TurnIntentType.Ambiguous, route.EffectiveIntent.Type);
        Assert.False(route.EffectiveIntent.AllowsDeterministicExecution);
        Assert.False(route.ExecutionContract.IsActionable);
        Assert.Contains("runtime-veto=true", route.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletionAdapter_RuntimeEvidenceFloor_RejectsTextOnlyMutationCompletion()
    {
        var contract = UserIntentTranslator.Translate("logs 폴더 만들어줘");
        var adapter = new DesktopTaskContractCompletionAdapter(new CompletionSafetyPolicy());

        var rejected = adapter.ShouldReject(contract, "작업을 완료했습니다.", [], AgentWorkMode.Coding, []);

        Assert.True(rejected);
    }

    [Fact]
    public void CompletionAdapter_RuntimeEvidenceFloor_DoesNotRejectMutationWithToolEvidenceWhenLegacyAllows()
    {
        var contract = UserIntentTranslator.Translate("logs 폴더 만들어줘");
        var adapter = new DesktopTaskContractCompletionAdapter(new CompletionSafetyPolicy());
        var replay = new[] { new ToolReplayEntry { ToolName = "create_directory", IsError = false } };

        var rejected = adapter.ShouldReject(contract, "logs 폴더를 생성했습니다.", [], AgentWorkMode.Coding, replay);

        Assert.False(rejected);
    }

    private sealed class VetoPipeline : IIntentRoutingPipeline
    {
        public IntentRoutingDecision Route(IntentRoutingRequest request) =>
            new(AgentTurnIntent.Ambiguous, false, true, false, "test-runtime-veto");
    }
}
