# Claw Code - C# 蹂???꾨줈?앺듃 怨꾪쉷

## ?꾨줈?앺듃 媛쒖슂

**紐⑺몴**: Rust 湲곕컲 Claw Code CLI瑜?C# (.NET 8)?쇰줈 蹂??
**紐⑹쟻**: ?숈뒿/?ㅽ뿕 - Rust ??C# 蹂???숈뒿 諛?CLI ?좏뵆由ъ???媛쒕컻 ?숈뒿

**?먮낯 ?꾨줈?앺듃**: `claw-code-parity` (Rust, 9 crates, ~48,600 LOC)

---

## ?꾩껜 ?꾨줈?앺듃 援ъ“

### ?섏〈??諛⑺뼢 (?쒗솚 李몄“ 諛⑹?)

```
AgentQ.sln
???쒋?? AgentQ.Api/                   ??理쒗븯??(?쒖닔 DTO, ?섏〈???놁쓬)
??  ?쒋?? MessageRequest/Response
??  ?쒋?? ContentBlocks
??  ?붴?? ToolDefinition
???쒋?? AgentQ.MockService/           ??Api留?李몄“
??  ?붴?? Mock Anthropic HTTP ?쒕쾭
???쒋?? AgentQ.Tools/                 ??Api留?李몄“ (?낅┰???좎?)
??  ?쒋?? IToolExecutor (Interface)
??  ?쒋?? BashTool
??  ?쒋?? ReadFileTool
??  ?붴?? PermissionEnforcer
???쒋?? AgentQ.Cli/                   ??Api, MockService 李몄“
??  ?쒋?? REPL
??  ?쒋?? Commands
??  ?붴?? Session
???붴?? AgentQ.Tests/                 ??紐⑤뱺 ?꾨줈?앺듃 李몄“
```

### ?듭떖 ?먯튃
- **AgentQ.Api**: ?쒖닔 ?곗씠??紐⑤뜽(DTO)留??댁쓬, **?대뼡 ?꾨줈?앺듃??李몄“?섏? ?딆쓬**
- **MockService, Tools**: Api留?李몄“ (?쒕줈 李몄“?섏? ?딆쓬)
- **Cli**: Api, MockService 李몄“ (理쒖긽??
- **Tools??AgentQ.Api ?몄뿉 ?꾨Т寃껊룄 李몄“?섏? ?딆쓬** ??Unity ???ㅻⅨ ?섍꼍 ?ъ궗??媛??
---

## Phase 1: Mock Anthropic Service (Day 1-3)

### 紐⑺몴
Anthropic Messages API瑜?mocking?섎뒗 HTTP ?쒕쾭 援ы쁽

### ?꾨줈?앺듃 援ъ“
```
AgentQ.MockService/
?쒋?? Program.cs                 # CLI entrypoint (~40以?
?쒋?? MockAnthropicService.cs    # HTTP ?쒕쾭 (~150以?
?쒋?? HttpParser.cs              # HTTP ?붿껌 ?뚯떛 (~120以?
?쒋?? Scenario.cs                # 12媛??쒕굹由ъ삤 enum + 媛먯? (~100以?
?쒋?? ScenarioHandler.cs         # ?쒕굹由ъ삤 泥섎━ 濡쒖쭅 (~350以?
?쒋?? SseBuilder.cs              # SSE ?ㅽ듃由щ컢 ?묐떟 (~180以?
?붴?? AgentQ.MockService.csproj

# ?섏〈?? AgentQ.Api留?李몄“
# Models??AgentQ.Api?먯꽌 媛?몄샂 (以묐났 諛⑹?)
```

### ?꾨줈?앺듃 李몄“
```xml
<!-- AgentQ.MockService.csproj -->
<ProjectReference Include="..\AgentQ.Api\AgentQ.Api.csproj" />
```

### 湲곗닠 ?ㅽ깮
- `System.Net.HttpListener` - HTTP ?쒕쾭
- `System.Text.Json` - JSON ?뚯떛
- `System.Threading.Tasks` - 鍮꾨룞湲?泥섎━

### 12媛??쒕굹由ъ삤
```
1. StreamingText           - ?띿뒪???ㅽ듃由щ컢 (湲곕낯)
2. ReadFileRoundtrip       - read_file ?꾧뎄 ?ㅽ뻾
3. GrepChunkAssembly       - grep_search 泥?겕 ?댁뀍釉붾━
4. WriteFileAllowed        - write_file ?깃났
5. WriteFileDenied         - write_file 沅뚰븳 嫄곕?
6. MultiToolTurnRoundtrip  - ?ㅼ쨷 ?꾧뎄 ?ㅽ뻾
7. BashStdoutRoundtrip     - bash ?ㅽ뻾
8. BashPermissionPromptApproved - bash 沅뚰븳 ?뱀씤
9. BashPermissionPromptDenied   - bash 沅뚰븳 嫄곕?
10. PluginToolRoundtrip    - ?뚮윭洹몄씤 ?꾧뎄
11. AutoCompactTriggered   - ?먮룞 compact
12. TokenCostReporting     - ?좏겙/鍮꾩슜 由ы룷??```

### NuGet ?⑦궎吏
```xml
<PackageReference Include="System.Text.Json" Version="8.0.0" />
```

### ?ㅽ뻾 寃利?```bash
# ?ㅽ뻾
dotnet run --project AgentQ.MockService -- --bind 127.0.0.1:8080

# 異쒕젰
MOCK_ANTHROPIC_BASE_URL=http://127.0.0.1:8080

# ?뚯뒪??(curl)
curl -X POST http://127.0.0.1:8080/v1/messages \
  -H "Content-Type: application/json" \
  -d '{"model":"claude-sonnet-4-6","max_tokens":100,"messages":[{"role":"user","content":[{"type":"text","text":"PARITY_SCENARIO:streaming_text hello"}]}],"stream":true}'
```

---

## Phase 2: API Types (Day 4-4.5)

### 紐⑺몴
**?쒖닔 ?곗씠??紐⑤뜽(DTO)留?* ?뺤쓽 - **?대뼡 ?꾨줈?앺듃??李몄“?섏? ?딆쓬**

### ?꾨줈?앺듃 援ъ“
```
AgentQ.Api/
?쒋?? MessageRequest.cs        # ?붿껌 紐⑤뜽 (~50以?
?쒋?? MessageResponse.cs       # ?묐떟 紐⑤뜽 (~45以?
?쒋?? ContentBlocks.cs         # Input/Output ContentBlock (~100以?
?쒋?? ToolDefinition.cs        # Tool ?뺤쓽 (~40以?
?쒋?? Usage.cs                 # ?좏겙 ?ъ슜??(~30以?
?붴?? AgentQ.Api.csproj

# ?좑툘 以묒슂: ???꾨줈?앺듃???대뼡 ?ㅻⅨ ?꾨줈?앺듃??李몄“?섏? ?딆쓬!
#         紐⑤뱺 ?꾨줈?앺듃媛 ?닿쾬??李몄“??```

### NuGet ?⑦궎吏
```xml
<!-- AgentQ.Api.csproj - 理쒖냼 ?섏〈??-->
<PackageReference Include="System.Text.Json" Version="8.0.0" />
<!-- .NET 8??湲곕낯 ?ы븿?섏뼱 ?덉쓬 -->
```

### NuGet ?⑦궎吏
```xml
<PackageReference Include="System.Text.Json" Version="8.0.0" />
```

---

## Phase 3: CLI (Day 5-9)

### 紐⑺몴
**REPL + one-shot prompt CLI 援ы쁽** - 理쒖긽???꾨줈?앺듃 (?ㅻⅨ ?꾨줈?앺듃??議고빀)

### ?꾨줈?앺듃 援ъ“
```
AgentQ.Cli/
?쒋?? Program.cs               # Entry point (~100以?
?쒋?? ArgsParser.cs            # CLI args ?뚯떛 (~300以?
?쒋?? Repl.cs                  # REPL 猷⑦봽 (~250以?
?쒋?? Commands/
??  ?쒋?? SlashCommands.cs     # /help, /status ??(~200以?
??  ?붴?? CommandHandlers.cs   # ?몃뱾??(~150以?
?쒋?? Renderer/
??  ?쒋?? TerminalRenderer.cs  # Spectre.Console ?뚮뜑留?(~150以?
??  ?붴?? MarkdownRenderer.cs  # Markdown ??ANSI (~100以?
?쒋?? Session/
??  ?쒋?? Session.cs           # ?몄뀡 愿由?(~150以?
??  ?붴?? SessionStore.cs      # JSONL ???濡쒕뱶 (~100以?
?붴?? AgentQ.Cli.csproj

# ?섏〈?? Api, MockService 李몄“ (Tools??媛꾩젒 李몄“)
```

### ?꾨줈?앺듃 李몄“
```xml
<!-- AgentQ.Cli.csproj -->
<ProjectReference Include="..\AgentQ.Api\AgentQ.Api.csproj" />
<ProjectReference Include="..\AgentQ.MockService\AgentQ.MockService.csproj" />
<ProjectReference Include="..\AgentQ.Tools\AgentQ.Tools.csproj" />
```

### NuGet ?⑦궎吏
```xml
<PackageReference Include="Spectre.Console" Version="0.49.1" />
<PackageReference Include="Spectre.Console.Json" Version="0.49.1" />
<PackageReference Include="System.Text.Json" Version="8.0.0" />
```

### 湲곗닠 ?ㅽ깮
- `Spectre.Console` - ?곕???UI
- `System.Text.Json` - ?몄뀡 ???
### NuGet ?⑦궎吏
```xml
<PackageReference Include="Spectre.Console" Version="0.49.1" />
<PackageReference Include="Spectre.Console.Json" Version="0.49.1" />
<PackageReference Include="System.Text.Json" Version="8.0.0" />
```

### CLI 紐낅졊??```bash
claw                    # REPL ?쒖옉
claw prompt "text"      # one-shot ?ㅽ뻾
claw login              # OAuth ?몄쬆
claw logout             # ?몄쬆 ??젣
claw --help             # ?꾩?留?claw --version          # 踰꾩쟾
claw status             # ?몄뀡 ?곹깭
```

---

## Phase 4: Tools (Day 10-14)

### 紐⑺몴
**6媛??듭떖 ?꾧뎄 援ы쁽** - AgentQ.Api ?몄뿉 ?꾨Т寃껊룄 李몄“?섏? ?딆쓬 (Unity ?ъ궗??媛??

### ?꾨줈?앺듃 援ъ“
```
AgentQ.Tools/
?쒋?? Abstractions/
??  ?붴?? IFileSystem.cs       # ?뚯씪 ?쒖뒪??異붿긽??(~40以?
?쒋?? ToolExecutor.cs          # ?꾧뎄 ?ㅽ뻾 ?명꽣?섏씠??(~100以?
?쒋?? Tools/
??  ?쒋?? BashTool.cs          # Bash ?ㅽ뻾 (~200以?
??  ?쒋?? ReadFileTool.cs      # ?뚯씪 ?쎄린 (~150以?
??  ?쒋?? WriteFileTool.cs     # ?뚯씪 ?곌린 (~150以?
??  ?쒋?? EditFileTool.cs      # ?뚯씪 ?몄쭛 (~200以?
??  ?쒋?? GrepTool.cs          # Grep 寃??(~180以?
??  ?붴?? GlobTool.cs          # Glob 寃??(~120以?
?쒋?? Permissions/
??  ?쒋?? PermissionMode.cs    # 沅뚰븳 紐⑤뱶 (~60以?
??  ?붴?? PermissionEnforcer.cs # 沅뚰븳 寃??(~150以?
?붴?? AgentQ.Tools.csproj

# ?섏〈?? AgentQ.Api留?李몄“
# ?좑툘 以묒슂: MockService, Cli瑜?李몄“?섏? ?딆쓬!
```

### ?꾨줈?앺듃 李몄“
```xml
<!-- AgentQ.Tools.csproj -->
<ProjectReference Include="..\AgentQ.Api\AgentQ.Api.csproj" />
```

### ?뮕 System.IO.Abstractions ?꾩엯 (?좏깮)
```xml
<!-- ?뚯뒪???몄쓽??諛?Unity ?명솚??-->
<PackageReference Include="System.IO.Abstractions" Version="21.0.0" />
```

### Rust Trait ??C# Interface
```csharp
// Rust: pub trait ToolExecutor { ... }
// C#:
public interface IToolExecutor
{
    string Name { get; }
    Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken ct);
}

// ?뚯씪 ?쒖뒪??異붿긽??public interface IFileSystem
{
    Task<string> ReadFileAsync(string path);
    Task WriteFileAsync(string path, string content);
    bool FileExists(string path);
}

// ?ㅼ젣 援ы쁽
public class PhysicalFileSystem : IFileSystem { ... }

// ?뚯뒪?몄슜 Mock 援ы쁽
public class InMemoryFileSystem : IFileSystem { ... }
```

### NuGet ?⑦궎吏
```xml
<PackageReference Include="System.Text.Json" Version="8.0.0" />
```

---

## Rust ??C# 留ㅽ븨 ?뚯씠釉?
### ?곗씠?????| Rust | C# |
|------|-----|
| `String` | `string` |
| `Vec<T>` | `List<T>` |
| `HashMap<K,V>` | `Dictionary<K,V>` |
| `Option<T>` | `T?` (nullable) |
| `Result<T,E>` | `Result<T>` ?먮뒗 ?덉쇅 |
| `enum` | `enum` ?먮뒗 `record` |

### 鍮꾨룞湲?| Rust | C# |
|------|-----|
| `tokio::spawn` | `Task.Run` |
| `async fn` | `async Task` |
| `.await` | `.await` |
| `tokio::select!` | `Task.WhenAny` |

### ?숈떆??| Rust | C# |
|------|-----|
| `Arc<Mutex<T>>` | `ConcurrentBag<T>` ?먮뒗 `lock` |
| `Arc<Mutex<Vec<T>>>` | `ConcurrentBag<T>` |
| `oneshot::channel` | `TaskCompletionSource<T>` |

### HTTP
| Rust | C# |
|------|-----|
| `tokio::net::TcpListener` | `HttpListener` |
| `reqwest::Client` | `HttpClient` |
| HTTP ?섎룞 ?뚯떛 | `HttpListenerContext` |

### JSON
| Rust | C# |
|------|-----|
| `serde_json::from_str` | `JsonSerializer.Deserialize` |
| `serde_json::to_string` | `JsonSerializer.Serialize` |
| `serde_json::json!` | `JsonNode.Create` |
| `#[serde(tag = "type")]` | `[JsonPolymorphic("type")]` |

### Rust Trait ??C# Interface
| Rust | C# |
|------|-----|
| `pub trait ToolExecutor` | `public interface IToolExecutor` |
| `impl ToolExecutor for BashTool` | `public class BashTool : IToolExecutor` |
| `Box<dyn ToolExecutor>` | `IToolExecutor` |
| `dyn Trait` | `interface` |

---

## ?묒뾽???덉긽

| Phase | ?뚯씪 ??| C# LOC | ?묒뾽??|
|-------|---------|---------|--------|
| Phase 1 (Mock) | 9媛?| ~1,005 | 2-3??|
| Phase 2 (Api) | 4媛?| ~370 | 0.5??|
| Phase 3 (Cli) | 9媛?| ~1,250 | 3-4??|
| Phase 4 (Tools) | 10媛?| ~1,260 | 5-7??|
| **?⑷퀎** | **32媛?* | **~3,885** | **10-14??* |

---

## Phase蹂?寃利?
| Phase | 寃利?諛⑸쾿 |
|-------|----------|
| Phase 1 | `curl` HTTP ?붿껌 ?뚯뒪??|
| Phase 2 | JSON serialization ?⑥쐞 ?뚯뒪??|
| Phase 3 | CLI ?ㅽ뻾 `dotnet run --project Cli` |
| Phase 4 | ?꾧뎄 ?ㅽ뻾 ?⑥쐞 ?뚯뒪??|

---

## ?ㅽ뻾 ?섍꼍

### .NET 踰꾩쟾
- **.NET 8** (LTS, ?щ줈???뚮옯??

### 媛쒕컻 ?섍꼍
- Visual Studio 2022 ?먮뒗 VS Code
- .NET SDK 8.0

### ?ㅽ뻾 諛⑹떇
```bash
# Mock ?쒕쾭 ?ㅽ뻾
dotnet run --project AgentQ.MockService -- --bind 127.0.0.1:8080

# CLI ?ㅽ뻾 (Mock ?쒕쾭 ?곌껐)
set ANTHROPIC_BASE_URL=http://127.0.0.1:8080
set ANTHROPIC_API_KEY=any-key
dotnet run --project AgentQ.Cli
```

---

## 李멸퀬 ?먮즺

### ?먮낯 Rust ?꾨줈?앺듃
- Location: `claw-code-parity/rust/`
- Mock Service: `rust/crates/mock-anthropic-service/src/lib.rs` (1,123以?
- API Types: `rust/crates/api/src/types.rs` (290以?
- CLI: `rust/crates/rusty-claude-cli/src/main.rs` (1,514以?
- Tools: `rust/crates/tools/src/lib.rs` (6,835以?

### Rust Mock Service 遺꾩꽍
- HTTP ?쒕쾭: `tokio::net::TcpListener` (?섎룞 HTTP ?뚯떛)
- ?쒕굹由ъ삤 媛먯?: `PARITY_SCENARIO:` prefix 寃??- SSE ?ㅽ듃由щ컢: `event: / data:` ?뺤떇
- JSON ?묐떟: `serde_json` 吏곷젹??
---

## 援ы쁽 ?곗꽑?쒖쐞

### Priority 1 (?꾩닔 - Phase 1)
```
?쒋?? MessageRequest/Response 紐⑤뜽
?쒋?? HttpListener 湲곕낯 ?쒕쾭
?쒋?? HTTP ?붿껌 ?뚯떛
?붴?? Scenario 媛먯?
```

### Priority 2 (?듭떖 - Phase 1)
```
?쒋?? StreamingText ?쒕굹由ъ삤 (媛???⑥닚)
?쒋?? ReadFileRoundtrip ?쒕굹由ъ삤
?쒋?? SSE ?ㅽ듃由щ컢 ?묐떟
```

### Priority 3 (?뺤옣 - Phase 1)
```
?쒋?? ?섎㉧吏 10媛??쒕굹由ъ삤
?쒋?? JSON non-streaming ?묐떟
?붴?? ?먮윭 泥섎━
```

---

## 蹂???쒖옉??
### ?щ컮瑜??쒖꽌 (?섏〈??怨좊젮)

```
Step 1: 理쒗븯???꾨줈?앺듃 (?섏〈???놁쓬)
?쒋?? AgentQ.sln ?앹꽦
?쒋?? AgentQ.Api/
??  ?쒋?? MessageRequest.cs
??  ?쒋?? MessageResponse.cs
??  ?쒋?? ContentBlocks.cs
??  ?붴?? AgentQ.Api.csproj

Step 2: 以묎컙 ?꾨줈?앺듃 (Api留?李몄“)
?쒋?? AgentQ.MockService/
??  ?쒋?? Program.cs
??  ?쒋?? MockAnthropicService.cs
??  ?쒋?? HttpParser.cs
??  ?쒋?? Scenario.cs
??  ?붴?? AgentQ.MockService.csproj
???붴?? AgentQ.Tools/
    ?쒋?? Abstractions/IFileSystem.cs
    ?쒋?? ToolExecutor.cs
    ?쒋?? Tools/BashTool.cs
    ?붴?? AgentQ.Tools.csproj

Step 3: 理쒖긽???꾨줈?앺듃 (紐⑤몢 李몄“)
?붴?? AgentQ.Cli/
    ?쒋?? Program.cs
    ?쒋?? ArgsParser.cs
    ?쒋?? Repl.cs
    ?붴?? AgentQ.Cli.csproj
```

### Phase 1 ?곸꽭 ?쒖꽌
1. `AgentQ.sln` - Solution ?앹꽦
2. `AgentQ.Api/AgentQ.Api.csproj` - Api ?꾨줈?앺듃 (?섏〈???놁쓬)
3. `AgentQ.Api/MessageRequest.cs` - ?붿껌 紐⑤뜽
4. `AgentQ.Api/MessageResponse.cs` - ?묐떟 紐⑤뜽
5. `AgentQ.Api/ContentBlocks.cs` - ContentBlock ???6. `AgentQ.MockService/AgentQ.MockService.csproj` - Mock ?꾨줈?앺듃 (Api 李몄“)
7. `AgentQ.MockService/HttpParser.cs` - HTTP ?붿껌 ?뚯떛
8. `AgentQ.MockService/MockAnthropicService.cs` - HTTP ?쒕쾭
9. `AgentQ.MockService/Scenario.cs` - ?쒕굹由ъ삤 enum + 媛먯?
10. `AgentQ.MockService/SseBuilder.cs` - SSE ?ㅽ듃由щ컢
11. `AgentQ.MockService/Program.cs` - CLI entrypoint

---

## ?듭떖 肄붾뱶 ?덉떆

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
        // 1. HTTP ?붿껌 ?뚯떛
        // 2. JSON ?뚯떛 ??MessageRequest
        // 3. Scenario 媛먯?
        // 4. ?묐떟 ?앹꽦 (JSON or SSE)
        // 5. HTTP ?묐떟 ?꾩넚
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

## ?깃났 吏??
### Phase 1 ?꾨즺 議곌굔
- Mock ?쒕쾭 ?ㅽ뻾 媛??- 12媛??쒕굹由ъ삤 紐⑤몢 泥섎━ 媛??- SSE ?ㅽ듃由щ컢 ?묐떟 ?뺤긽
- `curl` ?뚯뒪??紐⑤뱺 ?쒕굹由ъ삤 ?듦낵

### ?꾩껜 ?꾨줈?앺듃 ?꾨즺 議곌굔
- CLI REPL ?ㅽ뻾 媛??- Mock ?쒕쾭? ?듭떊 媛??- 6媛??꾧뎄 ?ㅽ뻾 媛??- ?몄뀡 ???濡쒕뱶 媛??
---

## ?ㅼ쓬 ?묒뾽

Phase 1 援ы쁽 ?쒖옉:
1. `AgentQ.sln` ?앹꽦
2. `AgentQ.MockService` ?꾨줈?앺듃 ?앹꽦
3. Models ?뚯씪 ?앹꽦
4. HTTP ?쒕쾭 援ы쁽
5. ?쒕굹由ъ삤 泥섎━ 濡쒖쭅 援ы쁽
6. SSE ?ㅽ듃由щ컢 援ы쁽
7. CLI entrypoint 援ы쁽
8. `curl` ?뚯뒪??寃利
