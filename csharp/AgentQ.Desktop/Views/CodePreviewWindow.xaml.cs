using System.Windows;
using AgentQ.Desktop.Services;

namespace AgentQ.Desktop.Views;

public partial class CodePreviewWindow : Window
{
    public CodePreviewWindow()
    {
        InitializeComponent();
    }

    public void ShowCode(string title, string source)
    {
        Title = $"Code preview - {title}";
        PathText.Text = title;
        CodeBox.Document = DesktopCodeHighlighter.CreateDocument(source);
    }
}
