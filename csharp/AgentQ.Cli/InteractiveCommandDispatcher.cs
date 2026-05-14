namespace AgentQ.Cli;

public sealed class InteractiveCommandDispatcher(InteractiveCommandCallbacks callbacks)
{
    public async Task<bool> DispatchAsync(string input)
    {
        var parts = input.Split(' ', 2, StringSplitOptions.TrimEntries);
        var command = parts[0].ToLowerInvariant();
        var argument = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        switch (command)
        {
            case "/exit":
            case "/quit":
                return false;

            case "/clear":
                callbacks.Clear();
                break;

            case "/help":
                callbacks.Help();
                break;

            case "/setup":
                await callbacks.Setup();
                break;

            case "/history":
                callbacks.History();
                break;

            case "/compact":
                await callbacks.Compact();
                break;

            case "/tools":
                callbacks.Tools();
                break;

            case "/permissions":
                callbacks.Permissions(argument);
                break;

            case "/status":
                callbacks.Status();
                break;

            case "/run":
                await callbacks.RunTool(argument);
                break;

            case "/provider":
                await callbacks.Provider(argument);
                break;

            case "/model":
                callbacks.Model(argument);
                break;

            case "/base-url":
                callbacks.BaseUrl(argument);
                break;

            case "/api-key":
                callbacks.ApiKey(argument);
                break;

            case "/timeout":
                callbacks.Timeout(argument);
                break;

            case "/max-tokens":
                callbacks.MaxTokens(argument);
                break;

            case "/config":
                await callbacks.Config(argument);
                break;

            case "/save":
                await callbacks.Save(argument);
                break;

            case "/load":
                await callbacks.Load(argument);
                break;

            default:
                callbacks.Unknown(command);
                break;
        }

        return true;
    }
}
