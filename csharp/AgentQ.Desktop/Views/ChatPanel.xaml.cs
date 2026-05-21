using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AgentQ.Desktop.Views;

public partial class ChatPanel : System.Windows.Controls.UserControl
{
    private const double MouseWheelScrollFactor = 0.35;
    private bool _messagesPinnedToBottom = true;

    public ChatPanel()
    {
        InitializeComponent();
    }

    public event EventHandler? AttachFilesRequested;
    public event EventHandler? BrowseWorkspaceRequested;
    public event EventHandler? ClearAttachmentsRequested;
    public event EventHandler? SendRequested;
    public event EventHandler? ContinueLastRunRequested;
    public event EventHandler? StopAgentRequested;
    public event EventHandler<object?>? CopyMessageRequested;

    public void ScrollMessagesToEndIfPinned()
    {
        Dispatcher.BeginInvoke(() =>
        {
            var scrollViewer = FindDescendant<ScrollViewer>(MessagesList);
            if (scrollViewer == null || !_messagesPinnedToBottom)
            {
                return;
            }

            scrollViewer.ScrollToEnd();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void AttachFiles_OnClick(object sender, RoutedEventArgs e)
    {
        AttachFilesRequested?.Invoke(this, EventArgs.Empty);
    }

    private void BrowseWorkspace_OnClick(object sender, RoutedEventArgs e)
    {
        BrowseWorkspaceRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ClearAttachments_OnClick(object sender, RoutedEventArgs e)
    {
        ClearAttachmentsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Send_OnClick(object sender, RoutedEventArgs e)
    {
        SendRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ContinueLastRun_OnClick(object sender, RoutedEventArgs e)
    {
        ContinueLastRunRequested?.Invoke(this, EventArgs.Empty);
    }

    private void StopAgent_OnClick(object sender, RoutedEventArgs e)
    {
        StopAgentRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CopyMessage_OnClick(object sender, RoutedEventArgs e)
    {
        var message = sender switch
        {
            System.Windows.Controls.MenuItem menuItem => menuItem.DataContext,
            System.Windows.Controls.Button button => button.Tag,
            _ => null
        };

        CopyMessageRequested?.Invoke(this, message);
    }

    private void InputBox_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            return;
        }

        e.Handled = true;
        SendRequested?.Invoke(this, EventArgs.Empty);
    }

    private void MessageTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox textBox)
        {
            textBox.ScrollToEnd();
        }
    }

    private void MessageTextBox_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scrollViewer = FindDescendant<ScrollViewer>(MessagesList);
        if (scrollViewer == null)
        {
            return;
        }

        e.Handled = true;
        SmoothScroll(scrollViewer, e.Delta);
    }

    private void MessagesList_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is ScrollViewer scrollViewer)
        {
            _messagesPinnedToBottom = IsNearBottom(scrollViewer);
        }
    }

    private static void SmoothScroll(ScrollViewer scrollViewer, int wheelDelta)
    {
        var targetOffset = scrollViewer.VerticalOffset - wheelDelta * MouseWheelScrollFactor;
        targetOffset = Math.Clamp(targetOffset, 0, scrollViewer.ScrollableHeight);
        scrollViewer.ScrollToVerticalOffset(targetOffset);
    }

    private static bool IsNearBottom(ScrollViewer scrollViewer)
    {
        return scrollViewer.ScrollableHeight <= 0 ||
               scrollViewer.ScrollableHeight - scrollViewer.VerticalOffset < 80;
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
            {
                return typed;
            }

            var descendant = FindDescendant<T>(child);
            if (descendant != null)
            {
                return descendant;
            }
        }

        return null;
    }
}
