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
    private readonly HashSet<string> _sessionAllowedTools = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> SessionAllowedTools =>
        _sessionAllowedTools.OrderBy(tool => tool, StringComparer.OrdinalIgnoreCase).ToArray();

    public void ClearSessionAllowedTools()
    {
        _sessionAllowedTools.Clear();
    }

    /// <summary>
    /// 도구 실행 전 사용자 허용 여부를 확인합니다.
    /// </summary>
    /// <param name="toolName">도구 이름</param>
    /// <param name="description">도구 설명</param>
    /// <param name="inputJson">도구 입력 JSON</param>
    /// <returns>허용 여부</returns>
    public Task<bool> RequestPermissionAsync(string toolName, string description, string inputJson)
    {
        if (_sessionAllowedTools.Contains(toolName))
        {
            AnsiConsole.MarkupLine($"[dim]이번 세션에서 이미 허용된 도구:[/] [cyan]{toolName}[/]");
            return Task.FromResult(true);
        }

        AnsiConsole.Write(new Rule { Title = "권한 확인", Style = Style.Parse("yellow") });
        AnsiConsole.MarkupLine($"[bold yellow]도구:[/] [cyan]{toolName}[/]");
        AnsiConsole.MarkupLine($"[bold yellow]설명:[/] {description}");

        foreach (var summaryLine in BuildSummary(toolName, inputJson))
        {
            AnsiConsole.MarkupLine(summaryLine);
        }

        AnsiConsole.MarkupLine("[bold yellow]인수:[/]");
        AnsiConsole.Write(new JsonText(inputJson));
        AnsiConsole.WriteLine();

        var allowOnce = "예, 이번만 허용";
        var allowSession = $"예, 이번 세션 동안 {toolName} 항상 허용";
        var deny = "아니오";
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("실행을 허용할까요?")
                .AddChoices(allowOnce, allowSession, deny));
        AnsiConsole.Write(new Rule { Style = Style.Parse("yellow") });

        if (choice == allowSession)
        {
            _sessionAllowedTools.Add(toolName);
            return Task.FromResult(true);
        }

        return Task.FromResult(choice == allowOnce);
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
                summary.Add($"[bold yellow]명령:[/] [white]{Markup.Escape(Shorten(command, 120))}[/]");

                if (root.TryGetProperty("timeout", out var timeoutProperty) && timeoutProperty.TryGetInt32(out var timeout))
                {
                    summary.Add($"[bold yellow]시간 제한:[/] {timeout}ms");
                }
            }

            if (root.TryGetProperty("path", out var pathProperty))
            {
                var path = pathProperty.GetString() ?? string.Empty;
                summary.Add($"[bold yellow]경로:[/] [white]{Markup.Escape(path)}[/]");
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
