using System.Text.Json;
using AgentQ.Core.Models;
using AgentQ.Tools;

namespace AgentQ.Cli;

internal sealed class ToolExecutor(
    ToolRegistry registry,
    IPermissionEnforcer enforcer,
    ToolExecutionCallbacks callbacks)
{
    public async Task<List<ChatContent>> ExecuteAsync(
        IEnumerable<ChatContent> toolUses,
        CancellationToken ct = default)
    {
        var toolResults = new List<ChatContent>();

        foreach (var toolUse in toolUses)
        {
            var toolName = toolUse.ToolName!;
            var toolId = toolUse.ToolId!;
            var input = toolUse.ToolInput;
            var inputJson = JsonSerializer.Serialize(input, new JsonSerializerOptions { WriteIndented = true });

            var tool = registry.Get(toolName);
            if (tool == null)
            {
                toolResults.Add(ChatContent.CreateToolResult(toolId, $"Tool not found: {toolName}", true));
                continue;
            }

            if (tool.RequiresPermission)
            {
                if (!await enforcer.RequestPermissionAsync(toolName, tool.Description, inputJson))
                {
                    callbacks.OnPermissionDenied?.Invoke(toolName);
                    toolResults.Add(ChatContent.CreateToolResult(toolId, "Permission denied by user", true));
                    continue;
                }
            }

            callbacks.OnToolExecution?.Invoke(toolName);

            try
            {
                var result = await tool.ExecuteAsync(JsonArgumentParser.ParseInput(input), ct);
                if (result.IsError)
                {
                    callbacks.OnToolError?.Invoke(toolName, result.Content);
                }
                else
                {
                    callbacks.OnToolOutput?.Invoke(toolName, result.Content);
                }

                toolResults.Add(ChatContent.CreateToolResult(toolId, result.Content, result.IsError));
            }
            catch (Exception ex)
            {
                var message = $"Error: {ex.Message}";
                callbacks.OnToolError?.Invoke(toolName, message);
                toolResults.Add(ChatContent.CreateToolResult(toolId, message, true));
            }
        }

        return toolResults;
    }
}
