using System.Text;
using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

public static class DesktopConversationSummaryBuilder
{
    public static string BuildRecentText(IEnumerable<ChatMessageViewModel> messages, int maxMessages)
    {
        var builder = new StringBuilder();
        foreach (var message in messages.TakeLast(maxMessages))
        {
            builder.AppendLine($"{message.Role}:");
            builder.AppendLine(DesktopPromptBuilder.Truncate(message.Content, 2000));
            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }
}
