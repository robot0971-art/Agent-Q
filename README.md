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
```

Current non-interactive behavior:

- tools that require permission are denied automatically unless `--yes` is provided
- `--allow-tool <name>` can be repeated to approve only specific tools in non-interactive mode
- `--prompt`, `--stdin`, and `--input` are mutually exclusive
- missing model/API configuration exits immediately instead of opening the REPL
- `--json` emits a machine-readable result envelope with `success`, `exitCode`, `terminationReason`, `finalText`, `allowedTools`, `deniedTools`, `toolErrors`, and `toolOutputs`

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
- `/base-url <url>`
- `/timeout <seconds>`
- `/save <path>`
- `/load <path>`
- `/config save`
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

## Validation Snapshot

Current wrapper-script validation passed in this environment:

- `.\test.cmd`: `44` non-integration tests passed
- `.\test.integration.cmd`: `4` integration tests passed

The repository can still be validated on a normal local machine or CI runner as the primary source of truth for repeatable build and test confidence.

## Current Priority

1. documentation synchronization and final cleanup
2. optional provider integration expansion beyond current local coverage
3. optional CLI rendering and UX polish
