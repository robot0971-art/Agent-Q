using System.Text.Json;
using AgentQ.Tools;
using Spectre.Console;
using Spectre.Console.Json;

namespace AgentQ.Cli;

public class ConsolePermissionEnforcer : IPermissionEnforcer
{
    private readonly HashSet<string> _sessionAllowedTools = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> SessionAllowedTools =>
        _sessionAllowedTools
            .Where(IsSessionReusableTool)
            .OrderBy(tool => tool, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public void ClearSessionAllowedTools()
    {
        _sessionAllowedTools.Clear();
    }

    public Task<bool> RequestPermissionAsync(string toolName, string description, string inputJson)
    {
        var reusableApprovalAllowed = IsSessionReusableTool(toolName);
        if (reusableApprovalAllowed && _sessionAllowedTools.Contains(toolName))
        {
            AnsiConsole.MarkupLine($"[dim]Already allowed for this session:[/] [cyan]{toolName}[/]");
            return Task.FromResult(true);
        }

        AnsiConsole.Write(new Rule { Title = "Permission", Style = Style.Parse("yellow") });
        AnsiConsole.MarkupLine($"[bold yellow]Tool:[/] [cyan]{toolName}[/]");
        AnsiConsole.MarkupLine($"[bold yellow]Description:[/] {description.EscapeMarkup()}");

        foreach (var summaryLine in BuildSummary(toolName, inputJson))
        {
            AnsiConsole.MarkupLine(summaryLine);
        }

        AnsiConsole.MarkupLine("[bold yellow]Arguments:[/]");
        AnsiConsole.Write(new JsonText(inputJson));
        AnsiConsole.WriteLine();

        var allowOnce = "Allow once";
        var allowSession = $"Allow {toolName} for this session";
        var deny = "Deny";
        var choices = reusableApprovalAllowed
            ? new[] { allowOnce, allowSession, deny }
            : [allowOnce, deny];
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Allow execution?")
                .AddChoices(choices));
        AnsiConsole.Write(new Rule { Style = Style.Parse("yellow") });

        if (reusableApprovalAllowed && choice == allowSession)
        {
            _sessionAllowedTools.Add(toolName);
            return Task.FromResult(true);
        }

        return Task.FromResult(choice == allowOnce);
    }

    public static bool IsSessionReusableTool(string toolName) =>
        string.Equals(toolName, "web_search", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> BuildSummary(string toolName, string inputJson)
    {
        var summary = new List<string>();

        try
        {
            using var document = JsonDocument.Parse(inputJson);
            var root = NormalizeRootElement(document.RootElement);
            if (root.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var riskSummary = GetRiskSummary(toolName);
            if (!string.IsNullOrWhiteSpace(riskSummary))
            {
                summary.Add($"[bold yellow]Risk:[/] {riskSummary}");
            }

            if (toolName == "bash" && root.TryGetProperty("command", out var commandProperty))
            {
                var command = commandProperty.GetString() ?? string.Empty;
                summary.Add($"[bold yellow]Command:[/] [white]{Markup.Escape(Shorten(command, 120))}[/]");

                if (root.TryGetProperty("timeout", out var timeoutProperty) && timeoutProperty.TryGetInt32(out var timeout))
                {
                    summary.Add($"[bold yellow]Timeout:[/] {timeout}ms");
                }
            }

            if (root.TryGetProperty("path", out var pathProperty))
            {
                var path = pathProperty.GetString() ?? string.Empty;
                summary.Add($"[bold yellow]Path:[/] [white]{Markup.Escape(path)}[/]");
            }
        }
        catch (JsonException)
        {
            return [];
        }
        catch (InvalidOperationException)
        {
            return [];
        }

        return summary;
    }

    private static string? GetRiskSummary(string toolName)
    {
        return toolName.ToLowerInvariant() switch
        {
            "bash" => "shell command execution",
            "write_file" => "project file write or overwrite",
            "edit_file" => "project file edit",
            "create_directory" => "project directory creation",
            "delete_path" => "project file or directory deletion",
            "web_search" => "network access",
            _ => null
        };
    }

    private static JsonElement NormalizeRootElement(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.String)
        {
            return root;
        }

        var raw = root.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return root;
        }

        try
        {
            using var innerDocument = JsonDocument.Parse(raw);
            return innerDocument.RootElement.Clone();
        }
        catch (JsonException)
        {
            return root;
        }
    }

    private static string Shorten(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "...";
    }
}
