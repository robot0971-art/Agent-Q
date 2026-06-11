namespace AgentQ.Desktop.Services;

public static class VerificationCommandPolicy
{
    private static readonly string[] AllowedCommands =
    [
        "cmd /c build.cmd",
        "cmd /c test.cmd",
        "docker compose config",
        "dotnet build",
        "dotnet test",
        "npm install",
        "npm run build",
        "npm run lint",
        "npm run test",
        "npm run test:e2e",
        "npm test",
        "npx playwright test",
        "pnpm build",
        "pnpm exec playwright test",
        "pnpm run test:e2e",
        "pnpm test",
        "yarn playwright test",
        "yarn build",
        "yarn test:e2e",
        "yarn test",
        "bun test:e2e",
        "bunx playwright test",
        "bash scripts/app.sh",
        "cargo fmt --check",
        "cargo build",
        "cargo test",
        "cmake -S . -B build",
        "cmake --build build",
        "composer test",
        "ctest --test-dir build",
        "go test ./...",
        "gradle test",
        "./gradlew test",
        "mvn test",
        "python -m pytest",
        "python -m streamlit run app.py --server.headless true",
        "pwsh -File scripts/app.ps1 -DryRun",
        "pytest",
        "Rscript -e \"testthat::test_dir('tests')\"",
        "swift test",
        "New-Item -ItemType Directory -Force .agentq-verify\\desktop-out | Out-Null; dotnet build csharp\\AgentQ.Desktop\\AgentQ.Desktop.csproj --no-restore -p:OutDir=.agentq-verify\\desktop-out\\"
    ];

    public static bool IsAllowed(string? command, IEnumerable<string>? projectAllowedCommands = null)
    {
        return !string.IsNullOrWhiteSpace(command) &&
               (AllowedCommands.Any(allowed => string.Equals(allowed, command, StringComparison.Ordinal)) ||
                IsAllowedFocusedDotnetTest(command) ||
                 IsAllowedDotnetTestTarget(command) ||
                 IsAllowedDirectoryScopedCommand(command) ||
                 IsAllowedProjectConfiguredCommand(command, projectAllowedCommands));
    }

    private static bool IsAllowedProjectConfiguredCommand(
        string command,
        IEnumerable<string>? projectAllowedCommands)
    {
        if (projectAllowedCommands?.Any(allowed => string.Equals(allowed, command, StringComparison.Ordinal)) != true)
        {
            return false;
        }

        if (ContainsUnsafeShellSyntax(command) || ContainsDestructiveToken(command))
        {
            return false;
        }

        return command.StartsWith("npm run ", StringComparison.Ordinal) ||
               command.StartsWith("pnpm run ", StringComparison.Ordinal) ||
               command.StartsWith("yarn ", StringComparison.Ordinal) ||
               command.StartsWith("bun run ", StringComparison.Ordinal) ||
               command.StartsWith("dotnet test ", StringComparison.Ordinal) ||
               command.StartsWith("dotnet build ", StringComparison.Ordinal) ||
               command.StartsWith("python -m pytest ", StringComparison.Ordinal) ||
               command.StartsWith("pytest ", StringComparison.Ordinal) ||
               command.StartsWith("go test ", StringComparison.Ordinal) ||
               command.StartsWith("cargo test ", StringComparison.Ordinal) ||
               command.StartsWith("mvn ", StringComparison.Ordinal) ||
               command.StartsWith("gradle ", StringComparison.Ordinal) ||
               command.StartsWith("./gradlew ", StringComparison.Ordinal);
    }

    private static bool ContainsUnsafeShellSyntax(string command)
    {
        return command.IndexOfAny([';', '|', '<', '>', '`']) >= 0 ||
               command.Contains("&&", StringComparison.Ordinal) ||
               command.Contains("||", StringComparison.Ordinal);
    }

    private static bool ContainsDestructiveToken(string command)
    {
        var lower = command.ToLowerInvariant();
        return lower.Contains("remove-item", StringComparison.Ordinal) ||
               lower.StartsWith("rm ", StringComparison.Ordinal) ||
               lower.Contains(" rm ", StringComparison.Ordinal) ||
               lower.StartsWith("del ", StringComparison.Ordinal) ||
               lower.Contains(" del ", StringComparison.Ordinal) ||
               lower.StartsWith("rmdir ", StringComparison.Ordinal) ||
               lower.Contains(" rmdir ", StringComparison.Ordinal) ||
               lower.StartsWith("rd ", StringComparison.Ordinal) ||
               lower.Contains(" rd ", StringComparison.Ordinal) ||
               lower.StartsWith("erase ", StringComparison.Ordinal) ||
               lower.Contains("erase ", StringComparison.Ordinal) ||
               lower.Contains("git reset", StringComparison.Ordinal) ||
               lower.Contains("git clean", StringComparison.Ordinal) ||
               lower.Contains("git restore", StringComparison.Ordinal) ||
               lower.Contains("shutdown", StringComparison.Ordinal) ||
               lower.Contains("reboot", StringComparison.Ordinal) ||
               lower.Contains("format ", StringComparison.Ordinal);
    }

    private static bool IsAllowedFocusedDotnetTest(string command)
    {
        const string prefix = "dotnet test csharp\\AgentQ.Tests\\AgentQ.Tests.csproj --filter FullyQualifiedName~";
        if (!command.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var filter = command[prefix.Length..];
        return filter.Length is > 0 and <= 120 &&
               filter.All(character => char.IsLetterOrDigit(character) || character is '_' or '.');
    }

    private static bool IsAllowedDotnetTestTarget(string command)
    {
        const string prefix = "dotnet test ";
        if (!command.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var target = command[prefix.Length..];
        return target.Length is > 0 and <= 180 &&
               (target.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) &&
               target.All(character => char.IsLetterOrDigit(character) ||
                                       character is '_' or '-' or '.' or '\\' or '/');
    }

    private static bool IsAllowedDirectoryScopedCommand(string command)
    {
        const string prefix = "cmd /c cd ";
        const string quotedPrefix = "cmd /c cd /d \"";
        if (!command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var separatorIndex = command.IndexOf(" && ", StringComparison.Ordinal);
        if (separatorIndex <= prefix.Length)
        {
            return false;
        }

        string directory;
        if (command.StartsWith(quotedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var closingQuoteIndex = command.IndexOf("\" && ", quotedPrefix.Length, StringComparison.Ordinal);
            if (closingQuoteIndex <= quotedPrefix.Length ||
                closingQuoteIndex + 1 != separatorIndex)
            {
                return false;
            }

            directory = command[quotedPrefix.Length..closingQuoteIndex];
        }
        else
        {
            directory = command[prefix.Length..separatorIndex];
        }

        if (string.IsNullOrWhiteSpace(directory) ||
            directory.Contains("..", StringComparison.Ordinal) ||
            directory.IndexOfAny(['&', '|', ';', '<', '>', '"', '\'']) >= 0)
        {
            return false;
        }

        if (!directory.All(character =>
                char.IsLetterOrDigit(character) ||
                char.IsWhiteSpace(character) ||
                character is '_' or '-' or '.' or '/' or '\\'))
        {
            return false;
        }

        var nestedCommand = command[(separatorIndex + " && ".Length)..];
        return AllowedCommands.Any(allowed => string.Equals(allowed, nestedCommand, StringComparison.Ordinal));
    }
}
