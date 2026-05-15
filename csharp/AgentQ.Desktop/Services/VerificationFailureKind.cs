namespace AgentQ.Desktop.Services;

public enum VerificationFailureKind
{
    Unknown,
    CompileError,
    TestFailure,
    Timeout,
    PermissionBlocked,
    MissingDependency,
    EnvironmentIssue,
    CommandNotAllowed
}
