using System.Windows;
using SWC = System.Windows.Controls;
using SWM = System.Windows.Media;

namespace AgentQ.Desktop.Services;

public enum PermissionApprovalChoice
{
    Deny,
    AllowOnce,
    AllowSimilarForRun,
    AllowAllForRun
}

public sealed class PermissionApprovalDialog : Window
{
    private PermissionApprovalDialog(string title, PermissionDialogContent dialogContent, bool canAllowSimilar, bool canAllowAll)
    {
        Title = title;
        Width = 640;
        Height = 620;
        MinWidth = 440;
        MinHeight = 380;
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

        buttons.Children.Add(CreateButton("이번만 허용", () =>
        {
            Choice = PermissionApprovalChoice.AllowOnce;
            DialogResult = true;
        }));

        if (canAllowSimilar)
        {
            buttons.Children.Add(CreateButton("같은 종류 허용", () =>
            {
                Choice = PermissionApprovalChoice.AllowSimilarForRun;
                DialogResult = true;
            }));
        }

        if (canAllowAll)
        {
            buttons.Children.Add(CreateButton("편집+빌드 허용", () =>
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

        var body = new SWC.StackPanel();
        body.Children.Add(new SWC.TextBlock
        {
            Text = dialogContent.Summary,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = SWM.Brushes.Black,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });
        body.Children.Add(CreateKeyValueRow("위험도", dialogContent.RiskLabel));
        body.Children.Add(CreateKeyValueRow("대상", dialogContent.Target));
        body.Children.Add(CreateKeyValueRow("이유", dialogContent.Reason));
        body.Children.Add(CreateKeyValueRow("정책", dialogContent.Policy));
        body.Children.Add(CreateKeyValueRow("도구", $"{dialogContent.ToolName} - {dialogContent.ToolDescription}"));

        if (!string.IsNullOrWhiteSpace(dialogContent.FocusedPreview))
        {
            body.Children.Add(new SWC.TextBlock
            {
                Text = "미리보기",
                FontWeight = FontWeights.SemiBold,
                Foreground = SWM.Brushes.Black,
                Margin = new Thickness(0, 12, 0, 4)
            });
            body.Children.Add(CreateReadOnlyBox(dialogContent.FocusedPreview, 120));
        }

        body.Children.Add(new SWC.TextBlock
        {
            Text = "세부 입력",
            FontWeight = FontWeights.SemiBold,
            Foreground = SWM.Brushes.Black,
            Margin = new Thickness(0, 12, 0, 4)
        });
        body.Children.Add(CreateReadOnlyBox(dialogContent.RawInput, 180));

        var content = new SWC.ScrollViewer
        {
            VerticalScrollBarVisibility = SWC.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = SWC.ScrollBarVisibility.Disabled,
            Content = body
        };

        root.Children.Add(buttons);
        root.Children.Add(content);
        Content = root;
    }

    public PermissionApprovalChoice Choice { get; private set; }

    public static PermissionApprovalChoice Show(
        Window owner,
        string title,
        PermissionDialogContent dialogContent,
        bool canAllowSimilar,
        bool canAllowAll)
    {
        var dialog = new PermissionApprovalDialog(title, dialogContent, canAllowSimilar, canAllowAll)
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
            MinWidth = 92,
            Height = 30,
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(8, 0, 0, 0)
        };

        button.Click += (_, _) => onClick();
        return button;
    }

    private static SWC.Grid CreateKeyValueRow(string label, string value)
    {
        var grid = new SWC.Grid { Margin = new Thickness(0, 0, 0, 7) };
        grid.ColumnDefinitions.Add(new SWC.ColumnDefinition { Width = new GridLength(76) });
        grid.ColumnDefinitions.Add(new SWC.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(new SWC.TextBlock
        {
            Text = label,
            Foreground = SWM.Brushes.DimGray,
            FontWeight = FontWeights.SemiBold
        });

        var valueBlock = new SWC.TextBlock
        {
            Text = string.IsNullOrWhiteSpace(value) ? "(없음)" : value,
            Foreground = SWM.Brushes.Black,
            TextWrapping = TextWrapping.Wrap
        };
        SWC.Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(valueBlock);
        return grid;
    }

    private static SWC.TextBox CreateReadOnlyBox(string text, double maxHeight)
    {
        return new SWC.TextBox
        {
            Text = text,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = SWC.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = SWC.ScrollBarVisibility.Disabled,
            BorderBrush = SWM.Brushes.LightGray,
            FontFamily = new SWM.FontFamily("Consolas"),
            FontSize = 12,
            Padding = new Thickness(10),
            MaxHeight = maxHeight
        };
    }
}

public sealed record PermissionDialogContent(
    string Summary,
    string RiskLabel,
    string Target,
    string Reason,
    string Policy,
    string ToolName,
    string ToolDescription,
    string FocusedPreview,
    string RawInput);
