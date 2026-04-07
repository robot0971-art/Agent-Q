using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Json;
using AgentQ.Tools;

namespace AgentQ.Cli;

/// <summary>
/// 콘솔 권한 인포서
/// </summary>
public class ConsolePermissionEnforcer : IPermissionEnforcer
{
    /// <summary>
    /// 권한 요청
    /// </summary>
    /// <param name="toolName">도구 이름</param>
    /// <param name="description">설명</param>
    /// <param name="inputJson">입력 JSON</param>
    /// <returns>권한 승인 여부</returns>
    public Task<bool> RequestPermissionAsync(string toolName, string description, string inputJson)
    {
        AnsiConsole.Write(new Rule { Title = "Permission Required", Style = Style.Parse("yellow") });
        AnsiConsole.MarkupLine($"[bold yellow]Tool:[/] [cyan]{toolName}[/]");
        AnsiConsole.MarkupLine($"[bold yellow]Description:[/] {description}");

        foreach (var summaryLine in BuildSummary(toolName, inputJson))
        {
            AnsiConsole.MarkupLine(summaryLine);
        }

        AnsiConsole.MarkupLine("[bold yellow]Arguments:[/]");
        AnsiConsole.Write(new JsonText(inputJson));
        AnsiConsole.WriteLine();

        var confirm = AnsiConsole.Confirm("Allow execution?", defaultValue: true);
        AnsiConsole.Write(new Rule { Style = Style.Parse("yellow") });

        return Task.FromResult(confirm);
    }

    private static IEnumerable<string> BuildSummary(string toolName, string inputJson)
    {
        var summary = new List<string>();

        try
        {
            using var document = JsonDocument.Parse(inputJson);
            var root = document.RootElement;

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

        return summary;
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
