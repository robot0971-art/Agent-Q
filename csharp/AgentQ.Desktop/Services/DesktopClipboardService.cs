using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

public sealed class DesktopClipboardService
{
    private const int MaxClipboardAttempts = 5;
    private const int ClipboardRetryDelayMs = 80;

    private readonly Action<string> _setClipboardText;

    public DesktopClipboardService()
        : this(text => System.Windows.Clipboard.SetDataObject(text, copy: true))
    {
    }

    public DesktopClipboardService(Action<string> setClipboardText)
    {
        _setClipboardText = setClipboardText;
    }

    public void CopyMessage(MainViewModel viewModel, ChatMessageViewModel? message)
    {
        if (string.IsNullOrEmpty(message?.Content))
        {
            viewModel.StatusText = "Nothing to copy";
            return;
        }

        CopyWithStatus(viewModel, message.Content, "Message copied to clipboard");
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

        CopyWithStatus(viewModel, message.Content, "Last response copied to clipboard");
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

        CopyWithStatus(viewModel, builder.ToString().TrimEnd(), "Conversation copied to clipboard");
    }

    public void CopyText(MainViewModel viewModel, string text, string successStatus)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            viewModel.StatusText = "Nothing to copy";
            return;
        }

        CopyWithStatus(viewModel, text, successStatus);
    }

    private void CopyWithStatus(MainViewModel viewModel, string text, string successStatus)
    {
        Exception? lastException = null;
        for (var attempt = 1; attempt <= MaxClipboardAttempts; attempt++)
        {
            try
            {
                _setClipboardText(text);
                viewModel.StatusText = successStatus;
                return;
            }
            catch (Exception ex) when (ex is ExternalException or InvalidOperationException or COMException)
            {
                lastException = ex;
                if (attempt < MaxClipboardAttempts)
                {
                    Thread.Sleep(ClipboardRetryDelayMs);
                }
            }
        }

        viewModel.StatusText = $"Clipboard copy failed: {lastException?.Message ?? "Unknown clipboard error"}";
    }
}
