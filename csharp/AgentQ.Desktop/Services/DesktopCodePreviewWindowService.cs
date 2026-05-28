using System.Windows;
using AgentQ.Desktop.Views;

namespace AgentQ.Desktop.Services;

public sealed class DesktopCodePreviewWindowService
{
    private CodePreviewWindow? _window;

    public void Show(Window owner, string title, string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        if (_window == null || !_window.IsLoaded)
        {
            _window = new CodePreviewWindow
            {
                Owner = owner,
                Left = owner.Left + owner.ActualWidth + 8,
                Top = owner.Top,
                Height = Math.Max(520, owner.ActualHeight)
            };
        }

        _window.ShowCode(title, source);
        _window.Show();
        _window.Activate();
    }
}
