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

        // If we set a very low threshold like 5 tokens, it will definitely exceed it and compact.
        // It will keep system messages (1) and last `keepRecentTurns` (e.g. 4) of non-system messages.
        // non-system messages = 6. Last 4 are kept: User 2, Assistant 2, User 3, Assistant 3.
        // The first 2 (User 1, Assistant 1) will be compacted.
        var result = compactor.Compact(messages, maxEstimatedTokens: 5, keepRecentTurns: 4);

        Assert.Equal(7, result.Count);
        Assert.Equal(ChatRole.System, result[0].Role);
        
        // Compacted messages
        Assert.True(result[1].IsCompacted);
        Assert.True(result[2].IsCompacted);

        // Kept messages
        Assert.False(result[3].IsCompacted);
        Assert.Equal("User message 2", result[3].Content[0].Text);
        Assert.False(result[4].IsCompacted);
        Assert.Equal("Assistant response 2", result[4].Content[0].Text);
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

        // Compact with low threshold, keepRecentTurns = 4
        var result = compactor.Compact(messages, maxEstimatedTokens: 5, keepRecentTurns: 4);

        Assert.Equal(7, result.Count);
        
        // Verify tool use is summarized
        var compactedToolUse = result[1];
        Assert.True(compactedToolUse.IsCompacted);
        Assert.Contains("[Tool Use Summary]", compactedToolUse.Content[0].Text);
        Assert.Contains("🛠️ Call: read_file (ID: id-1)", compactedToolUse.Content[0].Text);

        // Verify tool result is summarized
        var compactedToolResult = result[2];
        Assert.True(compactedToolResult.IsCompacted);
        Assert.Contains("[Tool Result Summary]", compactedToolResult.Content[0].Text);
        Assert.Contains("✅ Success toolUseId: id-1", compactedToolResult.Content[0].Text);
    }
}
