# AgentQ - 멀티 LLM 코딩 어시스턴트

## 프로젝트 개요

**이름**: AgentQ 
**언어**: C# (.NET 8)  
 
**목표**: 여러 LLM 제공자를 지원하는 AI 기반 코딩 어시스턴트 구축

---

## 아키텍처 개요

```
AgentQ.sln
├── AgentQ.Api/                 # 공유 DTO 및 모델 (외부 의존성 없음)
│   ├── MessageRequest/Response
│   ├── ContentBlocks
│   └── ToolDefinition
├── AgentQ.MockService/         # 테스트용 HTTP 모의 서버
│   └── Mock Anthropic HTTP Server
├── AgentQ.Tools/               # 도구 구현체 (REPL 의존성만)
│   ├── IToolExecutor (인터페이스)
│   ├── BashTool
│   ├── ReadFileTool
│   ├── WriteFileTool
│   ├── EditFileTool
│   ├── GrepTool
│   └── GlobTool
├── AgentQ.Cli/                 # REPL CLI (Api, MockService 참조)
│   ├── REPL
│   ├── Commands
│   └── Session
└── AgentQ.Tests/               # 모든 프로젝트 테스트
```

### 의존성 규칙
- **AgentQ.Api**: 데이터 모델(DTO)만 포함, 다른 프로젝트 참조 없음
- **MockService, Tools**: Api만 참조 (상호 참조 없음)
- **Cli**: Api, MockService 참조 (최상위 수준)
- **Tools는 AgentQ.Api만 참조 가능** - Unity 호환성 확보

---

## 구현 단계

### Phase 1: Mock Anthropic Service (Day 1-3)

**목표**: 로컬 테스트용 Anthropic Messages API 모의 구현

**프로젝트 구조**:
```
AgentQ.MockService/
├── Program.cs                 # CLI 진입점
├── MockAnthropicService.cs    # HTTP 서버
├── HttpParser.cs              # HTTP 요청 파싱
├── Scenario.cs                # 12가지 테스트 시나리오
├── ScenarioHandler.cs         # 시나리오 처리 로직
└── SseBuilder.cs              # SSE 스트림 빌더
```

**핵심 기술**:
- `System.Net.HttpListener` - HTTP 서버
- `System.Text.Json` - JSON 파싱
- `System.Threading.Tasks` - 비동기 처리

**12가지 테스트 시나리오**:
1. StreamingText - 텍스트 스트리밍 (기본)
2. ReadFileRoundtrip - read_file 도구 실행
3. GrepChunkAssembly - grep_search 청크 조립
4. WriteFileAllowed - write_file 성공
5. WriteFileDenied - write_file 권한 거부
6. MultiToolTurnRoundtrip - 멀티턴 도구 실행
7. BashStdoutRoundtrip - bash 실행
8. BashPermissionPromptApproved - bash 권한 승인
9. BashPermissionPromptDenied - bash 권한 거부
10. PluginToolRoundtrip - 플러그인 도구 명령
11. AutoCompactTriggered - 자동 compact
12. TokenCostReporting - 토큰/비용 리포팅

**테스트**:
```bash
# 모의 서버 실행
dotnet run --project AgentQ.MockService -- --bind 127.0.0.1:8080

# curl로 테스트
curl -X POST http://127.0.0.1:8080/v1/messages \
  -H "Content-Type: application/json" \
  -d '{"model":"claude-sonnet-4-6","max_tokens":100,"messages":[{"role":"user","content":[{"type":"text","text":"PARITY_SCENARIO:streaming_text hello"]}],"stream":true}'
```

---

### Phase 2: API Types (Day 4-4.5)

**목표**: 공유 데이터 모델 정의 (DTO만, 로직 없음)

**프로젝트 구조**:
```
AgentQ.Api/
├── MessageRequest.cs        # 요청 모델
├── MessageResponse.cs       # 응답 모델
├── ContentBlocks.cs         # Input/Output ContentBlock
├── ToolDefinition.cs        # 도구 정의
└── Usage.cs                 # 토큰 사용량 통계
```

**핵심 설계**: 다른 모든 프로젝트가 이 모델을 참조 - 다른 상호 참조 없음!

---

### Phase 3: CLI (Day 5-9)

**목표**: REPL + one-shot prompt CLI 구현

**프로젝트 구조**:
```
AgentQ.Cli/
├── Program.cs               # 진입점
├── ArgsParser.cs            # CLI 인수 파싱
├── Repl.cs                  # REPL 루프
├── Commands/
│   ├── SlashCommands.cs     # /help, /status 등
│   └── CommandHandlers.cs   # 명령 처리
├── Renderer/
│   ├── TerminalRenderer.cs  # Spectre.Console 렌더링
│   └── MarkdownRenderer.cs  # Markdown to ANSI
└── Session/
    ├── Session.cs           # 세션 관리
    └── SessionStore.cs      # JSONL 로드/저장
```

**CLI 명령어**:
```bash
claw                    # REPL 시작
claw prompt "text"      # one-shot 실행
claw --help             # 도움말 표시
claw --version          # 버전 표시
claw status             # 세션 상태
```

**NuGet 패키지**:
```xml
<PackageReference Include="Spectre.Console" Version="0.49.1" />
<PackageReference Include="Spectre.Console.Json" Version="0.49.1" />
<PackageReference Include="System.Text.Json" Version="8.0.0" />
```

---

### Phase 4: Tools (Day 10-14)

**목표**: 6개 핵심 도구 구현 - AgentQ.Api만 의존 (Unity 호환)

**프로젝트 구조**:
```
AgentQ.Tools/
├── Abstractions/
│   └── IFileSystem.cs       # 파일 시스템 추상화
├── ToolExecutor.cs          # 도구 실행 디스패처
├── Tools/
│   ├── BashTool.cs          # Bash 실행
│   ├── ReadFileTool.cs      # 파일 읽기
│   ├── WriteFileTool.cs     # 파일 쓰기
│   ├── EditFileTool.cs      # 파일 편집
│   ├── GrepTool.cs          # Grep 검색
│   └── GlobTool.cs          # Glob 검색
└── Permissions/
    ├── PermissionMode.cs    # 권한 모드
    └── PermissionEnforcer.cs # 권한 검사
```

**인터페이스**:
```csharp
// Rust: pub trait ToolExecutor { ... }
// C#:
public interface IToolExecutor
{
    string Name { get; }
    Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken ct);
}

// 파일 시스템 추상화
public interface IFileSystem
{
    Task<string> ReadFileAsync(string path);
    Task WriteFileAsync(string path, string content);
    bool FileExists(string path);
}

// 물리적 구현
public class PhysicalFileSystem : IFileSystem { ... }

// 테스트용 Mock 구현
public class InMemoryFileSystem : IFileSystem { ... }
```

---

### Phase 5: 멀티 LLM 제공자 지원 (미래)

**목표**: LLM 제공자를 추상화하여 여러 API 지원 (Anthropic, OpenAI, Alibaba Qwen, Gemini, Ollama)

#### 핵심 설계 변경사항

**A. 제공자 추상화**
- 직접 `AnthropicApiClient` 참조에서 `ILlmProvider` 인터페이스로 변경
- 새 아키텍처: `Cli` -> `ILlmProvider` -> `구체적 구현체`

**B. 표준화된 내부 DTO**
- 모든 API용 공통 모델 정의
- `AgentQ.Core` 또는 `AgentQ.Api`: `ChatMessage`, `ToolCall`, `ToolResult`

**C. 어댑터 패턴**
- 각 제공자가 `ILlmProvider` 구현
- OpenAI 호환 API용 `OpenAiCompatibleProvider` (Alibaba, Groq, DeepSeek 등)

#### 인터페이스 정의

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

#### 구현 단계

**Phase 1: 핵심 추상화 (Day 1-2)**
1. `AgentQ.Core.Models`에 공통 모델 정의:
   - `ChatRole` (System, User, Assistant, Tool)
   - `ChatContent` (Text, ToolUse, ToolResult)
   - `UsageStats` (Input/Output Tokens)
2. `ILlmProvider` 인터페이스 정의

**Phase 2: 제공자 어댑터 (Day 3-5)**
1. `AnthropicProvider`: 기존 `AnthropicApiClient` 리팩토링
2. `OpenAiCompatibleProvider`: OpenAI 호환 API (Alibaba 포함)
3. `GoogleGeminiProvider`: Google Gemini API (선택사항)

**Phase 3: CLI 및 설정 (Day 6-7)**
1. `ProviderFactory`: 설정 기반 적절한 제공자 생성
2. 확장된 CLI 명령어:
   ```bash
   claw --provider alibaba --model qwen-2.5-coder
   claw --provider openai --model gpt-4o
   claw --provider ollama --model llama3
   ```

#### 향후 프로젝트 구조

```
AgentQ.sln
├── AgentQ.Core/              # 신규: 공통 모델
│   ├── Models/               # 공유 메시지 모델
│   └── Providers/
│       ├── ILlmProvider.cs
│       └── ProviderFactory.cs
├── AgentQ.Providers.Anthropic/   # Anthropic 어댑터
├── AgentQ.Providers.OpenAi/      # OpenAI 호환 (Alibaba 등)
├── AgentQ.Providers.Google/      # Gemini 어댑터 (선택)
├── AgentQ.Tools/             # 기존
└── AgentQ.Cli/               # ILlmProvider 사용하도록 수정
```

#### 기대 효과

1. **모델 자유도**: 사용자가 필요한 최적의 모델 선택 가능
2. **로컬 실행**: Ollama 지원으로 오프라인 작동 가능
3. **미래 대응**: 설정만으로 새로운 LLM 모델 추가 가능

---

## Rust to C# 매핑 참조

### 데이터 타입
| Rust | C# |
|------|-----|
| `String` | `string` |
| `Vec<T>` | `List<T>` |
| `HashMap<K,V>` | `Dictionary<K,V>` |
| `Option<T>` | `T?` (nullable) |
| `Result<T,E>` | `Result<T>` 또는 예외 |
| `enum` | `enum` 또는 `record` |

### 비동기
| Rust | C# |
|------|-----|
| `tokio::spawn` | `Task.Run` |
| `async fn` | `async Task` |
| `.await` | `.await` |
| `tokio::select!` | `Task.WhenAny` |

### 동기화
| Rust | C# |
|------|-----|
| `Arc<Mutex<T>>` | `ConcurrentBag<T>` 또는 `lock` |
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

### 트레이트
| Rust | C# |
|------|-----|
| `pub trait ToolExecutor` | `public interface IToolExecutor` |
| `impl ToolExecutor for BashTool` | `public class BashTool : IToolExecutor` |
| `Box<dyn ToolExecutor>` | `IToolExecutor` |

---

## 개발 환경

### 요구사항
- **.NET 8** (LTS)
- Visual Studio 2022 또는 VS Code
- .NET SDK 8.0

### 실행
```bash
# 모의 서버 실행
dotnet run --project AgentQ.MockService -- --bind 127.0.0.1:8080

# CLI 실행 (모의 서버 연결)
set ANTHROPIC_BASE_URL=http://127.0.0.1:8080
set ANTHROPIC_API_KEY=any-key
dotnet run --project AgentQ.Cli
```

---

## 코드 샘플

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
        // 1. HTTP 요청 파싱
        // 2. JSON 역직렬화 -> MessageRequest
        // 3. 시나리오 감지
        // 4. 응답 생성 (JSON 또는 SSE)
        // 5. HTTP 응답 전송
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

## 완료 기준

### Phase 1
- [x] 모의 서버 정상 실행
- [x] 12개 시나리오 모두 정상 처리
- [x] SSE 스트리밍 응답 작동
- [x] `curl` 테스트 모든 시나리오 통과

### 전체 프로젝트
- [x] CLI REPL 정상 실행
- [x] 모의 서버 연결
- [x] 6개 도구 정상 실행
- [x] 세션 저장/로드 작동

---

## 다음 단계

1. Phase 1-4 구현 완료
2. 핵심이 안정되면 Phase 5 (멀티 LLM 제공자) 시작
3. `ILlmProvider` 인터페이스 및 공통 모델 구현
4. 기존 코드에서 `AnthropicProvider` 리팩토링
5. Alibaba (DashScope)용 `OpenAiCompatibleProvider` 추가
