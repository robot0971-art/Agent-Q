using System.Collections.Generic;
using System.Linq;
using AgentQ.Desktop.Services;
using Xunit;

namespace AgentQ.Tests;

public sealed class RecoveryStrategyTests
{
    [Fact]
    public void Classifier_ShouldExtractErrorLocations_FromCSharpBuildOutput()
    {
        var classifier = new VerificationFailureClassifier();
        var plan = new AgentVerificationPlan { Title = "dotnet build", Command = "dotnet build" };
        var buildOutput = """
Microsoft (R) Build Engine version 17.0.0
C:\Projects\MyApp\Program.cs(12,5): error CS0103: The name 'myVar' does not exist in the current context.
C:\Projects\MyApp\Services\MyService.cs(45,20): error CS0246: The type or namespace name 'MyType' could not be found.
Build FAILED.
""";
        var runResult = new VerificationRunResult
        {
            StandardOutput = buildOutput,
            StandardError = string.Empty,
            ExitCode = 1
        };

        var analysis = classifier.Analyze(plan, runResult);

        Assert.Equal(VerificationFailureKind.CompileError, analysis.Kind);
        Assert.Equal(2, analysis.ErrorLocations.Count);

        var first = analysis.ErrorLocations[0];
        Assert.Contains("Program.cs", first.FilePath);
        Assert.Equal(12, first.Line);
        Assert.Equal(5, first.Column);
        Assert.Equal("CS0103", first.ErrorCode);
        Assert.Equal("The name 'myVar' does not exist in the current context.", first.Message);

        var second = analysis.ErrorLocations[1];
        Assert.Contains("MyService.cs", second.FilePath);
        Assert.Equal(45, second.Line);
        Assert.Equal(20, second.Column);
        Assert.Equal("CS0246", second.ErrorCode);
        Assert.Equal("The type or namespace name 'MyType' could not be found.", second.Message);
    }

    [Fact]
    public void RecoveryRouter_ShouldSelectCorrectStrategies()
    {
        var router = new RecoveryStrategyRouter();
        var dummyResult = new TaskStepResult();

        // 1. Compile Error Strategy
        var compileFailure = new VerificationFailureAnalysis
        {
            Kind = VerificationFailureKind.CompileError,
            ErrorLocations = new List<ErrorLocation>
            {
                new() { FilePath = "Program.cs", Line = 10, Column = 5, ErrorCode = "CS0103" }
            }
        };
        var strategy = router.SelectStrategy(compileFailure, dummyResult, attemptNumber: 1);
        Assert.Equal(RecoveryStrategyKind.PatchCompileError, strategy.Kind);
        Assert.Single(strategy.AdditionalContextFiles);
        Assert.Equal("Program.cs", strategy.AdditionalContextFiles[0]);
        Assert.Contains("CS0103", strategy.Prompt);

        // 2. Test Failure Strategy
        var testFailure = new VerificationFailureAnalysis { Kind = VerificationFailureKind.TestFailure };
        var testStrategy = router.SelectStrategy(testFailure, dummyResult, attemptNumber: 1);
        Assert.Equal(RecoveryStrategyKind.FixTestAssertion, testStrategy.Kind);

        // 3. Max Attempts Fallback Strategy
        var maxAttemptsStrategy = router.SelectStrategy(compileFailure, dummyResult, attemptNumber: 3);
        Assert.Equal(RecoveryStrategyKind.FallbackToManual, maxAttemptsStrategy.Kind);
    }
}
