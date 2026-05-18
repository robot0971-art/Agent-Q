using AgentQ.Core.Providers;
using AgentQ.Tools;

namespace AgentQ.Cli;

public sealed class CliPermissionEnforcerFactory
{
    public CliPermissionEnforcers Create(AutomationInvocation invocation, ProviderConfiguration config)
    {
        if (invocation.IsNonInteractive)
        {
            return new CliPermissionEnforcers(
                new NonInteractivePermissionEnforcer(config.AllowToolsWithoutPrompt, config.AllowedToolNames, config.DeniedToolNames),
                ConsoleEnforcer: null);
        }

        var consoleEnforcer = new ConsolePermissionEnforcer();
        return new CliPermissionEnforcers(consoleEnforcer, consoleEnforcer);
    }
}

public sealed record CliPermissionEnforcers(IPermissionEnforcer Enforcer, ConsolePermissionEnforcer? ConsoleEnforcer);
