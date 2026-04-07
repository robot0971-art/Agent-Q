using AgentQ.Cli;
using AgentQ.Core.Models;
using AgentQ.Core.Providers;
using Xunit;

namespace AgentQ.Tests;

public sealed class ConversationCompactorTests
{
    [Fact]
    public async Task CompactAsync_ReplacesOlderMessagesWithSummaryAndKeepsRecentTail()
    {
        var history = new ChatConversationHistory();
        history.AddUserMessage("u1");
        history.AddAssistantMessage([ChatContent.CreateText("a1")]);
        history.AddUserMessage("u2");
        history.AddAssistantMessage([ChatContent.CreateText("a2")]);
        history.AddUserMessage("u3");
        history.AddAssistantMessage([ChatContent.CreateText("a3")]);
        history.AddUserMessage("u4");
        history.AddAssistantMessage([ChatContent.CreateText("a4")]);

        var compactor = new ConversationCompactor();
        var provider = new CompactingProvider("summary text");

        var result = await compactor.CompactAsync(provider, "test-model", history);

        Assert.True(result.Applied);
        Assert.Equal(2, result.CompactedMessages);
        Assert.Equal(7, history.MessageCount);

        var first = history.Messages.First();
        Assert.Equal(ChatRole.System, first.Role);
        Assert.Contains("summary text", Assert.Single(first.Content).Text);

        Assert.Equal("u2", Assert.Single(history.Messages[1].Content).Text);
        Assert.Equal("a4", Assert.Single(history.Messages[^1].Content).Text);
    }

    [Fact]
    public async Task CompactAsync_SkipsWhenThereAreNotEnoughMessages()
    {
        var history = new ChatConversationHistory();
        history.AddUserMessage("u1");
        history.AddAssistantMessage([ChatContent.CreateText("a1")]);

        var compactor = new ConversationCompactor();
        var result = await compactor.CompactAsync(new CompactingProvider("unused"), "test-model", history);

        Assert.False(result.Applied);
        Assert.Equal("Not enough messages to compact.", result.Reason);
        Assert.Equal(2, history.MessageCount);
    }

    private sealed class CompactingProvider(string summary) : ILlmProvider
    {
        public string Name => "compacting";

        public string DefaultModel => "test-model";

        public Task<ChatResponse> GenerateResponseAsync(ChatContext context, IEnumerable<ToolDefinition> tools, CancellationToken ct = default)
        {
            return Task.FromResult(new ChatResponse
            {
                Model = context.Model,
                Content = [ChatContent.CreateText(summary)]
            });
        }

        public async IAsyncEnumerable<StreamChunk> GenerateStreamAsync(ChatContext context, IEnumerable<ToolDefinition> tools, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield break;
        }
    }
}
