using System.Text;
using System.Windows;
using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

public sealed class DesktopClipboardService
{
    public void CopyMessage(MainViewModel viewModel, ChatMessageViewModel? message)
    {
        if (string.IsNullOrEmpty(message?.Content))
        {
            return;
        }

        System.Windows.Clipboard.SetText(message.Content);
        viewModel.StatusText = "Message copied to clipboard";
    }

    public void CopyLastAssistantMessage(MainViewModel viewModel)
    {
        var message = viewModel.Messages.LastOrDefault(item =>
            string.Equals(item.Role, "AgentQ", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(item.Content));

        if (message == null)
        {
            viewModel.StatusText = "No AgentQ response to copy";
            return;
        }

        System.Windows.Clipboard.SetText(message.Content);
        viewModel.StatusText = "Last response copied to clipboard";
    }

    public void CopyConversation(MainViewModel viewModel)
    {
        if (viewModel.Messages.Count == 0)
        {
            viewModel.StatusText = "No conversation to copy";
            return;
        }

        var builder = new StringBuilder();
        foreach (var message in viewModel.Messages)
        {
            builder.AppendLine($"{message.Role}:");
            builder.AppendLine(message.Content);
            builder.AppendLine();
        }

        System.Windows.Clipboard.SetText(builder.ToString().TrimEnd());
        viewModel.StatusText = "Conversation copied to clipboard";
    }

    public void CopyText(MainViewModel viewModel, string text, string successStatus)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            viewModel.StatusText = "Nothing to copy";
            return;
        }

        System.Windows.Clipboard.SetText(text);
        viewModel.StatusText = successStatus;
    }
}
