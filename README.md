# AgentQ

AgentQ is a C# coding assistant with a CLI, a Windows desktop app, tool-use support, provider abstraction, and a mock-service-backed test workflow.

## Status

The project is past the prototype stage.

- core CLI loop exists
- WPF desktop app exists
- Anthropic and OpenAI-compatible providers exist
- tool execution and permission flow exist
- session/config persistence exist
- mock parity infrastructure exists

Current work is focused on desktop stabilization, regression coverage, and documentation sync.

## Requirements

- Windows
- .NET 10 SDK

## Download and Install

Download the latest beta from the GitHub Releases page:

- [AgentQ Releases](https://github.com/robot0971-art/Agent-Q/releases)

For most Windows users, choose `AgentQ-Setup-<version>.exe`. The ZIP file is a portable build for quick testing without installation.

AgentQ is currently a beta app and the Windows installer is not code-signed yet. Microsoft Edge or Windows SmartScreen may show warnings such as "not commonly downloaded" or "unknown publisher." Only run installers downloaded from the official GitHub Releases page. If you trust the source, choose **Keep**, then **More info -> Run anyway**.

## Project Layout

```text
csharp/
|- AgentQ.Api
|- AgentQ.Core
|- AgentQ.Providers.Anthropic
|- AgentQ.Providers.OpenAi
|- AgentQ.Tools
|- AgentQ.Cli
|- AgentQ.Desktop
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
- Windows desktop chat UI
- desktop workspace analysis, Git diff/status panels, checkpoints, session summaries, and verification result cards
- project memory files and approved learning candidates for repeatable desktop runs
- hybrid codebase search across symbols, keyword search, project map, and optional embeddings
- TypeScript/JavaScript and Python language-worker analysis for project maps, symbols, routes, scripts, and package metadata
- focused verification guardrails for C#, TypeScript/Node, Python, and Docker Compose changes
- desktop HITL approvals with file/command previews, Stop handling, snapshots, rollback, telemetry, model routing recommendations, MCP server config, and tool replay logs

## Built-in Tools

- `bash`
- `read_file`
- `write_file`
- `edit_file`
- `grep_search`
- `glob_search`
- `semantic_search`
- `symbol_search`
- `hybrid_search`
- `plugin_echo`

## Environment Variables

- `AGENTQ_PROVIDER`
- `AGENTQ_MODEL`
- `AGENTQ_API_KEY`
- `AGENTQ_BASE_URL`
- `AGENTQ_TIMEOUT`
- `AGENTQ_CONFIG_HOME`
- `AGENTQ_WORKSPACE_ROOT`
- `OPENCODE_GO_API_KEY`
- `OPENCODE_GO_BASE_URL`
- `OPENCODE_GO_MODEL`

`AGENTQ_CONFIG_HOME` is optional. When set, AgentQ stores configuration in `<AGENTQ_CONFIG_HOME>\.agentq\config.json`; otherwise it uses the current user's profile directory. This is mainly useful for tests and isolated local runs.

## Running

Start the CLI:

```powershell
dotnet run --project .\csharp\AgentQ.Cli
```

On first interactive launch, run `/setup` when prompted. It walks through provider, model, base URL, and API key, then offers to save the configuration to `<user-profile>\.agentq\config.json`.

Start the Windows desktop app:

```powershell
dotnet run --project .\csharp\AgentQ.Desktop
```

On first desktop launch, fill in the Settings panel and click Save before sending a message.

Build the desktop app without launching it:

```powershell
dotnet build .\csharp\AgentQ.Desktop\AgentQ.Desktop.csproj
```

## Project Memory

AgentQ can read project memory from `.agentq/memory.shared.json` and `.agentq/memory.local.json` in the selected workspace. Shared memory is intended for team-safe rules and verification commands that can be committed; local memory is ignored by Git and is meant for private notes, preferences, and approved lessons.

After a desktop run, the Memory panel may show learning candidates. These are only suggestions. AgentQ writes a lesson to `.agentq/memory.local.json` only after you approve it with Save lesson.

Memory entries can be disabled with `enabled: false` or retired with `expiresAt`. AgentQ skips expired, disabled, stale, low-confidence, overly long, sensitive, or dangerous entries before adding project memory to the model context. For desktop runs, learned lessons are ranked against the current request so the most relevant approved memory is shown to the model first. Matching local lessons are marked with `lastUsedAt`; shared memory is read but not rewritten automatically. Approved duplicate local lessons are merged by title/content so repeated learning does not bloat the memory file. The desktop Memory panel can refresh, disable, or delete saved local lessons.

Example:

```json
{
  "version": 1,
  "workspaceRules": ["Run tests before committing desktop changes."],
  "lessons": [
    {
      "id": "desktop-test-lock",
      "title": "Close desktop before tests",
      "content": "AgentQ.Desktop.exe can lock build outputs during dotnet test.",
      "tags": ["desktop", "test"],
      "confidence": 0.9,
      "source": "approved learning candidate",
      "enabled": true
    }
  ],
  "preferences": [
    { "key": "language", "value": "Korean", "enabled": true }
  ],
  "checks": [
    { "name": "tests", "command": "dotnet test .\\csharp\\AgentQ.sln -c Release", "when": "before_push", "enabled": true }
  ]
}
```

## Evidence Trail

The desktop Evidence tab records evidence-oriented events instead of exposing hidden model reasoning. It can show when project memory was used, which files or patterns were searched, which commands ran, which files changed, and which verification plans were proposed. File and search evidence also includes a short reason when the path maps to a known project role such as UI, API, tests, configuration, assets, database, or domain logic. At the end of each run, AgentQ adds a confidence event based on observable signals such as tool evidence, project memory matches, file changes, and whether build or test verification ran.

## Project Map

The desktop Project panel builds a lightweight project map during workspace analysis. It detects common folder roles such as UI, API, database, domain logic, tests, assets, configuration, and Unity project folders, then lists key files such as `README.md`, `package.json`, solution files, Docker files, and `.agentq` project memory/config files.

## Demo Scenarios

Repeatable desktop demo flows are documented in [docs/demo-scenarios.md](docs/demo-scenarios.md). They cover a C# bug fix with verification, a React/TypeScript feature change with project-aware search, and Unity project analysis with visual/game evidence.

## Search Retry

When a text or file search returns no results, AgentQ can automatically retry with broader variants before handing the result back to the model. For example, a failed `grep_search` can retry with a case-insensitive pattern, and a failed `glob_search` can retry with a recursive path pattern. Retry attempts are recorded in the Evidence tab.

## Embedding and RAG Design

AgentQ's planned semantic retrieval system is documented in [docs/embedding-rag-design.md](docs/embedding-rag-design.md). The design keeps keyword search, project map signals, evidence, confidence scoring, and future embedding search working together as a hybrid retrieval system.

The desktop app includes an initial `semantic_search` tool for OpenAI embedding indexes. It searches `.agentq/embeddings/chunks.jsonl` by cosine similarity after an embedding vector index has been built. Chat provider and embedding provider settings are separate, so you can use OpenCode Go for chat while using OpenAI for embeddings. Use the Project panel's `Build embedding index` button to create the local vector index with the configured embedding provider.

Embeddings are optional. If you do not want to use embeddings or pay for embedding API calls, set `Embedding Provider` to `none` and only enter the main chat provider API key. AgentQ will still use project map, file search, keyword search, and normal tool-based context gathering.

### Embedding Usage

Embeddings are optional. Set `Embedding Provider` to `none` if you only want to use the main chat provider API key. AgentQ will continue to use Project Map, file search, keyword search, symbol search, and tool-based context gathering.

## Desktop Beta v0.1.0-beta.8

This beta bundles the current v1 desktop reliability work:

- Project Map and multi-language workspace analysis for C#, TypeScript/JavaScript, Python, Docker, FastAPI, React/Vite, SQLAlchemy, Alembic, and C++ hints
- Symbol Index, `symbol_search`, `semantic_search`, and `hybrid_search`
- optional OpenAI embeddings with keyword/project-map fallback when embeddings are disabled
- project memory, approved learning candidates, structured context bank, and recurring error history
- evidence-backed analysis responses, Project Map evidence paths, confidence signals, focused verification guardrails, and search retry
- link auto-read evidence that reports fetch success or failure instead of claiming URLs are always inaccessible
- reusable workspace analysis reports with copy/save actions
- HITL permission dialogs with file mutation and command previews
- larger file change diff preview, stable status-panel tabs, file mutation snapshots, per-change revert, local telemetry JSONL, model routing recommendations, MCP server config foundation, and tool replay logs

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

If you already saved configuration with `/setup` or `/config save`, the installed `agentq` command can use that saved config without shell-local environment variables.

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
- `/timeout <seconds>` (`0` disables the provider request timeout)
- `/max-tokens <count>` (`8192` or higher is useful for long reviews)
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

For the full solution, including desktop service tests:

```powershell
dotnet test .\csharp\AgentQ.sln
```

The test project targets `net10.0-windows` so it can cover desktop services without launching the WPF UI.

## Mock Service

The repository includes `AgentQ.MockService` for parity-style provider testing.

Run it with:

```powershell
dotnet run --project .\csharp\AgentQ.MockService
```

The mock service listens on `http://localhost:18080/` by default. Override the listener prefix with `AGENTQ_MOCK_URL` when running in containers or another host environment.

## Docker

The Windows desktop app is not containerized because it is a WPF application. Docker support currently targets the mock provider service used by CLI and provider parity workflows.

Build and run the mock service with Docker Compose:

```powershell
docker compose up --build mockservice
```

Or build the image directly:

```powershell
docker build -f .\Dockerfile.mockservice -t agentq-mockservice:local .
docker run --rm -p 18080:18080 agentq-mockservice:local
```

The container sets `AGENTQ_MOCK_URL=http://*:18080/` so the service is reachable through the published port.

## CI

GitHub Actions is configured in `.github/workflows/ci.yml`.

The CI workflow:

- restores and builds `csharp/AgentQ.sln` on `windows-latest`
- runs the full Release test suite
- packs the CLI as a .NET tool package
- publishes the Windows desktop app as a self-contained `win-x64` package
- uploads CLI packages, desktop packages, and test results as artifacts
- builds the mock service Docker image on `ubuntu-latest`
- starts the mock service container and runs a CLI JSON smoke test against it

## Release Artifacts

Tag pushes matching `v*` run `.github/workflows/release.yml`.

Example:

```powershell
git tag v0.1.0-beta.8
git push origin v0.1.0-beta.8
```

The release workflow builds and tests the solution, packs the CLI using the tag version, publishes the Windows desktop app, builds an Inno Setup installer, and creates a draft GitHub Release with:

- `AgentQ-Setup-<tag>.exe`
- `AgentQ.Tool.<version>.nupkg`
- `AgentQ.Desktop-win-x64-<tag>.zip`

For most Windows users, download and run `AgentQ-Setup-<tag>.exe`. It installs AgentQ under `%LOCALAPPDATA%\Programs\AgentQ`, creates Start Menu shortcuts, offers an optional desktop shortcut, and includes an uninstaller.

The ZIP artifact is a portable build for quick testing without installation. Extract it and run `AgentQ.Desktop.exe`.

Release drafts remain private to repository collaborators until someone opens the draft and clicks **Publish release**. Beta releases should stay marked as **Pre-release**.

The installer and desktop executable are not code-signed yet. Windows SmartScreen or Microsoft Edge may warn that the file is not commonly downloaded or has an unknown publisher. For internal beta testing, choose **Keep** or **More info -> Run anyway** only if you trust the release source.

Beta feedback is welcome. Please try the installer or portable ZIP and share bugs, rough edges, or suggestions through GitHub Issues, especially around installation, provider setup, model selection, optional embeddings, and desktop workflow stability.

## OpenCode Go

OpenCode Go can be used through AgentQ when you have an API key for one of the OpenAI-compatible Go models.

PowerShell:

```powershell
$env:OPENCODE_GO_MODEL="kimi-k2.6"
$env:OPENCODE_GO_API_KEY="<your_opencode_go_api_key>"

agentq --prompt "hello" --json
```

Equivalent generic configuration:

```powershell
$env:AGENTQ_PROVIDER="opencode-go"
$env:AGENTQ_BASE_URL="https://opencode.ai/zen/go/v1"
$env:AGENTQ_MODEL="kimi-k2.6"
$env:AGENTQ_API_KEY="<your_opencode_go_api_key>"
```

`opencode-go` uses the same OpenAI-compatible Chat Completions request path as the built-in `openai` provider. When `OPENCODE_GO_API_KEY` or `OPENCODE_GO_MODEL` is set, AgentQ defaults the base URL to `https://opencode.ai/zen/go/v1`.

Model IDs currently documented by OpenCode Go for the Chat Completions endpoint include `glm-5.1`, `glm-5`, `kimi-k2.5`, `kimi-k2.6`, `deepseek-v4-pro`, `deepseek-v4-flash`, `mimo-v2.5`, and `mimo-v2.5-pro`. MiniMax Go models use an Anthropic-style `/messages` endpoint and are not covered by this OpenAI-compatible provider alias yet.

## Validation Snapshot

Current local validation passed in this environment:

- Latest local validation: `dotnet test .\csharp\AgentQ.sln -c Release`: `213` tests passed
- Next beta target: `v0.1.0-beta.8`
- Expected release artifacts: `AgentQ-Setup-v0.1.0-beta.8.exe`, `AgentQ.Desktop-win-x64-v0.1.0-beta.8.zip`, and `AgentQ.Tool.0.1.0-beta.8.nupkg`

The repository can still be validated on a normal local machine or CI runner as the primary source of truth for repeatable build and test confidence.

## Roadmap

Near-term priorities:

- publish and QA `v0.1.0-beta.8`
- turn MCP server config into a real MCP client/tool bridge
- add a visible telemetry/replay dashboard for local eval review
- improve model routing from recommendation-only to user-approved switching
- expand language workers and dependency graph support
- continue improving release trust, including code signing

See [docs/language-worker-architecture.md](docs/language-worker-architecture.md) for the planned C# core plus language worker design.

## Current Priority

1. publish `v0.1.0-beta.8` release artifacts
2. QA installer, portable ZIP, provider setup, optional embeddings, memory, verification, snapshots, telemetry, and replay
3. add code signing before broader Windows distribution
4. expand MCP, routing, replay, and language-worker coverage
