using System.Collections.ObjectModel;
using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

public static class DesktopAttachmentWorkflowService
{
    public static ChatAttachmentViewModel ToViewModel(DesktopAttachment attachment)
    {
        return new ChatAttachmentViewModel
        {
            FileName = attachment.FileName,
            Kind = attachment.IsImage ? "Image" : "Video",
            Path = attachment.Path
        };
    }

    public static bool ClearAfterSuccessfulSend(
        ICollection<DesktopAttachment> attachments,
        ObservableCollection<string> attachmentNames)
    {
        if (attachments.Count == 0)
        {
            return false;
        }

        attachments.Clear();
        attachmentNames.Clear();
        return true;
    }

    public static string? BuildRetryLog(int attachmentCount)
    {
        return attachmentCount > 0
            ? "Attachments kept for retry after failed send"
            : null;
    }
}
