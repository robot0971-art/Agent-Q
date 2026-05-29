namespace AgentQ.Desktop.Services;

public interface IVerificationArtifactCollector
{
    IReadOnlyList<VerificationArtifact> Collect(
        AgentVerificationPlan plan,
        VerificationRunResult result,
        string workspaceRoot);
}
