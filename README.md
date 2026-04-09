# AgentQ

AgentQ is a C# CLI coding assistant with tool-use support, provider abstraction, and a mock-service-backed test workflow.

## Status

The project is past the prototype stage.

- core CLI loop exists
- Anthropic and OpenAI-compatible providers exist
- tool execution and permission flow exist
- session/config persistence exist
- mock parity infrastructure exists

Current work is focused on stabilization, regression coverage, and documentation sync.

## Requirements

- Windows
- .NET 10 SDK

## Project Layout

```text
csharp/
|- AgentQ.Api
|- AgentQ.Core
|- AgentQ.Providers.Anthropic
|- AgentQ.Providers.OpenAi
|- AgentQ.Tools
|- AgentQ.Cli
|- AgentQ.MockService
`- AgentQ.Tests
```

## Main Features

- REPL-based coding assistant CLI
- tool-use conversation loop
- provider switching between `anthropic` and `openai`
- file and shell tools
- workspace-root path restriction
- permission-gated tool execution
- session save/load
- config persistence
- streamed tool-call assembly
- retry wrapper for transient provider failures

## Built-in Tools

- `bash`
- `read_file`
- `write_file`
- `edit_file`
- `grep_search`
- `glob_search`
- `plugin_echo`

## Environment Variables

- `AGENTQ_PROVIDER`
- `AGENTQ_MODEL`
- `AGENTQ_API_KEY`
- `AGENTQ_BASE_URL`
- `AGENTQ_TIMEOUT`
- `AGENTQ_WORKSPACE_ROOT`

## Running

Start the CLI:

```powershell
dotnet run --project .\csharp\AgentQ.Cli
```

Install it as a .NET global tool:

```powershell
dotnet pack .\csharp\AgentQ.Cli\AgentQ.Cli.csproj -c Release
dotnet tool install --global --add-source .\artifacts\packages AgentQ.Tool
```

After installation, run it from any terminal with:

```powershell
agentq
```

To refresh an existing installation after rebuilding:

```powershell
dotnet tool update --global --add-source .\artifacts\packages AgentQ.Tool
```

Local development packs now use a timestamped numeric package version, so each `dotnet pack ... -c Release` produces an upgradable tool package. That means `dotnet tool update --global ...` refreshes the global `agentq` command in place instead of silently staying on an older build.

Recommended local workflow:

```powershell
dotnet pack .\csharp\AgentQ.Cli\AgentQ.Cli.csproj -c Release
dotnet tool update --global --add-source .\artifacts\packages AgentQ.Tool
agentq --prompt "hello" --json
```

If the tool has not been installed before, run `dotnet tool install --global --add-source .\artifacts\packages AgentQ.Tool` once, then use `dotnet tool update --global ...` for later rebuilds.

## Automation Mode

AgentQ now supports one-shot non-interactive execution in addition to the interactive REPL.

Examples:

```powershell
agentq --prompt "Summarize README.md"
Get-Content .\prompt.txt | agentq --stdin
agentq --input .\prompt.txt
agentq --prompt "Summarize README.md" --json
agentq --prompt "List files" --yes
agentq --prompt "Read README.md" --allow-tool read_file
agentq --prompt "Read README.md" --allow-tool read_file --deny-tool bash
```

Current non-interactive behavior:

- tools that require permission are denied automatically unless `--yes` is provided
- `--allow-tool <name>` can be repeated to approve only specific tools in non-interactive mode
- `--deny-tool <name>` can be repeated to explicitly block tools and overrides allow rules
- `--prompt`, `--stdin`, and `--input` are mutually exclusive
- missing model/API configuration exits immediately instead of opening the REPL
- `--json` emits a machine-readable result envelope with `success`, `exitCode`, `terminationReason`, `finalText`, `allowedTools`, `configuredDeniedTools`, `deniedTools`, `executedTools`, `toolErrors`, and structured `toolOutputs`

Non-interactive mode reads configuration from the current process environment. If `agentq --prompt ... --json` works in `cmd.exe` but fails in PowerShell, or the reverse, check whether `AGENTQ_MODEL` and `AGENTQ_API_KEY` are only set in one shell session.

`toolOutputs` items now include:

- `toolName`
- `isError`
- `raw`
- `isJson`
- `parsed`

Example:

```json
{
  "toolName": "read_file",
  "isError": false,
  "raw": "{\"content\":\"hello\"}",
  "isJson": true,
  "parsed": {
    "content": "hello"
  }
}
```

Additional JSON metadata includes:

- `provider`
- `model`
- `baseUrl`
- `permissionPolicy`

## Smoke Test

Use this sequence after rebuilding the CLI package:

```powershell
dotnet pack .\csharp\AgentQ.Cli\AgentQ.Cli.csproj -c Release
dotnet tool update --global --add-source .\artifacts\packages AgentQ.Tool
agentq --prompt "hello" --json
```

Expected success shape:

```json
{
  "success": true,
  "exitCode": 0,
  "terminationReason": "completed",
  "finalText": "Hello! How can I assist you today?"
}
```

Set the required environment variables in the same shell before running the smoke test.

PowerShell:

```powershell
$env:AGENTQ_MODEL="your-model"
$env:AGENTQ_API_KEY="your-key"
agentq --prompt "hello" --json
```

CMD:

```cmd
set AGENTQ_MODEL=your-model
set AGENTQ_API_KEY=your-key
agentq --prompt "hello" --json
```

Startup currently renders a centered pastel-purple `Q` mark before the status panel.

Useful slash commands:

- `/help`
- `/clear`
- `/history`
- `/compact`
- `/tools`
- `/status`
- `/provider <name>`
- `/model <name>`
- `/api-key <key>`
- `/base-url <url>`
- `/timeout <seconds>`
- `/config save`
- `/config show`
- `/config path`
- `/config clear`
- `/save <path>`
- `/load <path>`
- `/run <tool> <json>`

## Build and Test

Use the repository wrapper scripts as the default entrypoints:

```powershell
.\build.cmd
.\test.cmd
.\test.integration.cmd
```

PowerShell variants also exist:

```powershell
.\build.ps1
.\test.ps1
.\test.integration.ps1
```

`test.cmd` and `test.ps1` exclude integration tests by default.
`test.integration.cmd` and `test.integration.ps1` run only the integration test layer.

The scripts no longer force `DOTNET_CLI_HOME`. They use the current local dotnet environment unless you explicitly set that variable yourself.

## Mock Service

The repository includes `AgentQ.MockService` for parity-style provider testing.

Run it with:

```powershell
dotnet run --project .\csharp\AgentQ.MockService
```

## Alibaba Cloud Model Studio

Alibaba Cloud Model Studio exposes an OpenAI-compatible Chat Completions API, so it can be used through AgentQ's `openai` provider by setting the correct `base-url`, model, and API key.

Example for the Singapore international endpoint:

```powershell
$env:AGENTQ_PROVIDER="openai"
$env:AGENTQ_BASE_URL="https://dashscope-intl.aliyuncs.com/compatible-mode/v1"
$env:AGENTQ_MODEL="qwen-plus"
$env:AGENTQ_API_KEY="<your_dashscope_api_key>"

agentq --prompt "Summarize README.md" --json
```

Notes:

- Use the endpoint that matches your Alibaba Cloud region and key.
- The browser console URL is not the API endpoint. Use the `dashscope.../compatible-mode/v1` base URL from the official docs.
- The OpenAI-compatible provider is now covered by local tests that verify the outgoing `chat/completions` request shape, including `Authorization`, `messages`, `tools`, `tool_calls`, `tool_call_id`, and `stream=true` handling.
- Third-party compatibility should still be validated against your target model and region because provider-side behavior can differ even when the request contract matches.

## Validation Snapshot

Current wrapper-script validation passed in this environment:

- `.\test.cmd`: `54` non-integration tests passed
- `dotnet test .\csharp\AgentQ.Tests\AgentQ.Tests.csproj --filter "FullyQualifiedName~OpenAiProviderTests|FullyQualifiedName~ProviderUnitTests"`: `10` provider-focused tests passed

The repository can still be validated on a normal local machine or CI runner as the primary source of truth for repeatable build and test confidence.

## Current Priority

1. documentation synchronization and final cleanup
2. optional provider integration expansion beyond current local coverage
3. optional CLI rendering and UX polish
