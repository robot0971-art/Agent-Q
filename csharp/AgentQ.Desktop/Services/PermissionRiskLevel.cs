namespace AgentQ.Desktop.Services;

public enum PermissionRiskLevel
{
    SafeRead,
    ProjectWrite,
    VerificationCommand,
    ShellCommand,
    Network,
    GitWrite,
    ExternalWrite,
    Destructive
}
