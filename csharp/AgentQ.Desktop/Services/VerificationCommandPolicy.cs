namespace AgentQ.Desktop.Services;

public static class VerificationCommandPolicy
{
    private static readonly string[] AllowedCommands =
    [
        "cmd /c build.cmd",
        "cmd /c test.cmd",
        "New-Item -ItemType Directory -Force .agentq-verify\\desktop-out | Out-Null; dotnet build csharp\\AgentQ.Desktop\\AgentQ.Desktop.csproj --no-restore -p:OutDir=.agentq-verify\\desktop-out\\"
    ];

    public static bool IsAllowed(string? command, IEnumerable<string>? projectAllowedCommands = null)
    {
        return !string.IsNullOrWhiteSpace(command) &&
               (AllowedCommands.Any(allowed => string.Equals(allowed, command, StringComparison.Ordinal)) ||
                projectAllowedCommands?.Any(allowed => string.Equals(allowed, command, StringComparison.Ordinal)) == true);
    }
}
