using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using AgentQ.Core.Models;
using AgentQ.MockService;
using AgentQ.Providers.Anthropic;
using Xunit;

namespace AgentQ.Tests;

public sealed class MockParityIntegrationTests
{
    [Fact]
    public async Task GenerateResponseAsync_CompletesPluginParityRoundtrip()
    {
        await using var fixture = await MockServiceFixture.StartAsync();
        var provider = new AnthropicProvider(fixture.BaseUrl, "test-key");
        var context = CreateContext("PARITY_SCENARIO:plugin_tool_roundtrip");
        var tools = CreateToolDefinitions("plugin_echo");

        var firstResponse = await provider.GenerateResponseAsync(context, tools);

        var toolUse = Assert.Single(firstResponse.Content, c => c.Type == ContentType.ToolUse);
        Assert.Equal("plugin_echo", toolUse.ToolName);

        context.Messages.Add(ChatMessage.AssistantToolUse(
            toolUse.ToolId!,
            toolUse.ToolName!,
            toolUse.ToolInput!));
        context.Messages.Add(ChatMessage.UserToolResult(
            toolUse.ToolId!,
            JsonSerializer.Serialize(new
            {
                input = new { message = "hello from plugin parity" },
                message = "hello from plugin parity"
            }),
            isError: false));

        var finalResponse = await provider.GenerateResponseAsync(context, tools);
        var finalText = Assert.Single(finalResponse.Content, c => c.Type == ContentType.Text).Text;

        Assert.Equal("plugin tool completed: hello from plugin parity", finalText);

        var capturedRequests = fixture.Service.GetCapturedRequests();
        Assert.Equal(2, capturedRequests.Count);
        Assert.All(capturedRequests, request => Assert.Equal("plugin_tool_roundtrip", request.Scenario));
        Assert.All(capturedRequests, request => Assert.False(request.Stream));
    }

    [Fact]
    public async Task GenerateStreamAsync_CompletesMultiToolParityRoundtrip()
    {
        await using var fixture = await MockServiceFixture.StartAsync();
        var provider = new AnthropicProvider(fixture.BaseUrl, "test-key");
        var context = CreateContext("PARITY_SCENARIO:multi_tool_turn_roundtrip");
        var tools = CreateToolDefinitions("read_file", "grep_search");

        var firstChunks = await CollectChunksAsync(provider.GenerateStreamAsync(context, tools));

        var completedToolUses = firstChunks
            .Where(chunk => chunk.ToolUseDelta?.IsComplete == true)
            .Select(chunk => chunk.ToolUseDelta!)
            .ToArray();

        Assert.Equal(2, completedToolUses.Length);
        Assert.Contains(completedToolUses, chunk => chunk.ToolName == "read_file");
        Assert.Contains(completedToolUses, chunk => chunk.ToolName == "grep_search");

        foreach (var toolUse in completedToolUses)
        {
            context.Messages.Add(ChatMessage.AssistantToolUse(
                toolUse.ToolId,
                toolUse.ToolName,
                JsonSerializer.Deserialize<JsonElement>(toolUse.PartialInput ?? "{}")));

            var toolResult = toolUse.ToolName switch
            {
                "read_file" => JsonSerializer.Serialize(new
                {
                    path = "fixture.txt",
                    content = "fixture parity text",
                    totalLines = 1,
                    readLines = 1,
                    offset = 1,
                    limit = 1
                }),
                "grep_search" => JsonSerializer.Serialize(new
                {
                    pattern = "parity",
                    numMatches = 2,
                    searchPath = "fixture.txt"
                }),
                _ => throw new InvalidOperationException($"Unexpected tool name: {toolUse.ToolName}")
            };

            context.Messages.Add(ChatMessage.UserToolResult(toolUse.ToolId, toolResult, isError: false));
        }

        var finalChunks = await CollectChunksAsync(provider.GenerateStreamAsync(context, tools));
        var finalText = string.Concat(finalChunks.Select(chunk => chunk.TextDelta).Where(text => !string.IsNullOrEmpty(text)));

        Assert.Equal("multi-tool roundtrip complete: fixture parity text / 2 occurrences", finalText);
        Assert.Contains(finalChunks, chunk => chunk.IsComplete);

        var capturedRequests = fixture.Service.GetCapturedRequests();
        Assert.Equal(2, capturedRequests.Count);
        Assert.All(capturedRequests, request => Assert.Equal("multi_tool_turn_roundtrip", request.Scenario));
        Assert.All(capturedRequests, request => Assert.True(request.Stream));
    }

    private static ChatContext CreateContext(string scenarioToken)
    {
        return new ChatContext
        {
            Model = MockAnthropicService.DefaultModel,
            Messages = new List<ChatMessage>
            {
                ChatMessage.UserText(scenarioToken)
            },
            MaxTokens = 1024
        };
    }

    private static ToolDefinition[] CreateToolDefinitions(params string[] names)
    {
        return names.Select(name => new ToolDefinition
        {
            Name = name,
            Description = $"{name} test tool",
            InputSchema = new
            {
                type = "object",
                properties = new Dictionary<string, object?>()
            }
        }).ToArray();
    }

    private static async Task<List<StreamChunk>> CollectChunksAsync(IAsyncEnumerable<StreamChunk> stream)
    {
        var chunks = new List<StreamChunk>();
        await foreach (var chunk in stream)
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    private sealed class MockServiceFixture : IAsyncDisposable
    {
        public required MockAnthropicService Service { get; init; }
        public required string BaseUrl { get; init; }

        public static async Task<MockServiceFixture> StartAsync()
        {
            var prefix = BuildListenerPrefix();
            var service = new MockAnthropicService();
            await service.StartAsync(prefix);

            return new MockServiceFixture
            {
                Service = service,
                BaseUrl = service.BaseUrl
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Service.StopAsync();
        }

        private static string BuildListenerPrefix()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            return $"http://127.0.0.1:{port}/";
        }
    }
}

