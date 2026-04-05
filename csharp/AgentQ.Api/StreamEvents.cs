namespace AgentQ.Api;

public enum StreamEventType
{
    MessageStart,
    MessageDelta,
    ContentBlockStart,
    ContentBlockDelta,
    ContentBlockStop,
    MessageStop
}

public class StreamEvent
{
    public StreamEventType Type { get; set; }
    public MessageResponse? Message { get; set; }
    public MessageDelta? Delta { get; set; }
    public Usage? Usage { get; set; }
    public uint Index { get; set; }
    public OutputContentBlock? ContentBlock { get; set; }
    public ContentBlockDelta? ContentDelta { get; set; }
}

public class MessageDelta
{
    public string? StopReason { get; set; }
    public string? StopSequence { get; set; }
}

public enum ContentBlockDeltaType
{
    TextDelta,
    InputJsonDelta,
    ThinkingDelta,
    SignatureDelta
}

public class ContentBlockDelta
{
    public ContentBlockDeltaType Type { get; set; }
    public string? Text { get; set; }
    public string? PartialJson { get; set; }
    public string? Thinking { get; set; }
    public string? Signature { get; set; }
}

