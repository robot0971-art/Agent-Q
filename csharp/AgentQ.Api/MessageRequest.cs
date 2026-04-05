namespace AgentQ.Api;

public class MessageRequest
{
    public string Model { get; set; } = string.Empty;
    public uint MaxTokens { get; set; }
    public List<InputMessage> Messages { get; set; } = new();
    public string? System { get; set; }
    public List<ToolDefinition>? Tools { get; set; }
    public ToolChoice? ToolChoice { get; set; }
    public bool Stream { get; set; }

    public MessageRequest WithStreaming()
    {
        Stream = true;
        return this;
    }
}

public class InputMessage
{
    public string Role { get; set; } = string.Empty;
    public List<InputContentBlock> Content { get; set; } = new();

    public static InputMessage UserText(string text)
    {
        return new InputMessage
        {
            Role = "user",
            Content = new List<InputContentBlock> { InputContentBlock.CreateText(text) }
        };
    }

    public static InputMessage UserToolResult(string toolUseId, string content, bool isError)
    {
        return new InputMessage
        {
            Role = "user",
            Content = new List<InputContentBlock>
            {
                InputContentBlock.CreateToolResult(toolUseId, content, isError)
            }
        };
    }
}

