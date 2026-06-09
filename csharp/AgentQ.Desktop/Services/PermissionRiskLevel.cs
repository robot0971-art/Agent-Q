namespace AgentQ.Desktop.Services;

public enum PermissionRiskLevel
{
    SafeRead,
    LowRiskProjectWrite,
    ProjectWrite,
    VerificationCommand,
    ShellCommand,
    Network,
    GitWrite,
    ExternalWrite,
    Destructive
}
