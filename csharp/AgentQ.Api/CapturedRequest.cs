namespace AgentQ.Api;

public class CapturedRequest
{
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = new();
    public string Scenario { get; set; } = string.Empty;
    public bool Stream { get; set; }
    public string RawBody { get; set; } = string.Empty;
}

