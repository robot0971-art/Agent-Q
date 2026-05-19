using System.Windows;
using SWC = System.Windows.Controls;
using SWM = System.Windows.Media;

namespace AgentQ.Desktop.Services;

public enum PermissionApprovalChoice
{
    Deny,
    AllowOnce,
    AllowAllForRun
}

public sealed class PermissionApprovalDialog : Window
{
    private PermissionApprovalDialog(string title, string message, bool canAllowAll)
    {
        Title = title;
        Width = 520;
        Height = 640;
        MinWidth = 420;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        Background = SWM.Brushes.White;
        Choice = PermissionApprovalChoice.Deny;

        var root = new SWC.DockPanel
        {
            LastChildFill = true,
            Margin = new Thickness(14)
        };

        var buttons = new SWC.StackPanel
        {
            Orientation = SWC.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        SWC.DockPanel.SetDock(buttons, SWC.Dock.Bottom);

        buttons.Children.Add(CreateButton("허용", () =>
        {
            Choice = PermissionApprovalChoice.AllowOnce;
            DialogResult = true;
        }));

        if (canAllowAll)
        {
            buttons.Children.Add(CreateButton("전체 권한 허용", () =>
            {
                Choice = PermissionApprovalChoice.AllowAllForRun;
                DialogResult = true;
            }));
        }

        buttons.Children.Add(CreateButton("거부", () =>
        {
            Choice = PermissionApprovalChoice.Deny;
            DialogResult = false;
        }));

        var content = new SWC.TextBox
        {
            Text = message,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = SWC.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = SWC.ScrollBarVisibility.Disabled,
            BorderBrush = SWM.Brushes.LightGray,
            FontFamily = new SWM.FontFamily("Consolas"),
            FontSize = 12,
            Padding = new Thickness(10)
        };

        root.Children.Add(buttons);
        root.Children.Add(content);
        Content = root;
    }

    public PermissionApprovalChoice Choice { get; private set; }

    public static PermissionApprovalChoice Show(
        Window owner,
        string title,
        string message,
        bool canAllowAll)
    {
        var dialog = new PermissionApprovalDialog(title, message, canAllowAll)
        {
            Owner = owner
        };

        return dialog.ShowDialog() == true
            ? dialog.Choice
            : PermissionApprovalChoice.Deny;
    }

    private static SWC.Button CreateButton(string content, Action onClick)
    {
        var button = new SWC.Button
        {
            Content = content,
            MinWidth = 86,
            Height = 30,
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(8, 0, 0, 0)
        };

        button.Click += (_, _) => onClick();
        return button;
    }
}
