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
    private PermissionApprovalDialog(
        string title,
        PermissionDialogContent dialogContent,
        bool canAllowSimilar,
        bool canAllowAll,
        bool useKoreanUi)
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

        buttons.Children.Add(CreateButton(useKoreanUi ? "이번만 허용" : "Allow once", () =>
        {
            Choice = PermissionApprovalChoice.AllowOnce;
            DialogResult = true;
        }));

        if (canAllowSimilar)
        {
            buttons.Children.Add(CreateButton(useKoreanUi ? "같은 종류 허용" : "Allow similar", () =>
            {
                Choice = PermissionApprovalChoice.AllowSimilarForRun;
                DialogResult = true;
            }));
        }

        if (canAllowAll)
        {
            buttons.Children.Add(CreateButton(useKoreanUi ? "편집+빌드 허용" : "Allow edits + builds", () =>
            {
                Choice = PermissionApprovalChoice.AllowAllForRun;
                DialogResult = true;
            }));
        }

        buttons.Children.Add(CreateButton(useKoreanUi ? "거부" : "Deny", () =>
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
        body.Children.Add(CreateKeyValueRow(useKoreanUi ? "위험도" : "Risk", dialogContent.RiskLabel));
        body.Children.Add(CreateKeyValueRow(useKoreanUi ? "대상" : "Target", dialogContent.Target));
        body.Children.Add(CreateKeyValueRow(useKoreanUi ? "이유" : "Reason", dialogContent.Reason));
        body.Children.Add(CreateKeyValueRow(useKoreanUi ? "정책" : "Policy", dialogContent.Policy));
        body.Children.Add(CreateKeyValueRow(useKoreanUi ? "도구" : "Tool", $"{dialogContent.ToolName} - {dialogContent.ToolDescription}"));

        if (!string.IsNullOrWhiteSpace(dialogContent.FocusedPreview))
        {
            body.Children.Add(new SWC.TextBlock
            {
                Text = useKoreanUi ? "미리보기" : "Preview",
                FontWeight = FontWeights.SemiBold,
                Foreground = SWM.Brushes.Black,
                Margin = new Thickness(0, 12, 0, 4)
            });
            body.Children.Add(CreateReadOnlyBox(dialogContent.FocusedPreview, 120));
        }

        body.Children.Add(new SWC.TextBlock
        {
            Text = useKoreanUi ? "원본 입력" : "Raw input",
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
        bool canAllowAll,
        bool useKoreanUi = false)
    {
        var dialog = new PermissionApprovalDialog(title, dialogContent, canAllowSimilar, canAllowAll, useKoreanUi)
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
