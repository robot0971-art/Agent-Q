using System;
using System.Collections.Generic;
using System.Linq;
using AgentQ.Core.Models;
using AgentQ.Desktop.Services;
using Xunit;

namespace AgentQ.Tests;

public sealed class DesktopConversationCompactorTests
{
    [Fact]
    public void Compact_ShouldDoNothing_WhenMessagesBelowThreshold()
    {
        var compactor = new ConversationCompactor();
        var messages = new List<ChatMessage>
        {
            ChatMessage.SystemText("System message"),
            ChatMessage.UserText("User message 1"),
            ChatMessage.AssistantText("Assistant response 1")
        };

        var result = compactor.Compact(messages, maxEstimatedTokens: 1000, keepRecentTurns: 4);

        Assert.Equal(3, result.Count);
        Assert.Equal("System message", result[0].Content[0].Text);
        Assert.Equal("User message 1", result[1].Content[0].Text);
        Assert.Equal("Assistant response 1", result[2].Content[0].Text);
    }

    [Fact]
    public void Compact_ShouldReturnCopy_WhenMessagesBelowThreshold()
    {
        var compactor = new ConversationCompactor();
        const string request = "test2 \uD3F4\uB354\uB97C \uC0DD\uC131\uD574\uC918";
        var messages = new List<ChatMessage>
        {
            ChatMessage.UserText(request),
            ChatMessage.AssistantText("Thinking...")
        };

        var result = compactor.Compact(messages, maxEstimatedTokens: 1000, keepRecentTurns: 4);

        Assert.NotSame(messages, result);
        messages.Clear();
        Assert.Equal(2, result.Count);
        Assert.Equal(request, result[0].Content[0].Text);
    }

    [Fact]
    public void Compact_ShouldCompactOlderMessages_WhenEstimatedTokensExceedThreshold()
    {
        var compactor = new ConversationCompactor();
        var messages = new List<ChatMessage>
        {
            ChatMessage.SystemText("System message"),
            ChatMessage.UserText("User message 1 with some text"),
            ChatMessage.AssistantText("Assistant response 1 with longer text"),
            ChatMessage.UserText("User message 2"),
            ChatMessage.AssistantText("Assistant response 2"),
            ChatMessage.UserText("User message 3"),
            ChatMessage.AssistantText("Assistant response 3")
        };

        var result = compactor.Compact(messages, maxEstimatedTokens: 5, keepRecentTurns: 4);

        Assert.Equal(7, result.Count);
        Assert.Equal(ChatRole.System, result[0].Role);
        Assert.True(result[1].IsCompacted);
        Assert.True(result[2].IsCompacted);
        Assert.False(result[3].IsCompacted);
        Assert.Equal("User message 2", result[3].Content[0].Text);
        Assert.False(result[4].IsCompacted);
        Assert.Equal("Assistant response 2", result[4].Content[0].Text);
    }

    [Fact]
    public void Compact_ShouldKeepLatestUserRequestWhenHistoryIsLarge()
    {
        var compactor = new ConversationCompactor();
        const string latestRequest = "test2 \uD3F4\uB354\uB97C \uC0DD\uC131\uD574\uC918";
        var messages = new List<ChatMessage> { ChatMessage.SystemText("System message") };
        for (var i = 0; i < 40; i++)
        {
            messages.Add(ChatMessage.UserText($"old user message {i} " + new string('x', 200)));
            messages.Add(ChatMessage.AssistantText($"old assistant message {i} " + new string('y', 200)));
        }

        messages.Add(ChatMessage.UserText(latestRequest));

        var result = compactor.Compact(messages, maxEstimatedTokens: 5, keepRecentTurns: 4);

        Assert.Equal(latestRequest, result[^1].Content[0].Text);
        Assert.False(result[^1].IsCompacted);
        Assert.DoesNotContain(result, message =>
            message.IsCompacted &&
            message.Content.Any(content => string.Equals(content.Text, latestRequest, StringComparison.Ordinal)));
    }

    [Fact]
    public void Compact_ShouldSummarizeToolUsesAndResults()
    {
        var compactor = new ConversationCompactor();
        var toolUseMsg = ChatMessage.AssistantToolUse("id-1", "read_file", new { path = "test.txt" });
        var toolResultMsg = ChatMessage.UserToolResult("id-1", "File contents here\nLine 2\nLine 3\nLine 4", false);

        var messages = new List<ChatMessage>
        {
            ChatMessage.SystemText("System message"),
            toolUseMsg,
            toolResultMsg,
            ChatMessage.UserText("User message 2"),
            ChatMessage.AssistantText("Assistant response 2"),
            ChatMessage.UserText("User message 3"),
            ChatMessage.AssistantText("Assistant response 3")
        };

        var result = compactor.Compact(messages, maxEstimatedTokens: 5, keepRecentTurns: 4);

        Assert.Equal(7, result.Count);

        var compactedToolUse = result[1];
        Assert.True(compactedToolUse.IsCompacted);
        Assert.Contains("[Compacted Tool Use]", compactedToolUse.Content[0].Text);
        Assert.Contains("- Tool: read_file", compactedToolUse.Content[0].Text);
        Assert.Contains("- Tool use id: id-1", compactedToolUse.Content[0].Text);
        Assert.Contains("test.txt", compactedToolUse.Content[0].Text);

        var compactedToolResult = result[2];
        Assert.True(compactedToolResult.IsCompacted);
        Assert.Contains("[Compacted Tool Result]", compactedToolResult.Content[0].Text);
        Assert.Contains("- Tool use id: id-1", compactedToolResult.Content[0].Text);
        Assert.Contains("- Status: success", compactedToolResult.Content[0].Text);
    }

    [Fact]
    public void Compact_ShouldPreserveImportantToolContextBeyondPreview()
    {
        var compactor = new ConversationCompactor();
        var toolUseMsg = ChatMessage.AssistantToolUse("id-1", "bash", new { command = "npm run build" });
        var longToolResult = string.Join(
            "\n",
            Enumerable.Range(1, 20).Select(index => $"ordinary output line {index}")
                .Concat(
                [
                    "Error: build failed in C:\\Users\\admin\\Desktop\\Agent-Q\\src\\App.tsx",
                    "Exit code: 1",
                    "npm run build"
                ]));
        var toolResultMsg = ChatMessage.UserToolResult("id-1", longToolResult, true);

        var messages = new List<ChatMessage>
        {
            ChatMessage.SystemText("System message"),
            toolUseMsg,
            toolResultMsg,
            ChatMessage.UserText("User message 2"),
            ChatMessage.AssistantText("Assistant response 2"),
            ChatMessage.UserText("User message 3"),
            ChatMessage.AssistantText("Assistant response 3")
        };

        var result = compactor.Compact(messages, maxEstimatedTokens: 5, keepRecentTurns: 4);
        var compactedToolResult = result[2].Content[0].Text!;

        Assert.Contains("Important evidence:", compactedToolResult);
        Assert.Contains("Error: build failed", compactedToolResult);
        Assert.Contains("Exit code: 1", compactedToolResult);
        Assert.Contains("npm run build", compactedToolResult);
    }

    [Fact]
    public void Compact_ShouldPreservePriorityContextFromLongTextMessages()
    {
        var compactor = new ConversationCompactor();
        const string latestRequestLine = "- Latest user request: logs \uD3F4\uB354 \uB9CC\uB4E4\uC5B4\uC918";
        var longContext = string.Join(
            "\n",
            [
                "Latest user request priority:",
                latestRequestLine,
                "ordinary context " + new string('x', 1200),
                "Current task contract:",
                "- Intent: create_directory",
                "- Required completion evidence:",
                "  - create_directory tool result",
                "tail " + new string('y', 1200)
            ]);

        var messages = new List<ChatMessage>
        {
            ChatMessage.SystemText("System message"),
            ChatMessage.UserText(longContext),
            ChatMessage.AssistantText("Assistant response 1"),
            ChatMessage.UserText("User message 2"),
            ChatMessage.AssistantText("Assistant response 2"),
            ChatMessage.UserText("User message 3"),
            ChatMessage.AssistantText("Assistant response 3")
        };

        var result = compactor.Compact(messages, maxEstimatedTokens: 5, keepRecentTurns: 4);
        var compactedText = result[1].Content[0].Text!;

        Assert.Contains("Preserved priority context:", compactedText);
        Assert.Contains("Latest user request priority:", compactedText);
        Assert.Contains(latestRequestLine, compactedText);
        Assert.Contains("Current task contract:", compactedText);
        Assert.Contains("- Required completion evidence:", compactedText);
    }
}
