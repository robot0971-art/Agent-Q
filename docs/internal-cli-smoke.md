# Internal CLI smoke/debug host

`AgentQ.Cli` is internal, experimental, and unsupported. AgentQ Desktop is the only supported user product and the only official download/release path.

Do not direct end users to install `AgentQ.Tool`, and do not publish the package or attach it to a GitHub Release. The project stays in the solution so maintainers can diagnose provider streaming, tool-loop behavior, and shared Runtime contracts. It may be reconsidered for headless automation or a remote-worker product only after an explicit product decision.

## Supported internal use

Run the host from source rather than treating it as a product installation:

```powershell
dotnet run --project .\csharp\AgentQ.Cli -- --prompt "hello" --json
```

Set `AGENTQ_PROVIDER`, `AGENTQ_MODEL`, `AGENTQ_API_KEY`, and optionally `AGENTQ_BASE_URL` in the current process environment. The warning printed to stderr is intentional; JSON remains on stdout for contract assertions.

For the mock provider/tool-loop contract smoke:

```powershell
$env:AGENTQ_PROVIDER="anthropic"
$env:AGENTQ_MODEL="demo-model"
$env:AGENTQ_API_KEY="demo-key"
$env:AGENTQ_BASE_URL="http://localhost:18080"
dotnet run --project .\csharp\AgentQ.Cli -- --prompt "PARITY_SCENARIO:plugin_tool_roundtrip" --allow-tool plugin_echo --json
```

CI runs this as an internal Runtime-contract smoke, not as product parity or a distribution check.

## Temporary local tool package

`AgentQ.Cli` remains packable only for controlled maintainer diagnostics. A locally packed `AgentQ.Tool` is not an official package, has no support commitment, and must not be uploaded to a public feed, CI artifact, or release.

```powershell
dotnet pack .\csharp\AgentQ.Cli\AgentQ.Cli.csproj -c Release
dotnet tool install --global --add-source .\artifacts\packages AgentQ.Tool
```

Use `dotnet tool update --global --add-source .\artifacts\packages AgentQ.Tool` only to refresh that local maintainer installation.

## Runtime extraction candidates (do not move yet)

The following duplicated orchestration belongs in `AgentQ.Runtime` once its contract is agreed by the integration session. This maintenance change deliberately does not move any of it:

- provider request assembly, streaming, and conversation/tool-result progression (`CliToolLoopRunner`, `StreamingProcessor`, `ConversationTurnBuilder`; compare `DesktopAgentService` and `DesktopPromptAssemblyService`)
- tool-loop limits, malformed input, permission-result flow, and termination reporting (`CliToolLoopRunner`, `ToolExecutor`, `CliNonInteractiveRunner`; compare `DesktopAgentService.ExecuteToolsAsync`)
- conversation compaction/history lifecycle (`AgentQ.Cli.ConversationCompactor`; compare `AgentQ.Desktop.Services.ConversationCompactor`)
- provider/config resolution and prompt construction (`CliConfigurationLoader`, `CliProviderResolver`, `SystemPromptManager`; compare Desktop services)

Until then, keep Core, Providers, Tools, MockService, and Desktop contracts product-neutral. CLI changes should remain thin-host diagnostics only.
