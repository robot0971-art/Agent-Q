using System.Reflection;
using AgentQ.Desktop;
using AgentQ.Desktop.Services;
using AgentQ.Runtime.Intent;
using AgentQ.Runtime.Contracts;
using AgentQ.Runtime.Evidence;
using AgentQ.Runtime.Completion;
using AgentQ.Runtime.Dispatch;
using AgentQ.Runtime.Repair;
using AgentQ.Runtime.Model;
using AgentQ.Runtime.Runs;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgentQ.Tests;

public sealed class RuntimeArchitectureTests
{
    [Fact]
    public void DesktopAgentService_UsesExtractedRoutingCompletionAndProviderSeams()
    {
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AgentQ.Desktop", "Services", "DesktopAgentService.cs");
        var source = File.ReadAllText(Path.GetFullPath(sourcePath));

        Assert.Contains("_intentRoutingAdapter.Route(", source);
        Assert.Contains("_taskContractCompletionAdapter.ShouldRetry(", source);
        Assert.Contains("_taskContractCompletionAdapter.ShouldReject(", source);
        Assert.Contains("_providerSessionFactory.Create(", source);
        Assert.DoesNotContain("LlmFirstIntentRouter.Route(", source);
        Assert.DoesNotContain("TaskContractCompletionChecker.ShouldRetry(", source);
        Assert.DoesNotContain("TaskContractCompletionChecker.ShouldReject(", source);
        Assert.DoesNotContain("new OpenAiCompatibleProvider(", source);
        Assert.DoesNotContain("new AnthropicProvider(", source);
    }

    [Fact]
    public void RuntimeAssembly_DoesNotReferenceWpfOrDesktop()
    {
        var references = typeof(AgentRunStateMachine).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        Assert.DoesNotContain("AgentQ.Desktop", references);
        Assert.DoesNotContain("PresentationCore", references);
        Assert.DoesNotContain("PresentationFramework", references);
        Assert.DoesNotContain("WindowsBase", references);
        Assert.DoesNotContain("System.Xaml", references);
    }

    [Fact]
    public void RuntimeAssembly_TargetsPortableNet10()
    {
        var targetFramework = typeof(AgentRunStateMachine).Assembly
            .GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()?.FrameworkName;

        Assert.Equal(".NETCoreApp,Version=v10.0", targetFramework);
    }

    [Fact]
    public void DesktopCompositionRoot_ProvidesRuntimeStateMachineAsStablePolicyService()
    {
        using var provider = new ServiceCollection()
            .AddAgentQDesktop()
            .BuildServiceProvider();

        var first = provider.GetRequiredService<IAgentRunStateMachine>();
        var second = provider.GetRequiredService<IAgentRunStateMachine>();
        var firstCoordinator = provider.GetRequiredService<IAgentRunCoordinator>();
        var secondCoordinator = provider.GetRequiredService<IAgentRunCoordinator>();
        var pipeline = provider.GetRequiredService<IIntentRoutingPipeline>();
        var contractFactory = provider.GetRequiredService<ITaskContractFactory>();
        var firstEvidenceCollector = provider.GetRequiredService<IRunEvidenceCollector>();
        var secondEvidenceCollector = provider.GetRequiredService<IRunEvidenceCollector>();
        var desktopRoutingAdapter = provider.GetRequiredService<IDesktopIntentRoutingAdapter>();
        var completionEvaluator = provider.GetRequiredService<ICompletionEvaluator>();
        var actionDispatcher = provider.GetRequiredService<IDeterministicActionDispatcher>();
        var finalAnswerGuard = provider.GetRequiredService<IFinalAnswerConsistencyGuard>();
        var repairCoordinator = provider.GetRequiredService<IRepairCoordinator>();
        var modelToolLoop = provider.GetRequiredService<IModelToolLoop>();
        var completionAdapter = provider.GetRequiredService<IDesktopTaskContractCompletionAdapter>();
        var providerSessionFactory = provider.GetRequiredService<IDesktopProviderSessionFactory>();

        Assert.IsType<AgentRunStateMachine>(first);
        Assert.Same(first, second);
        Assert.NotSame(firstCoordinator, secondCoordinator);
        Assert.IsType<IntentRoutingPipeline>(pipeline);
        Assert.IsType<TaskContractFactory>(contractFactory);
        Assert.NotSame(firstEvidenceCollector, secondEvidenceCollector);
        Assert.IsType<DesktopIntentRoutingAdapter>(desktopRoutingAdapter);
        Assert.IsType<CompletionEvaluator>(completionEvaluator);
        Assert.IsType<DeterministicActionDispatcher>(actionDispatcher);
        Assert.IsType<FinalAnswerConsistencyGuard>(finalAnswerGuard);
        Assert.IsType<RepairCoordinator>(repairCoordinator);
        Assert.IsType<ModelToolLoop>(modelToolLoop);
        Assert.IsType<DesktopTaskContractCompletionAdapter>(completionAdapter);
        Assert.IsType<DesktopProviderSessionFactory>(providerSessionFactory);
    }
}
