using System.IO;
using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

public sealed class DesktopFileChangeReviewService
{
    public void Mark(MainViewModel viewModel, FileChangeRecord? change, FileChangeReviewStatus status)
    {
        if (change == null)
        {
            viewModel.StatusText = "No file change selected";
            return;
        }

        change.ReviewStatus = status;
        viewModel.StatusText = $"Change marked {change.ReviewStatusText}";
        viewModel.AddLog($"File change review: {change.ReviewStatusText} - {change.RelativePath}");
    }

    public async Task RevertAsync(MainViewModel viewModel, FileChangeRecord? change, CancellationToken ct = default)
    {
        if (change == null)
        {
            viewModel.StatusText = "No file change selected";
            return;
        }

        if (string.IsNullOrWhiteSpace(change.Path))
        {
            viewModel.StatusText = "Cannot revert change without a file path";
            return;
        }

        if (!IsWorkspacePath(viewModel.WorkspaceRoot, change.Path))
        {
            viewModel.StatusText = "Cannot revert change outside the workspace";
            viewModel.AddLog($"File change revert blocked outside workspace: {change.Path}");
            return;
        }

        if (change.ExistedBefore)
        {
            if (string.Equals(change.Before, DesktopAgentService.DirectorySnapshotMarker, StringComparison.Ordinal))
            {
                Directory.CreateDirectory(change.Path);
            }
            else
            {
                await File.WriteAllTextAsync(change.Path, change.Before, ct);
            }
        }
        else if (File.Exists(change.Path))
        {
            File.Delete(change.Path);
        }
        else if (Directory.Exists(change.Path))
        {
            if (Directory.EnumerateFileSystemEntries(change.Path).Any())
            {
                viewModel.StatusText = "Cannot revert non-empty directory change";
                viewModel.AddLog($"File change revert blocked for non-empty directory: {change.RelativePath}");
                return;
            }

            Directory.Delete(change.Path);
        }

        change.ReviewStatus = FileChangeReviewStatus.Reverted;
        viewModel.StatusText = $"Change reverted: {change.RelativePath}";
        viewModel.AddLog(string.IsNullOrWhiteSpace(change.SnapshotPath)
            ? $"File change reverted: {change.RelativePath}"
            : $"File change reverted from snapshot: {change.RelativePath} ({change.SnapshotPath})");
    }

    private static bool IsWorkspacePath(string workspaceRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return WorkspacePathResolver.IsResolvedInsideWorkspace(workspaceRoot, path);
        }
        catch
        {
            return false;
        }
    }
}
