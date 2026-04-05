namespace AgentQ.MockService;

public enum Scenario
{
    StreamingText,
    ReadFileRoundtrip,
    GrepChunkAssembly,
    WriteFileAllowed,
    WriteFileDenied,
    MultiToolTurnRoundtrip,
    BashStdoutRoundtrip,
    BashPermissionPromptApproved,
    BashPermissionPromptDenied,
    PluginToolRoundtrip,
    AutoCompactTriggered,
    TokenCostReporting
}

public static class ScenarioParser
{
    public const string ScenarioPrefix = "PARITY_SCENARIO:";

    public static Scenario? Parse(string value)
    {
        return value.Trim() switch
        {
            "streaming_text" => Scenario.StreamingText,
            "read_file_roundtrip" => Scenario.ReadFileRoundtrip,
            "grep_chunk_assembly" => Scenario.GrepChunkAssembly,
            "write_file_allowed" => Scenario.WriteFileAllowed,
            "write_file_denied" => Scenario.WriteFileDenied,
            "multi_tool_turn_roundtrip" => Scenario.MultiToolTurnRoundtrip,
            "bash_stdout_roundtrip" => Scenario.BashStdoutRoundtrip,
            "bash_permission_prompt_approved" => Scenario.BashPermissionPromptApproved,
            "bash_permission_prompt_denied" => Scenario.BashPermissionPromptDenied,
            "plugin_tool_roundtrip" => Scenario.PluginToolRoundtrip,
            "auto_compact_triggered" => Scenario.AutoCompactTriggered,
            "token_cost_reporting" => Scenario.TokenCostReporting,
            _ => null
        };
    }

    public static string GetName(this Scenario scenario)
    {
        return scenario switch
        {
            Scenario.StreamingText => "streaming_text",
            Scenario.ReadFileRoundtrip => "read_file_roundtrip",
            Scenario.GrepChunkAssembly => "grep_chunk_assembly",
            Scenario.WriteFileAllowed => "write_file_allowed",
            Scenario.WriteFileDenied => "write_file_denied",
            Scenario.MultiToolTurnRoundtrip => "multi_tool_turn_roundtrip",
            Scenario.BashStdoutRoundtrip => "bash_stdout_roundtrip",
            Scenario.BashPermissionPromptApproved => "bash_permission_prompt_approved",
            Scenario.BashPermissionPromptDenied => "bash_permission_prompt_denied",
            Scenario.PluginToolRoundtrip => "plugin_tool_roundtrip",
            Scenario.AutoCompactTriggered => "auto_compact_triggered",
            Scenario.TokenCostReporting => "token_cost_reporting",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
    }

    public static string GetRequestId(Scenario scenario)
    {
        return $"req_{scenario.GetName()}";
    }
}

