using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

public sealed class DesktopWindowCommandService(
    DesktopGitPanelWorkflowService gitPanelWorkflowService,
    DesktopVerificationPanelWorkflowService verificationPanelWorkflowService,
    DesktopAutoFixWorkflowService autoFixWorkflowService)
{
    public void IncreaseFontSize(MainViewModel viewModel)
    {
        viewModel.DesktopFontSize += 1;
        viewModel.StatusText = $"Font size: {viewModel.DesktopFontSize:0}";
    }

    public void DecreaseFontSize(MainViewModel viewModel)
    {
        viewModel.DesktopFontSize = Math.Max(11, viewModel.DesktopFontSize - 1);
        viewModel.StatusText = $"Font size: {viewModel.DesktopFontSize:0}";
    }

    public void ResetFontSize(MainViewModel viewModel)
    {
        viewModel.DesktopFontSize = 14;
        viewModel.StatusText = "Font size reset";
    }

    public void ShowStatus(MainViewModel viewModel)
    {
        viewModel.StatusText =
            $"Provider: {viewModel.Provider}, Model: {viewModel.Model}, Font size: {viewModel.DesktopFontSize:0}";
    }

    public void ClearLogs(MainViewModel viewModel)
    {
        viewModel.Logs.Clear();
        viewModel.AddLog("Logs cleared");
    }

    public void ClearSidePanel(MainViewModel viewModel)
    {
        viewModel.ClearSidePanelState();
        gitPanelWorkflowService.ClearPanel(viewModel);
        verificationPanelWorkflowService.ClearFailure(viewModel);
        autoFixWorkflowService.ClearPendingReview();
        viewModel.AddLog("Side panel cleared");
    }

    public void HandleSmoothScroll(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject source)
        {
            return;
        }

        var scrollViewer = source is ScrollViewer viewer
            ? viewer
            : FindDescendant<ScrollViewer>(source);
        if (scrollViewer == null)
        {
            return;
        }

        e.Handled = true;
        SmoothScroll(scrollViewer, e.Delta);
    }

    public void HandleTitleBarMouseDown(Window window, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleWindowMaximized(window);
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            window.DragMove();
        }
        catch (InvalidOperationException)
        {
            // WPF can throw when the mouse capture state changes during a drag.
        }
    }

    public void Minimize(Window window)
    {
        window.WindowState = WindowState.Minimized;
    }

    public void ToggleWindowMaximized(Window window)
    {
        window.WindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    public void Close(Window window)
    {
        window.Close();
    }

    private static void SmoothScroll(ScrollViewer scrollViewer, int wheelDelta)
    {
        var targetOffset = scrollViewer.VerticalOffset - wheelDelta * DesktopUiConstants.MouseWheelScrollFactor;
        targetOffset = Math.Clamp(targetOffset, 0, scrollViewer.ScrollableHeight);
        scrollViewer.ScrollToVerticalOffset(targetOffset);
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
