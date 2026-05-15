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

        await File.WriteAllTextAsync(change.Path, change.Before, ct);
        change.ReviewStatus = FileChangeReviewStatus.Reverted;
        viewModel.StatusText = $"Change reverted: {change.RelativePath}";
        viewModel.AddLog($"File change reverted: {change.RelativePath}");
    }
}
