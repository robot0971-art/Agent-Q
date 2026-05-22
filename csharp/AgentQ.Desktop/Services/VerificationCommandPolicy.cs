namespace AgentQ.Desktop.Services;

public static class VerificationCommandPolicy
{
    private static readonly string[] AllowedCommands =
    [
        "cmd /c build.cmd",
        "cmd /c test.cmd",
        "docker compose config",
        "npm run build",
        "npm test",
        "pnpm build",
        "pnpm test",
        "yarn build",
        "yarn test",
        "python -m pytest",
        "pytest",
        "New-Item -ItemType Directory -Force .agentq-verify\\desktop-out | Out-Null; dotnet build csharp\\AgentQ.Desktop\\AgentQ.Desktop.csproj --no-restore -p:OutDir=.agentq-verify\\desktop-out\\"
    ];

    public static bool IsAllowed(string? command, IEnumerable<string>? projectAllowedCommands = null)
    {
        return !string.IsNullOrWhiteSpace(command) &&
               (AllowedCommands.Any(allowed => string.Equals(allowed, command, StringComparison.Ordinal)) ||
                IsAllowedDirectoryScopedCommand(command) ||
                projectAllowedCommands?.Any(allowed => string.Equals(allowed, command, StringComparison.Ordinal)) == true);
    }

    private static bool IsAllowedDirectoryScopedCommand(string command)
    {
        const string prefix = "cmd /c cd ";
        if (!command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var separatorIndex = command.IndexOf(" && ", StringComparison.Ordinal);
        if (separatorIndex <= prefix.Length)
        {
            return false;
        }

        var directory = command[prefix.Length..separatorIndex];
        if (string.IsNullOrWhiteSpace(directory) ||
            directory.Contains("..", StringComparison.Ordinal) ||
            directory.IndexOfAny(['&', '|', ';', '<', '>', '"', '\'']) >= 0)
        {
            return false;
        }

        var nestedCommand = command[(separatorIndex + " && ".Length)..];
        return AllowedCommands.Any(allowed => string.Equals(allowed, nestedCommand, StringComparison.Ordinal));
    }
}
