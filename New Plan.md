# AgentQ New Plan

Updated: 2026-04-08

## Current Position

AgentQ has moved past the prototype stage, but it is not yet at product-grade polish.

The core foundation is already in place:

- `.NET global tool` packaging exists and `agentq` is the command name
- interactive REPL exists
- `--prompt`, `--stdin`, and `--input` all enter non-interactive one-shot execution
- `--json` returns a machine-readable envelope
- non-interactive exit codes are defined
- non-interactive permission policy exists
  - default deny
  - `--yes` for full allow
  - repeated `--allow-tool <name>` for narrow allow
- automation helper tests exist
- process-level automation integration tests exist
- session/config persistence exists
- tool execution loop exists across interactive and non-interactive paths

Recent hands-on validation exposed important product gaps:

- one-shot OpenAI runs are working correctly via `agentq --prompt ... --json`
- interactive REPL had a streamed-output rendering bug where text appeared and then vanished
- interactive permission flow could crash when tool arguments arrived as a JSON string instead of an object
- local code fixes do not reliably reach the globally installed `agentq` command because the package version is static
- first-run configuration is still too shell-dependent and too easy to lose

Current quality read:

- feature completeness: high
- product completeness: moderate
- main risk area: UX and release flow, not core architecture

## What Is Already Done

These should not stay in the active work queue:

- core CLI loop and provider abstraction
- tool execution and workspace safety
- session/config persistence
- conversation compaction
- wrapper build/test scripts
- `.NET global tool` packaging
- non-interactive entrypoint
- JSON output baseline
- exit-code baseline
- non-interactive permission baseline
- automation process-level integration coverage

## Recent Fixes

### 1. Interactive response visibility

Status: fixed locally on 2026-04-08

Issue that was observed:

- REPL streamed text was written inside the Spectre status region
- console redraws caused the assistant text to flash and disappear

Resolution:

- stopped writing streamed text directly into the status area
- final assistant text is now rendered from conversation history after the request completes

Primary file:

- `csharp/AgentQ.Cli/Program.cs`

### 2. Permission summary robustness

Status: fixed locally on 2026-04-08

Issue that was observed:

- permission summary rendering assumed tool arguments were always a JSON object
- some tool-call paths can provide JSON text instead
- that mismatch caused an interactive API error before permission could be granted

Resolution:

- permission summary parsing now tolerates string payloads and non-object roots
- interactive permission flow no longer fails on that shape mismatch

Primary file:

- `csharp/AgentQ.Cli/ConsolePermissionEnforcer.cs`

## Active Work Queue

### 1. Fix packaged and global-tool update flow

Priority: highest

Current issue:

- local fixes build successfully
- the globally installed `agentq` command does not reliably pick them up
- current package versioning does not force a usable upgrade path during development

Target:

- define a versioning/update rule that makes local rebuilds visible in the installed tool
- remove the confusion between `agentq` and direct `AgentQ.Cli.exe` runs
- document the intended dev and release paths separately

Primary files:

- `csharp/AgentQ.Cli/AgentQ.Cli.csproj`
- `README.md`
- release/build scripts if needed

### 2. Improve persisted configuration UX

Priority: high

Current issue:

- users still have to understand shell-local `set` vs persistent `setx`
- stored configuration flow is not obvious enough for first-time use
- API key/provider/model/base-url setup should be simpler and safer

Target:

- make persistent configuration a first-class path
- improve help/startup guidance for missing config
- document shell-specific behavior clearly enough to avoid user confusion

Primary files:

- `csharp/AgentQ.Cli/ConfigStore.cs`
- `csharp/AgentQ.Cli/Program.cs`
- `README.md`

### 3. Finish REPL UX stabilization

Priority: high

Current issue:

- the biggest visible rendering bug is fixed
- the REPL still needs calmer status behavior, clearer tool logs, and more predictable console output
- startup branding should stay coherent with the rest of the console styling

Target:

- make interactive output consistently readable
- tighten permission prompts and tool execution logging
- reduce visually noisy redraw behavior where possible

Primary files:

- `csharp/AgentQ.Cli/Program.cs`
- `csharp/AgentQ.Cli\ConsolePermissionEnforcer.cs`
- `csharp/AgentQ.Cli\CliToolLoopRunner.cs`

### 4. Add regression coverage for newly found interactive bugs

Priority: medium

Current issue:

- recent failures were discovered manually
- there is still not enough protection against REPL regressions

Target:

- add tests for REPL output behavior where practical
- add tests for permission-summary parsing when tool input is a string
- add release validation steps for global-tool update behavior

Primary files:

- `csharp/AgentQ.Tests/CliToolLoopㅅRunnerTests.cs`
- `csharp/AgentQ.Tests/ToolAndConfigurationTests.cs`
- integration coverage if practical

## Recommended Order

1. Fix packaged and global-tool update flow
2. Improve persisted configuration UX
3. Finish REPL UX stabilization
4. Add regression coverage for newly found interactive bugs
5. Resume provider-compatibility validation once the user-facing path is stable

Reason:

- the biggest user confusion now is not missing functionality, but mismatch between what was fixed and what the installed tool actually runs
- configuration friction is still a first-run blocker
- REPL polish should continue after the release/install path is trustworthy
- regression tests should lock in the bugs already found by manual use

## Definition Of Done

The current product-hardening pass is complete when:

- `agentq` and direct CLI execution behave the same after an update
- `agentq --prompt`, `--stdin`, and `--input` all work reliably
- interactive REPL responses remain visible and readable
- permission prompts do not fail on mixed tool-argument shapes
- persistent configuration is straightforward for normal users
- non-interactive JSON output is stable and structured enough for scripting
- permission policy supports allow and deny behavior explicitly
- exit codes remain deterministic
- tests cover the newly fixed interactive regressions
- README matches the actual CLI behavior

## Immediate Next Step

Make the installed `agentq` command reliably update to the latest local build before taking on more feature work.

That is the highest-value next step because it removes the biggest source of confusion during manual validation and makes every subsequent fix observable through the normal user entrypoint.


현재 Program.cs는 모든 객체의 생성과 생명주기를 직접 관리하고 있어, 로직이 비대해지고 테스트가 어려운 구조입니다. 이를 Microsoft.Extensions.DependencyInjection을 활용해 개선하기 위한 5단계 계획입니다.

1. 서비스 인터페이스 추상화 (Static 클래스의 서비스화)
가장 먼저 수행할 작업은 정적(static) 메서드로 구성된 유틸리티 클래스들을 인터페이스 기반의 서비스로 전환하는 것입니다.

IConfigService (구 ConfigStore): 설정 로드, 저장, 삭제를 담당합니다.
ISessionService (구 SessionStore): 대화 기록의 파일 저장 및 로드를 담당합니다
.
IFileSystem (선택 사항): File.ReadAllTextAsync 등을 추상화하여, 테스트 시 실제 파일 시스템 없이도 동작을 검증할 수 있게 합니다.

2. 핵심 컴포넌트의 서비스 등록 및 생명주기 정의
CliToolLoopRunner, ConversationCompactor 등 핵심 로직을 담당하는 클래스들을 서비스 컨테이너에 등록합니다.

Singleton (단일 인스턴스):
ToolRegistry: 도구 목록은 애플리케이션 실행 동안 변하지 않으므로 싱글톤이 적합합니다.
ProviderFactory: 제공자 생성 로직을 관리합니다.
CliToolLoopRunner, ConversationCompactor: 상태를 가지지 않는 실행 로직이므로 싱글톤으로 충분합니다.

Scoped / Transient (대화 세션별):
ChatConversationHistory: 하나의 실행 세션 동안 유지되어야 하는 상태값이므로, 세션 단위로 관리합니다.
IPermissionEnforcer: 실행 모드(대화형 vs 비대화형)에 따라 주입되는 구현체가 달라져야 합니다.

3. 실행 모드에 따른 전략 주입 (Strategy Pattern)
현재 Program.cs는 if (invocation.IsNonInteractive) 분기문이 곳곳에 산재해 있습니다. 이를 DI를 통해 깔끔하게 분리합니다.

IPermissionEnforcer 분리: 서비스 등록 시점에 AutomationInvocation 결과를 확인하여 ConsolePermissionEnforcer 또는 NonInteractivePermissionEnforcer 중 하나를 컨테이너에 등록합니다.
IAgentApplication 인터페이스: InteractiveApp과 AutomationApp으로 나누어, Program.cs는 단순히 진입점 역할만 수행하고 실제 루프는 각 서비스가 담당하게 합니다.
4. LLM Provider 팩토리 개선
현재 ProviderFactory를 DI 컨테이너와 연동하여, 설정값(config.json 또는 환경 변수)에 따라 적절한 ILlmProvider가 주입되도록 구성합니다.

AddHttpClient를 활용하여 AnthropicProvider나 OpenAiCompatibleProvider 내부의 HTTP 통신을 더 안정적으로 관리(Retries, Timeout 등)할 수 있습니다.

5. Program.cs 리팩토링 단계 (최종 구조)
DI 도입 후 Program.cs의 모습은 대략 다음과 같은 흐름을 가지게 됩니다.

Configuration 설정: CommandLine 및 환경 변수로부터 ProviderConfiguration 로드.
ServiceCollection 구성:
services.AddSingleton(config);
services.AddSingleton<ToolRegistry>(...) (이때 각 ITool 구현체들도 함께 등록 가능)
services.AddTransient<ILlmProvider>(sp => ...) (팩토리 패턴 적용)
ServiceProvider 빌드.
애플리케이션 실행: var app = sp.GetRequiredService<IAgentApp>(); await app.RunAsync();
기대 효과
테스트 용이성: ILlmProvider나 IPermissionEnforcer를 Mock으로 교체하여 CliToolLoopRunner의 로직만 집중적으로 유닛 테스트할 수 있습니다.
유지보수성: Program.cs의 코드가 수백 줄에서 수십 줄로 줄어들며, 각 기능의 책임이 명확해집니다.
확장성: 새로운 도구(Tool)나 새로운 LLM 제공자를 추가할 때, 컨테이너 등록 코드만 수정하면 되므로 결합도가 낮아집니다.