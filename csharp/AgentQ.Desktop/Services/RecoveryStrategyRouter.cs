using System;
using System.Collections.Generic;
using System.Linq;

namespace AgentQ.Desktop.Services;

public enum RecoveryStrategyKind
{
    PatchCompileError,      
    FixTestAssertion,       
    ResolveTypeChain,       
    FixMissingDependency,   
    AdjustEnvironment,      
    FallbackToManual        
}

public sealed class RecoveryStrategy
{
    public RecoveryStrategyKind Kind { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public List<string> AdditionalContextFiles { get; set; } = [];
    public string? FocusedVerificationCommand { get; set; }
}

public sealed class RecoveryStrategyRouter
{
    public RecoveryStrategy SelectStrategy(
        VerificationFailureAnalysis failure,
        TaskStepResult previousResult,
        int attemptNumber)
    {
        if (attemptNumber >= 3)
        {
            return new RecoveryStrategy
            {
                Kind = RecoveryStrategyKind.FallbackToManual,
                Prompt = "The previous attempts failed repeatedly. Stop running tools and report the specific block blockages to the user, asking for instruction."
            };
        }

        switch (failure.Kind)
        {
            case VerificationFailureKind.CompileError:
                var files = failure.ErrorLocations.Select(loc => loc.FilePath).Distinct().ToList();
                return new RecoveryStrategy
                {
                    Kind = RecoveryStrategyKind.PatchCompileError,
                    Prompt = $"A compilation error occurred. Error Code: {string.Join(", ", failure.ErrorLocations.Select(l => l.ErrorCode).Distinct())}. Please read the source files around the error location and patch them carefully.",
                    AdditionalContextFiles = files
                };

            case VerificationFailureKind.TestFailure:
                return new RecoveryStrategy
                {
                    Kind = RecoveryStrategyKind.FixTestAssertion,
                    Prompt = "A test verification failed. Please inspect the test failure output, find where the assertion failed in the code, check the logic, and fix it."
                };

            case VerificationFailureKind.MissingDependency:
                return new RecoveryStrategy
                {
                    Kind = RecoveryStrategyKind.FixMissingDependency,
                    Prompt = "A command or dependency is missing. Restore nuget packages or check if references/dependencies are correctly configured."
                };

            case VerificationFailureKind.EnvironmentIssue:
                return new RecoveryStrategy
                {
                    Kind = RecoveryStrategyKind.AdjustEnvironment,
                    Prompt = "An SDK or environment issue was detected. Adjust project configuration files or setup environment properly."
                };

            default:
                return new RecoveryStrategy
                {
                    Kind = RecoveryStrategyKind.ResolveTypeChain,
                    Prompt = "The verification failed with an unclassified error. Review the previous tool outputs, locate the error files, and fix the mismatch."
                };
        }
    }
}
