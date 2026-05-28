using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AgentQ.Desktop.Services;
using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Views;

public partial class FileChangeReviewPanel : System.Windows.Controls.UserControl
{
    public FileChangeReviewPanel()
    {
        InitializeComponent();
    }

    public event EventHandler<FileChangeRecord?>? ApproveRequested;

    public event EventHandler<FileChangeRecord?>? NeedsEditRequested;

    public event EventHandler<FileChangeRecord?>? RevertRequested;

    public event EventHandler? ApproveAllAndVerifyRequested;

    public event EventHandler? RefreshSourceFilesRequested;

    public event EventHandler? SelectedSourceFileChanged;

    public event EventHandler? SelectedFileChangeChanged;

    private void ApproveAutoFixAndVerify_OnClick(object sender, RoutedEventArgs e)
    {
        ApproveAllAndVerifyRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ApproveFileChange_OnClick(object sender, RoutedEventArgs e)
    {
        ApproveRequested?.Invoke(this, (sender as FrameworkElement)?.DataContext as FileChangeRecord);
    }

    private void ApproveSelectedFileChange_OnClick(object sender, RoutedEventArgs e)
    {
        ApproveRequested?.Invoke(this, FileChangesList.SelectedItem as FileChangeRecord);
    }

    private void NeedsEditFileChange_OnClick(object sender, RoutedEventArgs e)
    {
        NeedsEditRequested?.Invoke(this, (sender as FrameworkElement)?.DataContext as FileChangeRecord);
    }

    private void NeedsEditSelectedFileChange_OnClick(object sender, RoutedEventArgs e)
    {
        NeedsEditRequested?.Invoke(this, FileChangesList.SelectedItem as FileChangeRecord);
    }

    private void RevertFileChange_OnClick(object sender, RoutedEventArgs e)
    {
        RevertRequested?.Invoke(this, (sender as FrameworkElement)?.DataContext as FileChangeRecord);
    }

    private void RevertSelectedFileChange_OnClick(object sender, RoutedEventArgs e)
    {
        RevertRequested?.Invoke(this, FileChangesList.SelectedItem as FileChangeRecord);
    }

    private void FileChangesList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedFileChangeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshSourceFiles_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshSourceFilesRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SourceFilesTree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SelectedSourceFile = e.NewValue as SourceFileEntry;
        }

        SelectedSourceFileChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SmoothScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
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
        var targetOffset = scrollViewer.VerticalOffset - e.Delta * DesktopUiConstants.MouseWheelScrollFactor;
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
