using System.Net;
using System.Text;
using System.Text.Json;
using AgentQ.Api;

namespace AgentQ.MockService;

/// <summary>
/// Mock Anthropic 서비스
/// </summary>
public class MockAnthropicService
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;
    private readonly List<CapturedRequest> _capturedRequests = new();
    private readonly Lock _requestsLock = new();

    /// <summary>
    /// 기본 모델
    /// </summary>
    public const string DefaultModel = "claude-sonnet-4-6";

    /// <summary>
    /// 기본 URL
    /// </summary>
    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>
    /// 서비스 시작
    /// </summary>
    /// <param name="prefix">URL 프리픽스</param>
    public async Task StartAsync(string prefix)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add(prefix);
        _listener.Start();

        BaseUrl = prefix.TrimEnd('/');
        _cts = new CancellationTokenSource();
        _listenerTask = Task.Run(() => ListenLoop(_cts.Token));

        await Task.CompletedTask;
    }

    /// <summary>
    /// 서비스 중지
    /// </summary>
    public async Task StopAsync()
    {
        _cts?.Cancel();
        _listener?.Stop();

        if (_listenerTask != null)
        {
            try
            {
                await _listenerTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _listener?.Close();
    }

    /// <summary>
    /// 캡처된 요청 목록 가져오기
    /// </summary>
    public List<CapturedRequest> GetCapturedRequests()
    {
        lock (_requestsLock)
        {
            return _capturedRequests.ToList();
        }
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(context), ct);
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private async Task HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            string body;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                body = await reader.ReadToEndAsync();
            }

            var messageRequest = JsonSerializer.Deserialize<MessageRequest>(body, JsonOptions);
            if (messageRequest == null)
            {
                response.StatusCode = 400;
                await WriteResponse(response, "Invalid request body");
                return;
            }

            var scenario = DetectScenario(messageRequest);
            if (scenario == null)
            {
                response.StatusCode = 400;
                await WriteResponse(response, "Missing parity scenario");
                return;
            }

            var capturedRequest = new CapturedRequest
            {
                Method = request.HttpMethod,
                Path = request.Url?.PathAndQuery ?? "/",
                Headers = request.Headers.AllKeys
                    .Where(k => k != null)
                    .ToDictionary(k => k!.ToLowerInvariant(), k => request.Headers[k] ?? string.Empty),
                Scenario = scenario.Value.GetName(),
                Stream = messageRequest.Stream,
                RawBody = body
            };

            lock (_requestsLock)
            {
                _capturedRequests.Add(capturedRequest);
            }

            if (messageRequest.Stream)
            {
                response.ContentType = "text/event-stream";
                response.Headers.Add("x-request-id", ScenarioParser.GetRequestId(scenario.Value));
                response.StatusCode = 200;

                var streamBody = BuildStreamBody(messageRequest, scenario.Value);
                var bytes = Encoding.UTF8.GetBytes(streamBody);
                await response.OutputStream.WriteAsync(bytes);
            }
            else
            {
                response.ContentType = "application/json";
                response.Headers.Add("request-id", ScenarioParser.GetRequestId(scenario.Value));
                response.StatusCode = 200;

                var messageResponse = BuildMessageResponse(messageRequest, scenario.Value);
                var json = JsonSerializer.Serialize(messageResponse);
                await WriteResponse(response, json);
            }
        }
        catch (Exception ex)
        {
            response.StatusCode = 500;
            await WriteResponse(response, "{\"error\": \"" + ex.Message + "\"}");
        }
        finally
        {
            response.Close();
        }
    }

    private static async Task WriteResponse(HttpListenerResponse response, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
    }

    private static Scenario? DetectScenario(MessageRequest request)
    {
        foreach (var message in request.Messages.AsEnumerable().Reverse())
        {
            foreach (var block in message.Content.AsEnumerable().Reverse())
            {
                if (block.Type == ContentBlockType.Text && !string.IsNullOrEmpty(block.TextContent))
                {
                    var tokens = block.TextContent.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var token in tokens)
                    {
                        if (token.StartsWith(ScenarioParser.ScenarioPrefix))
                        {
                            var scenarioValue = token.Substring(ScenarioParser.ScenarioPrefix.Length);
                            return ScenarioParser.Parse(scenarioValue);
                        }
                    }
                }
            }
        }
        return null;
    }

    private static (string Content, bool IsError)? GetLatestToolResult(MessageRequest request)
    {
        foreach (var message in request.Messages.AsEnumerable().Reverse())
        {
            foreach (var block in message.Content.AsEnumerable().Reverse())
            {
                if (block.Type == ContentBlockType.ToolResult && !string.IsNullOrEmpty(block.ToolUseId))
                {
                    var content = FlattenToolResultContent(block.Content);
                    return (content, block.IsError);
                }
            }
        }
        return null;
    }

    private static Dictionary<string, (string Content, bool IsError)> GetToolResultsByName(MessageRequest request)
    {
        var toolNamesById = new Dictionary<string, string>();
        foreach (var message in request.Messages)
        {
            foreach (var block in message.Content)
            {
                if (block.Type == ContentBlockType.ToolUse && !string.IsNullOrEmpty(block.Id))
                {
                    toolNamesById[block.Id] = block.Name ?? block.Id;
                }
            }
        }

        var results = new Dictionary<string, (string, bool)>();
        foreach (var message in request.Messages.AsEnumerable().Reverse())
        {
            foreach (var block in message.Content.AsEnumerable().Reverse())
            {
                if (block.Type == ContentBlockType.ToolResult && !string.IsNullOrEmpty(block.ToolUseId))
                {
                    var toolName = toolNamesById.GetValueOrDefault(block.ToolUseId, block.ToolUseId);
                    if (!results.ContainsKey(toolName))
                    {
                        results[toolName] = (FlattenToolResultContent(block.Content), block.IsError);
                    }
                }
            }
        }
        return results;
    }

    private static string FlattenToolResultContent(List<ToolResultContentItem>? content)
    {
        if (content == null) return string.Empty;

        var parts = new List<string>();
        foreach (var item in content)
        {
            if (item.ContentType == "text" && !string.IsNullOrEmpty(item.Text))
            {
                parts.Add(item.Text);
            }
            else if (item.ContentType == "json" && item.JsonValue != null)
            {
                parts.Add(item.JsonValue.ToString() ?? string.Empty);
            }
        }
        return string.Join("\n", parts);
    }

    private static string BuildStreamBody(MessageRequest request, Scenario scenario)
    {
        return scenario switch
        {
            Scenario.StreamingText => SseBuilder.StreamingText(),
            Scenario.ReadFileRoundtrip => BuildReadFileRoundtripStream(request),
            Scenario.GrepChunkAssembly => BuildGrepChunkAssemblyStream(request),
            Scenario.WriteFileAllowed => BuildWriteFileAllowedStream(request),
            Scenario.WriteFileDenied => BuildWriteFileDeniedStream(request),
            Scenario.MultiToolTurnRoundtrip => BuildMultiToolTurnRoundtripStream(request),
            Scenario.BashStdoutRoundtrip => BuildBashStdoutRoundtripStream(request),
            Scenario.BashPermissionPromptApproved => BuildBashPermissionPromptApprovedStream(request),
            Scenario.BashPermissionPromptDenied => BuildBashPermissionPromptDeniedStream(request),
            Scenario.PluginToolRoundtrip => BuildPluginToolRoundtripStream(request),
            Scenario.AutoCompactTriggered => SseBuilder.FinalTextWithUsage("auto compact parity complete.", 50000, 200),
            Scenario.TokenCostReporting => SseBuilder.FinalTextWithUsage("token cost reporting parity complete.", 1000, 500),
            _ => SseBuilder.FinalText($"Unknown scenario: {scenario}")
        };
    }

    private static string BuildReadFileRoundtripStream(MessageRequest request)
    {
        var toolResult = GetLatestToolResult(request);
        if (toolResult.HasValue)
        {
            var content = ExtractReadContent(toolResult.Value.Content);
            return SseBuilder.FinalText($"read_file roundtrip complete: {content}");
        }
        return SseBuilder.ToolUse("toolu_read_fixture", "read_file", new[] { "{\"path\":\"fixture.txt\"}" });
    }

    private static string BuildGrepChunkAssemblyStream(MessageRequest request)
    {
        var toolResult = GetLatestToolResult(request);
        if (toolResult.HasValue)
        {
            var numMatches = ExtractNumMatches(toolResult.Value.Content);
            return SseBuilder.FinalText($"grep_search matched {numMatches} occurrences");
        }
        return SseBuilder.ToolUse("toolu_grep_fixture", "grep_search", new[] { "{\"pattern\":\"par", "ity\",\"path\":\"fixture.txt\"", ",\"output_mode\":\"count\"}" });
    }

    private static string BuildWriteFileAllowedStream(MessageRequest request)
    {
        var toolResult = GetLatestToolResult(request);
        if (toolResult.HasValue)
        {
            var filePath = ExtractFilePath(toolResult.Value.Content);
            return SseBuilder.FinalText($"write_file succeeded: {filePath}");
        }
        return SseBuilder.ToolUse("toolu_write_allowed", "write_file", new[] { "{\"path\":\"generated/output.txt\",\"content\":\"created by mock service\\n\"}" });
    }

    private static string BuildWriteFileDeniedStream(MessageRequest request)
    {
        var toolResult = GetLatestToolResult(request);
        if (toolResult.HasValue)
        {
            return SseBuilder.FinalText($"write_file denied as expected: {toolResult.Value.Content}");
        }
        return SseBuilder.ToolUse("toolu_write_denied", "write_file", new[] { "{\"path\":\"generated/denied.txt\",\"content\":\"should not exist\\n\"}" });
    }

    private static string BuildMultiToolTurnRoundtripStream(MessageRequest request)
    {
        var toolResults = GetToolResultsByName(request);
        if (toolResults.TryGetValue("read_file", out var readResult) && toolResults.TryGetValue("grep_search", out var grepResult))
        {
            var readContent = ExtractReadContent(readResult.Content);
            var numMatches = ExtractNumMatches(grepResult.Content);
            return SseBuilder.FinalText($"multi-tool roundtrip complete: {readContent} / {numMatches} occurrences");
        }

        return SseBuilder.ToolUses(new[]
        {
            new ToolUseInfo("toolu_multi_read", "read_file", new[] { "{\"path\":\"fixture.txt\"}" }),
            new ToolUseInfo("toolu_multi_grep", "grep_search", new[] { "{\"pattern\":\"par", "ity\",\"path\":\"fixture.txt\"", ",\"output_mode\":\"count\"}" })
        });
    }

    private static string BuildBashStdoutRoundtripStream(MessageRequest request)
    {
        var toolResult = GetLatestToolResult(request);
        if (toolResult.HasValue)
        {
            var stdout = ExtractBashStdout(toolResult.Value.Content);
            return SseBuilder.FinalText($"bash completed: {stdout}");
        }
        return SseBuilder.ToolUse("toolu_bash_stdout", "bash", new[] { "{\"command\":\"echo alpha from bash\",\"timeout\":1000}" });
    }

    private static string BuildBashPermissionPromptApprovedStream(MessageRequest request)
    {
        var toolResult = GetLatestToolResult(request);
        if (toolResult.HasValue)
        {
            if (toolResult.Value.IsError)
            {
                return SseBuilder.FinalText($"bash approval unexpectedly failed: {toolResult.Value.Content}");
            }
            var stdout = ExtractBashStdout(toolResult.Value.Content);
            return SseBuilder.FinalText($"bash approved and executed: {stdout}");
        }
        return SseBuilder.ToolUse("toolu_bash_prompt_allow", "bash", new[] { "{\"command\":\"echo approved via prompt\",\"timeout\":1000}" });
    }

    private static string BuildBashPermissionPromptDeniedStream(MessageRequest request)
    {
        var toolResult = GetLatestToolResult(request);
        if (toolResult.HasValue)
        {
            return SseBuilder.FinalText($"bash denied as expected: {toolResult.Value.Content}");
        }
        return SseBuilder.ToolUse("toolu_bash_prompt_deny", "bash", new[] { "{\"command\":\"echo should not run\",\"timeout\":1000}" });
    }

    private static string BuildPluginToolRoundtripStream(MessageRequest request)
    {
        var toolResult = GetLatestToolResult(request);
        if (toolResult.HasValue)
        {
            var message = ExtractPluginMessage(toolResult.Value.Content);
            return SseBuilder.FinalText($"plugin tool completed: {message}");
        }
        return SseBuilder.ToolUse("toolu_plugin_echo", "plugin_echo", new[] { "{\"message\":\"hello from plugin parity\"}" });
    }

    private static MessageResponse BuildMessageResponse(MessageRequest request, Scenario scenario)
    {
        return scenario switch
        {
            Scenario.StreamingText => ResponseBuilder.TextMessage("msg_streaming_text", "Mock streaming says hello from the parity harness."),
            Scenario.ReadFileRoundtrip => BuildReadFileRoundtripResponse(request),
            Scenario.GrepChunkAssembly => BuildGrepChunkAssemblyResponse(request),
            Scenario.WriteFileAllowed => BuildWriteFileAllowedResponse(request),
            Scenario.WriteFileDenied => BuildWriteFileDeniedResponse(request),
            Scenario.MultiToolTurnRoundtrip => BuildMultiToolTurnRoundtripResponse(request),
            Scenario.BashStdoutRoundtrip => BuildBashStdoutRoundtripResponse(request),
            Scenario.BashPermissionPromptApproved => BuildBashPermissionPromptApprovedResponse(request),
            Scenario.BashPermissionPromptDenied => BuildBashPermissionPromptDeniedResponse(request),
            Scenario.PluginToolRoundtrip => BuildPluginToolRoundtripResponse(request),
            Scenario.AutoCompactTriggered => ResponseBuilder.TextMessageWithUsage("msg_auto_compact_triggered", "auto compact parity complete.", 50000, 200),
            Scenario.TokenCostReporting => ResponseBuilder.TextMessageWithUsage("msg_token_cost_reporting", "token cost reporting parity complete.", 1000, 500),
            _ => ResponseBuilder.TextMessage("msg_unknown", $"Unknown scenario: {scenario}")
        };
    }

    private static MessageResponse BuildReadFileRoundtripResponse(MessageRequest request)
    {
        var toolResult = GetLatestToolResult(request);
        if (toolResult.HasValue)
        {
            var content = ExtractReadContent(toolResult.Value.Content);
            return ResponseBuilder.TextMessage("msg_read_file_final", $"read_file roundtrip complete: {content}");
        }
        return ResponseBuilder.ToolMessage("msg_read_file_tool", "toolu_read_fixture", "read_file", new { path = "fixture.txt" });
    }

    private static MessageResponse BuildGrepChunkAssemblyResponse(MessageRequest request)
    {
        var toolResult = GetLatestToolResult(request);
        if (toolResult.HasValue)
        {
            var numMatches = ExtractNumMatches(toolResult.Value.Content);
            return ResponseBuilder.TextMessage("msg_grep_final", $"grep_search matched {numMatches} occurrences");
        }
        return ResponseBuilder.ToolMessage("msg_grep_tool", "toolu_grep_fixture", "grep_search", new { pattern = "parity", path = "fixture.txt", output_mode = "count" });
    }

    private static MessageResponse BuildWriteFileAllowedResponse(MessageRequest request)
    {
        var toolResult = GetLatestToolResult(request);
        if (toolResult.HasValue)
        {
            var filePath = ExtractFilePath(toolResult.Value.Content);
            return ResponseBuilder.TextMessage("msg_write_allowed_final", $"write_file succeeded: {filePath}");
        }
        return ResponseBuilder.ToolMessage("msg_write_allowed_tool", "toolu_write_allowed", "write_file", new { path = "generated/output.txt", content = "created by mock service\n" });
    }

    private static MessageResponse BuildWriteFileDeniedResponse(MessageRequest request)
    {
        var toolResult = GetLatestToolResult(request);
        if (toolResult.HasValue)
        {
            return ResponseBuilder.TextMessage("msg_write_denied_final", $"write_file denied as expected: {toolResult.Value.Content}");
        }
        return ResponseBuilder.ToolMessage("msg_write_denied_tool", "toolu_write_denied", "write_file", new { path = "generated/denied.txt", content = "should not exist\n" });
    }

    private static MessageResponse BuildMultiToolTurnRoundtripResponse(MessageRequest request)
    {
        var toolResults = GetToolResultsByName(request);
        if (toolResults.TryGetValue("read_file", out var readResult) && toolResults.TryGetValue("grep_search", out var grepResult))
        {
            var readContent = ExtractReadContent(readResult.Content);
            var numMatches = ExtractNumMatches(grepResult.Content);
            return ResponseBuilder.TextMessage("msg_multi_tool_final", $"multi-tool roundtrip complete: {readContent} / {numMatches} occurrences");
        }

        return ResponseBuilder.ToolMessages("msg_multi_tool_start", new[]
        {
            new ToolUseMessage("toolu_multi_read", "read_file", new { path = "fixture.txt" }),
            new ToolUseMessage("toolu_multi_grep", "grep_search", new { pattern = "parity", path = "fixture.txt", output_mode = "count" })
        });
    }

    private static MessageResponse BuildBashStdoutRoundtripResponse(MessageRequest request)
    {
        var toolResult = GetLatestToolResult(request);
        if (toolResult.HasValue)
        {
            var stdout = ExtractBashStdout(toolResult.Value.Content);
            return ResponseBuilder.TextMessage("msg_bash_stdout_final", $"bash completed: {stdout}");
        }
        return ResponseBuilder.ToolMessage("msg_bash_stdout_tool", "toolu_bash_stdout", "bash", new { command = "echo alpha from bash", timeout = 1000 });
    }

    private static MessageResponse BuildBashPermissionPromptApprovedResponse(MessageRequest request)
    {
        var toolResult = GetLatestToolResult(request);
        if (toolResult.HasValue)
        {
            if (toolResult.Value.IsError)
            {
                return ResponseBuilder.TextMessage("msg_bash_prompt_allow_error", $"bash approval unexpectedly failed: {toolResult.Value.Content}");
            }
            var stdout = ExtractBashStdout(toolResult.Value.Content);
            return ResponseBuilder.TextMessage("msg_bash_prompt_allow_final", $"bash approved and executed: {stdout}");
        }
        return ResponseBuilder.ToolMessage("msg_bash_prompt_allow_tool", "toolu_bash_prompt_allow", "bash", new { command = "echo approved via prompt", timeout = 1000 });
    }

    private static MessageResponse BuildBashPermissionPromptDeniedResponse(MessageRequest request)
    {
        var toolResult = GetLatestToolResult(request);
        if (toolResult.HasValue)
        {
            return ResponseBuilder.TextMessage("msg_bash_prompt_deny_final", $"bash denied as expected: {toolResult.Value.Content}");
        }
        return ResponseBuilder.ToolMessage("msg_bash_prompt_deny_tool", "toolu_bash_prompt_deny", "bash", new { command = "echo should not run", timeout = 1000 });
    }

    private static MessageResponse BuildPluginToolRoundtripResponse(MessageRequest request)
    {
        var toolResult = GetLatestToolResult(request);
        if (toolResult.HasValue)
        {
            var message = ExtractPluginMessage(toolResult.Value.Content);
            return ResponseBuilder.TextMessage("msg_plugin_tool_final", $"plugin tool completed: {message}");
        }
        return ResponseBuilder.ToolMessage("msg_plugin_tool_start", "toolu_plugin_echo", "plugin_echo", new { message = "hello from plugin parity" });
    }

    // Extraction helpers
    private static string ExtractReadContent(string toolOutput)
    {
        try
        {
            using var doc = JsonDocument.Parse(toolOutput);
            if (doc.RootElement.TryGetProperty("content", out var rootContent))
            {
                return rootContent.GetString() ?? toolOutput.Trim();
            }
            if (doc.RootElement.TryGetProperty("file", out var file) &&
                file.TryGetProperty("content", out var content))
            {
                return content.GetString() ?? toolOutput.Trim();
            }
        }
        catch { }
        return toolOutput.Trim();
    }

    private static int ExtractNumMatches(string toolOutput)
    {
        try
        {
            using var doc = JsonDocument.Parse(toolOutput);
            if (doc.RootElement.TryGetProperty("numMatches", out var numMatches))
            {
                return numMatches.GetInt32();
            }
        }
        catch { }
        return 0;
    }

    private static string ExtractFilePath(string toolOutput)
    {
        try
        {
            using var doc = JsonDocument.Parse(toolOutput);
            if (doc.RootElement.TryGetProperty("filePath", out var filePath))
            {
                return filePath.GetString() ?? toolOutput.Trim();
            }
        }
        catch { }
        return toolOutput.Trim();
    }

    private static string ExtractBashStdout(string toolOutput)
    {
        try
        {
            using var doc = JsonDocument.Parse(toolOutput);
            if (doc.RootElement.TryGetProperty("stdout", out var stdout))
            {
                return stdout.GetString() ?? toolOutput.Trim();
            }
        }
        catch { }
        return toolOutput.Trim();
    }

    private static string ExtractPluginMessage(string toolOutput)
    {
        try
        {
            using var doc = JsonDocument.Parse(toolOutput);
            if (doc.RootElement.TryGetProperty("input", out var input) &&
                input.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? toolOutput.Trim();
            }
        }
        catch { }
        return toolOutput.Trim();
    }
}

