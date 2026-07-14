using System.Security.Cryptography;
using System.Text;
using AgentQ.Runtime.Intent;

namespace AgentQ.Runtime.Contracts;

public sealed record RuntimeTaskContractRequest(
    string WorkspaceId,
    AgentTurnIntent Intent,
    string Goal,
    IReadOnlyList<string> Targets,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> ExpectedMutations,
    IReadOnlyList<string> VerificationRequirements,
    IReadOnlyList<string> CompletionConditions,
    DateTimeOffset ExpiresAt,
    string? ExternalPlanId = null,
    string? ExternalPlanHash = null,
    string Version = "runtime-task-contract-v1");

public sealed record RuntimeTaskContract(
    string ContractId,
    string WorkspaceId,
    AgentTurnIntent Intent,
    string Goal,
    IReadOnlyList<string> Targets,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> ExpectedMutations,
    IReadOnlyList<string> VerificationRequirements,
    IReadOnlyList<string> CompletionConditions,
    DateTimeOffset ExpiresAt,
    string Version,
    string Hash,
    string? ExternalPlanId,
    string? ExternalPlanHash);

public interface ITaskContractFactory
{
    RuntimeTaskContract Create(RuntimeTaskContractRequest request, string? contractId = null);
}

public sealed class TaskContractFactory : ITaskContractFactory
{
    public RuntimeTaskContract Create(RuntimeTaskContractRequest request, string? contractId = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        Require(request.WorkspaceId, nameof(request.WorkspaceId));
        Require(request.Goal, nameof(request.Goal));
        Require(request.Version, nameof(request.Version));

        var id = string.IsNullOrWhiteSpace(contractId) ? Guid.NewGuid().ToString("N") : contractId;
        var canonical = string.Join("\n", [
            request.Version, request.WorkspaceId, request.Intent.ToString(), request.Goal, request.ExpiresAt.ToUniversalTime().ToString("O"),
            request.ExternalPlanId ?? string.Empty, request.ExternalPlanHash ?? string.Empty,
            Join(request.Targets), Join(request.Capabilities), Join(request.ExpectedMutations),
            Join(request.VerificationRequirements), Join(request.CompletionConditions)]);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();

        return new RuntimeTaskContract(id, request.WorkspaceId, request.Intent, request.Goal,
            request.Targets.ToArray(), request.Capabilities.ToArray(), request.ExpectedMutations.ToArray(),
            request.VerificationRequirements.ToArray(), request.CompletionConditions.ToArray(), request.ExpiresAt,
            request.Version, hash, request.ExternalPlanId, request.ExternalPlanHash);
    }

    private static string Join(IReadOnlyList<string> values) => string.Join("\u001f", values.Select(value => value.Trim()));

    private static void Require(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A non-empty value is required.", parameterName);
    }
}
