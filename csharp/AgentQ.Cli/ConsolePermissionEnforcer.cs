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
        AnsiConsole.Write(new Rule { Title = $"Permission Required", Style = Style.Parse("yellow") });
        AnsiConsole.MarkupLine($"[bold yellow]Tool:[/] [cyan]{toolName}[/]");
        AnsiConsole.MarkupLine($"[bold yellow]Description:[/] {description}");
        AnsiConsole.MarkupLine($"[bold yellow]Arguments:[/]");
        AnsiConsole.Write(new JsonText(inputJson));
        AnsiConsole.WriteLine();

        var confirm = AnsiConsole.Confirm("Allow execution?", defaultValue: true);
        AnsiConsole.Write(new Rule { Style = Style.Parse("yellow") });
        
        return Task.FromResult(confirm);
    }
}

