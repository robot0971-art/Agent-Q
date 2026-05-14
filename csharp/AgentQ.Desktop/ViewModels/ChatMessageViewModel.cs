namespace AgentQ.Desktop.ViewModels;

public sealed class ChatMessageViewModel
{
    public required string Role { get; init; }

    public required string Content { get; init; }

    public IReadOnlyList<ChatAttachmentViewModel> Attachments { get; init; } = [];

    public bool HasAttachments => Attachments.Count > 0;
}
