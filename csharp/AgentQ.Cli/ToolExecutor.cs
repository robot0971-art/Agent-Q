using System.Text.Json;
using AgentQ.Api;
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
            var inputJson = FormatToolInputJson(input);
            if (!JsonArgumentParser.TryParseInput(input, out var parsedInput, out var parseError))
            {
                var message = $"Invalid tool input for {toolName}: {parseError}";
                callbacks.OnToolError?.Invoke(toolName, message);
                toolResults.Add(ChatContent.CreateToolResult(toolId, message, true));
                continue;
            }

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
                var result = await tool.ExecuteAsync(parsedInput, ct);
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

    private static string FormatToolInputJson(object? input)
    {
        if (input is string raw)
        {
            try
            {
                using var document = JsonDocument.Parse(raw);
                var root = NormalizeRootElement(document.RootElement);
                return JsonSerializer.Serialize(root, AgentQJsonOptions.Indented);
            }
            catch
            {
                return raw;
            }
        }

        if (input is JsonElement json)
        {
            return JsonSerializer.Serialize(json, AgentQJsonOptions.Indented);
        }

        return JsonSerializer.Serialize(input, AgentQJsonOptions.Indented);
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
            return innerDocument.RootElement.ValueKind == JsonValueKind.Object
                ? innerDocument.RootElement.Clone()
                : root;
        }
        catch (JsonException)
        {
            return root;
        }
    }
}
