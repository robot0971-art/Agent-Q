using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentQ.Api;

public static class AgentQJsonOptions
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true
    };

    public static readonly JsonSerializerOptions CaseInsensitiveIndented = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static readonly JsonSerializerOptions CamelCaseIndented = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static readonly JsonSerializerOptions CamelCaseIndentedRelaxed = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static readonly JsonSerializerOptions IndentedIgnoreNullCaseInsensitive = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public static readonly JsonSerializerOptions WebCaseInsensitiveIndented = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static readonly JsonSerializerOptions WebCaseInsensitive = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static readonly JsonSerializerOptions CaseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
