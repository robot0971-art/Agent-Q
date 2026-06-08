using System.IO;
using System.Windows;
using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

public sealed class DesktopAttachmentSelectionService
{
    private static readonly string[] SupportedAttachmentExtensions =
    [
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif",
        ".mp4", ".mov", ".avi", ".mkv", ".webm",
        ".txt", ".md", ".markdown", ".json", ".jsonl", ".csv", ".tsv",
        ".xml", ".yaml", ".yml", ".html", ".htm", ".css", ".js", ".jsx",
        ".ts", ".tsx", ".cs", ".py", ".java", ".kt", ".swift", ".go",
        ".rs", ".sql", ".log"
    ];

    public void SelectAttachments(Window owner, MainViewModel viewModel, ICollection<DesktopAttachment> attachments)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select attachments",
            Multiselect = true,
            Filter = "Supported attachments|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.mp4;*.mov;*.avi;*.mkv;*.webm;*.txt;*.md;*.markdown;*.json;*.jsonl;*.csv;*.tsv;*.xml;*.yaml;*.yml;*.html;*.htm;*.css;*.js;*.jsx;*.ts;*.tsx;*.cs;*.py;*.java;*.kt;*.swift;*.go;*.rs;*.sql;*.log|Images/Videos|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.mp4;*.mov;*.avi;*.mkv;*.webm|Text documents|*.txt;*.md;*.markdown;*.json;*.jsonl;*.csv;*.tsv;*.xml;*.yaml;*.yml;*.html;*.htm;*.css;*.js;*.jsx;*.ts;*.tsx;*.cs;*.py;*.java;*.kt;*.swift;*.go;*.rs;*.sql;*.log|All files|*.*"
        };

        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        foreach (var path in dialog.FileNames)
        {
            AddAttachment(path, viewModel, attachments);
        }

        viewModel.StatusText = attachments.Count == 0
            ? "No attachments selected."
            : $"{attachments.Count} attachment(s) selected.";
    }

    public void ClearAttachments(MainViewModel viewModel, ICollection<DesktopAttachment> attachments)
    {
        attachments.Clear();
        viewModel.Attachments.Clear();
        viewModel.StatusText = "Attachments cleared";
    }

    private static void AddAttachment(string path, MainViewModel viewModel, ICollection<DesktopAttachment> attachments)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (!SupportedAttachmentExtensions.Contains(extension))
        {
            viewModel.AddLog($"Unsupported attachment type: {Path.GetFileName(path)}");
            return;
        }

        if (attachments.Any(attachment => string.Equals(attachment.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        attachments.Add(new DesktopAttachment
        {
            Path = path,
            FileName = Path.GetFileName(path),
            MediaType = GetMediaType(extension)
        });
        viewModel.Attachments.Add(Path.GetFileName(path));
    }

    private static string GetMediaType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".avi" => "video/x-msvideo",
            ".mkv" => "video/x-matroska",
            ".webm" => "video/webm",
            ".txt" or ".log" => "text/plain",
            ".md" or ".markdown" => "text/markdown",
            ".json" or ".jsonl" => "application/json",
            ".csv" => "text/csv",
            ".tsv" => "text/tab-separated-values",
            ".xml" => "application/xml",
            ".yaml" or ".yml" => "application/x-yaml",
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".js" or ".jsx" => "text/javascript",
            ".ts" or ".tsx" => "text/typescript",
            ".cs" => "text/x-csharp",
            ".py" => "text/x-python",
            ".java" => "text/x-java",
            ".kt" => "text/x-kotlin",
            ".swift" => "text/x-swift",
            ".go" => "text/x-go",
            ".rs" => "text/x-rust",
            ".sql" => "text/x-sql",
            _ => "application/octet-stream"
        };
    }
}
