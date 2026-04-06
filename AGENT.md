# AgentQ - Multi-LLM Coding Assistant

## Project Overview

**Name**: AgentQ (previously ClawCode)  
**Language**: C# (.NET 8)  
**Origin**: Rust-based Claw Code CLI migration project  
**Goal**: Build an AI-powered coding assistant supporting multiple LLM providers

---

## Architecture Overview

```
AgentQ.sln
├── AgentQ.Api/                 # Shared DTOs and models (no external deps)
│   ├── MessageRequest/Response
│   ├── ContentBlocks
│   └── ToolDefinition
├── AgentQ.MockService/         # HTTP mock server for testing
│   └── Mock Anthropic HTTP Server
├── AgentQ.Tools/               # Tool implementations (REPL deps only)
│   ├── IToolExecutor (Interface)
│   ├── BashTool
│   ├── ReadFileTool
│   ├── WriteFileTool
│   ├── EditFileTool
│   ├── GrepTool
│   └── GlobTool
├── AgentQ.Cli/                 # REPL CLI (references Api, MockService)
│   ├── REPL
│   ├── Commands
│   └── Session
└── AgentQ.Tests/               # All project tests
```

### Dependency Rules
- **AgentQ.Api**: Contains only data models (DTOs), no references to other projects
- **MockService, Tools**: Reference Api only (no cross-references)
- **Cli**: References Api, MockService (highest level)
- **Tools can reference AgentQ.Api only** - enables Unity compatibility

---

## Implementation Phases

### Phase 1: Mock Anthropic Service (Day 1-3)

**Goal**: Mock Anthropic Messages API for local testing

**Project Structure**:
```
AgentQ.MockService/
├── Program.cs                 # CLI entrypoint
├── MockAnthropicService.cs    # HTTP server
├── HttpParser.cs              # HTTP request parsing
├── Scenario.cs                # 12 test scenarios
├── ScenarioHandler.cs         # Scenario processing logic
└── SseBuilder.cs              # SSE stream builder
```

**Key Technologies**:
- `System.Net.HttpListener` - HTTP server
- `System.Text.Json` - JSON parsing
- `System.Threading.Tasks` - Async processing

**12 Test Scenarios**:
1. StreamingText - Text streaming (basic)
2. ReadFileRoundtrip - read_file tool execution
3. GrepChunkAssembly - grep_search chunk assembly
4. WriteFileAllowed - write_file success
5. WriteFileDenied - write_file permission denied
6. MultiToolTurnRoundtrip - Multi-turn tool execution
7. BashStdoutRoundtrip - bash execution
8. BashPermissionPromptApproved - bash permission approved
9. BashPermissionPromptDenied - bash permission denied
10. PluginToolRoundtrip - Plugin tool command
11. AutoCompactTriggered - Auto compact
12. TokenCostReporting - Token/cost reporting

**Testing**:
```bash
# Run mock server
dotnet run --project AgentQ.MockService -- --bind 127.0.0.1:8080

# Test with curl
curl -X POST http://127.0.0.1:8080/v1/messages \
  -H "Content-Type: application/json" \
  -d '{"model":"claude-sonnet-4-6","max_tokens":100,"messages":[{"role":"user","content":[{"type":"text","text":"PARITY_SCENARIO:streaming_text hello"]}],"stream":true}'
```

---

### Phase 2: API Types (Day 4-4.5)

**Goal**: Define shared data models (DTOs only, no logic)

**Project Structure**:
```
AgentQ.Api/
├── MessageRequest.cs        # Request model
├── MessageResponse.cs       # Response model
├── ContentBlocks.cs         # Input/Output ContentBlock
├── ToolDefinition.cs        # Tool definitions
└── Usage.cs                 # Token usage stats
```

**Key Design**: All other projects reference these models - no other cross-project references!

---

### Phase 3: CLI (Day 5-9)

**Goal**: REPL + one-shot prompt CLI implementation

**Project Structure**:
```
AgentQ.Cli/
├── Program.cs               # Entry point
├── ArgsParser.cs            # CLI args parsing
├── Repl.cs                  # REPL loop
├── Commands/
│   ├── SlashCommands.cs     # /help, /status, etc.
│   └── CommandHandlers.cs   # Command processing
├── Renderer/
│   ├── TerminalRenderer.cs  # Spectre.Console rendering
│   └── MarkdownRenderer.cs  # Markdown to ANSI
└── Session/
    ├── Session.cs           # Session management
    └── SessionStore.cs      # JSONL load/save
```

**CLI Commands**:
```bash
claw                    # Start REPL
claw prompt "text"      # One-shot execution
claw --help             # Show help
claw --version          # Show version
claw status             # Session status
```

**NuGet Packages**:
```xml
<PackageReference Include="Spectre.Console" Version="0.49.1" />
<PackageReference Include="Spectre.Console.Json" Version="0.49.1" />
<PackageReference Include="System.Text.Json" Version="8.0.0" />
```

---

### Phase 4: Tools (Day 10-14)

**Goal**: Implement 6 core tools - AgentQ.Api only dependency (Unity compatible)

**Project Structure**:
```
AgentQ.Tools/
├── Abstractions/
│   └── IFileSystem.cs       # File system abstraction
├── ToolExecutor.cs          # Tool execution dispatcher
├── Tools/
│   ├── BashTool.cs          # Bash execution
│   ├── ReadFileTool.cs      # File reading
│   ├── WriteFileTool.cs     # File writing
│   ├── EditFileTool.cs      # File editing
│   ├── GrepTool.cs          # Grep search
│   └── GlobTool.cs          # Glob search
└── Permissions/
    ├── PermissionMode.cs    # Permission modes
    └── PermissionEnforcer.cs # Permission checking
```

**Interfaces**:
```csharp
// Rust: pub trait ToolExecutor { ... }
// C#:
public interface IToolExecutor
{
    string Name { get; }
    Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken ct);
}

// File system abstraction
public interface IFileSystem
{
    Task<string> ReadFileAsync(string path);
    Task WriteFileAsync(string path, string content);
    bool FileExists(string path);
}

// Physical implementation
public class PhysicalFileSystem : IFileSystem { ... }

// Mock for testing
public class InMemoryFileSystem : IFileSystem { ... }
```

---

### Phase 5: Multi-LLM Provider Support (Future)

**Goal**: Abstract LLM provider to support multiple APIs (Anthropic, OpenAI, Alibaba Qwen, Gemini, Ollama)

#### Key Design Changes

**A. Provider Abstraction**
- Change from direct `AnthropicApiClient` references to `ILlmProvider` interface
- New architecture: `Cli` -> `ILlmProvider` -> `Concrete Implementation`

**B. Standardized Internal DTO**
- Define common models for all APIs
- `AgentQ.Core` or `AgentQ.Api`: `ChatMessage`, `ToolCall`, `ToolResult`

**C. Adapter Pattern**
- Each provider implements `ILlmProvider`
- `OpenAiCompatibleProvider` for OpenAI-compatible APIs (Alibaba, Groq, DeepSeek)

#### Interface Definition

```csharp
public interface ILlmProvider
{
    string Name { get; }
    Task<ChatResponse> GenerateResponseAsync(
        ChatContext context, 
        IEnumerable<ToolDefinition> tools);
    IAsyncEnumerable<StreamChunk> GenerateStreamAsync(
        ChatContext context, 
        IEnumerable<ToolDefinition> tools);
}
```

#### Implementation Phases

**Phase 1: Core Abstraction (Day 1-2)**
1. Define common models in `AgentQ.Core.Models`:
   - `ChatRole` (System, User, Assistant, Tool)
   - `ChatContent` (Text, ToolUse, ToolResult)
   - `UsageStats` (Input/Output Tokens)
2. Define `ILlmProvider` interface

**Phase 2: Provider Adapters (Day 3-5)**
1. `AnthropicProvider`: Refactor existing `AnthropicApiClient`
2. `OpenAiCompatibleProvider`: OpenAI-compatible APIs (including Alibaba)
3. `GoogleGeminiProvider`: Google Gemini API (optional)

**Phase 3: CLI & Configuration (Day 6-7)**
1. `ProviderFactory`: Create appropriate provider based on config
2. Extended CLI commands:
   ```bash
   claw --provider alibaba --model qwen-2.5-coder
   claw --provider openai --model gpt-4o
   claw --provider ollama --model llama3
   ```

#### Future Project Structure

```
AgentQ.sln
├── AgentQ.Core/              # New: Common models
│   ├── Models/               # Shared message models
│   └── Providers/
│       ├── ILlmProvider.cs
│       └── ProviderFactory.cs
├── AgentQ.Providers.Anthropic/   # Anthropic adapter
├── AgentQ.Providers.OpenAi/      # OpenAI-compatible (Alibaba, etc.)
├── AgentQ.Providers.Google/      # Gemini adapter (optional)
├── AgentQ.Tools/             # Existing
└── AgentQ.Cli/               # Modified to use ILlmProvider
```

#### Expected Benefits

1. **Model Freedom**: Users can choose the best model for their needs
2. **Local Execution**: Support for Ollama enables offline operation
3. **Future-Proof**: New LLM models can be added via configuration

---

## Rust to C# Mapping Reference

### Data Types
| Rust | C# |
|------|-----|
| `String` | `string` |
| `Vec<T>` | `List<T>` |
| `HashMap<K,V>` | `Dictionary<K,V>` |
| `Option<T>` | `T?` (nullable) |
| `Result<T,E>` | `Result<T>` or exceptions |
| `enum` | `enum` or `record` |

### Async
| Rust | C# |
|------|-----|
| `tokio::spawn` | `Task.Run` |
| `async fn` | `async Task` |
| `.await` | `.await` |
| `tokio::select!` | `Task.WhenAny` |

### Synchronization
| Rust | C# |
|------|-----|
| `Arc<Mutex<T>>` | `ConcurrentBag<T>` or `lock` |
| `Arc<Mutex<Vec<T>>>` | `ConcurrentBag<T>` |
| `oneshot::channel` | `TaskCompletionSource<T>` |

### HTTP
| Rust | C# |
|------|-----|
| `tokio::net::TcpListener` | `HttpListener` |
| `reqwest::Client` | `HttpClient` |

### JSON
| Rust | C# |
|------|-----|
| `serde_json::from_str` | `JsonSerializer.Deserialize` |
| `serde_json::to_string` | `JsonSerializer.Serialize` |
| `#[serde(tag = "type")]` | `[JsonPolymorphic("type")]` |

### Traits
| Rust | C# |
|------|-----|
| `pub trait ToolExecutor` | `public interface IToolExecutor` |
| `impl ToolExecutor for BashTool` | `public class BashTool : IToolExecutor` |
| `Box<dyn ToolExecutor>` | `IToolExecutor` |

---

## Development Environment

### Requirements
- **.NET 8** (LTS)
- Visual Studio 2022 or VS Code
- .NET SDK 8.0

### Running
```bash
# Run mock server
dotnet run --project AgentQ.MockService -- --bind 127.0.0.1:8080

# Run CLI (connected to mock)
set ANTHROPIC_BASE_URL=http://127.0.0.1:8080
set ANTHROPIC_API_KEY=any-key
dotnet run --project AgentQ.Cli
```

---

## Code Samples

### MessageRequest.cs
```csharp
namespace AgentQ.MockService.Models;

public record MessageRequest(
    string Model,
    uint MaxTokens,
    List<InputMessage> Messages,
    bool Stream = false
);

public record InputMessage(string Role, List<InputContentBlock> Content);

[JsonPolymorphic("type")]
[JsonDerivedType(typeof(TextBlock), "text")]
[JsonDerivedType(typeof(ToolUseBlock), "tool_use")]
[JsonDerivedType(typeof(ToolResultBlock), "tool_result")]
public abstract record InputContentBlock();

public record TextBlock(string Text) : InputContentBlock;
public record ToolUseBlock(string Id, string Name, JsonElement Input) : InputContentBlock;
public record ToolResultBlock(string ToolUseId, List<ToolResultContent> Content, bool IsError) : InputContentBlock;
```

### MockAnthropicService.cs
```csharp
namespace AgentQ.MockService;

public class MockAnthropicService
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts;
    
    public async Task StartAsync(string bindAddress)
    {
        _listener.Prefixes.Add($"http://{bindAddress}/");
        _listener.Start();
        
        while (!_cts.Token.IsCancellationRequested)
        {
            var context = await _listener.GetContextAsync();
            _ = HandleRequestAsync(context);
        }
    }
    
    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        // 1. Parse HTTP request
        // 2. Deserialize JSON to MessageRequest
        // 3. Detect scenario
        // 4. Build response (JSON or SSE)
        // 5. Send HTTP response
    }
}
```

### Scenario.cs
```csharp
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

public static class ScenarioDetector
{
    public static Scenario? Detect(MessageRequest request)
    {
        const string prefix = "PARITY_SCENARIO:";
        foreach (var msg in request.Messages)
        {
            foreach (var block in msg.Content)
            {
                if (block is TextBlock text)
                {
                    var token = text.Text.Split()
                        .FirstOrDefault(t => t.StartsWith(prefix));
                    if (token != null)
                    {
                        var name = token[prefix.Length..];
                        return ParseScenario(name);
                    }
                }
            }
        }
        return null;
    }
}
```

---

## Completion Criteria

### Phase 1
- [x] Mock server runs successfully
- [x] All 12 scenarios process correctly
- [x] SSE streaming response works
- [x] `curl` tests pass for all scenarios

### Overall Project
- [x] CLI REPL runs successfully
- [x] Connects to mock server
- [x] 6 tools execute successfully
- [x] Session save/load works

---

## Next Steps

1. Complete Phase 1-4 implementation
2. Begin Phase 5 (Multi-LLM Provider) when core is stable
3. Implement `ILlmProvider` interface and common models
4. Refactor `AnthropicProvider` from existing code
5. Add `OpenAiCompatibleProvider` for Alibaba (DashScope)
