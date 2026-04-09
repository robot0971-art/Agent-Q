using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Json;
using AgentQ.Tools;

namespace AgentQ.Cli;

/// <summary>
/// 콘솔에서 도구 실행 권한을 확인하는 인포서입니다.
/// </summary>
public class ConsolePermissionEnforcer : IPermissionEnforcer
{
    /// <summary>
    /// 도구 실행 전 사용자 허용 여부를 확인합니다.
    /// </summary>
    /// <param name="toolName">도구 이름</param>
    /// <param name="description">도구 설명</param>
    /// <param name="inputJson">도구 입력 JSON</param>
    /// <returns>허용 여부</returns>
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
            // 일부 provider는 인수를 객체 대신 JSON 문자열로 감싸서 보내므로 먼저 정규화합니다.
            var root = NormalizeRootElement(document.RootElement);
            if (root.ValueKind != JsonValueKind.Object)
            {
                return [];
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
            // 문자열 안에 다시 JSON 객체가 들어온 경우 요약 로직이 읽을 수 있는 형태로 바꿉니다.
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
