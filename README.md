# AgentQ

AgentQ is a Windows desktop coding-agent runtime. The Desktop app is the supported product; it combines model reasoning with deterministic, approval-aware local execution.

## Status

The project is past the prototype stage.

- Windows WPF desktop app is the supported product
- Anthropic and OpenAI-compatible providers exist
- tool execution and permission flow exist
- session/config persistence exist
- an internal CLI smoke/debug host remains for provider streaming and tool-loop contract checks

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

## Start Desktop

Start the supported Windows desktop app:

```powershell
dotnet run --project .\csharp\AgentQ.Desktop
```

On first desktop launch, fill in the Settings panel and click Save before sending a message.

The legacy CLI is not a supported user product and is not included in official releases. It remains in the repository as an internal, experimental smoke/debug host; maintainers can use [docs/internal-cli-smoke.md](docs/internal-cli-smoke.md).

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

Repeatable desktop demo flows are archived in [docs/archive/demo-scenarios.md](docs/archive/demo-scenarios.md). They cover a C# bug fix with verification, a React/TypeScript feature change with project-aware search, and Unity project analysis with visual/game evidence.

## Desktop Beta Workflow And Limits

The supported desktop beta workflow is:

```text
open project -> analyze -> ask -> edit -> verify -> review changes -> inspect Git -> commit
```

Use the Project panel to confirm project type, key files, commands, symbols, dependencies, and memory. Use Evidence and Plan to see why context was selected, including file/search/tool evidence and visual attachments. Use Verify cards and Change preview before committing generated edits.

Current beta limitations:

- Windows desktop is WPF-based and targets `net10.0-windows`.
- The installer and desktop executable are not code-signed yet.
- Visual evidence is covered by automated tests, but release QA should still include a manual image/video attachment smoke test.
- Unity verification may require manual Unity Editor or batchmode checks unless the target project provides command-line tests.
- Broader MCP hardening, cross-platform desktop support, and release signing are later roadmap items.

Release QA details are archived in [docs/archive/release-readiness.md](docs/archive/release-readiness.md).

## Search Retry

When a text or file search returns no results, AgentQ can automatically retry with broader variants before handing the result back to the model. For example, a failed `grep_search` can retry with a case-insensitive pattern, and a failed `glob_search` can retry with a recursive path pattern. Retry attempts are recorded in the Evidence tab.

## Embedding and RAG Design

AgentQ's planned semantic retrieval system is archived in [docs/archive/embedding-rag-design.md](docs/archive/embedding-rag-design.md). The design keeps keyword search, project map signals, evidence, confidence scoring, and future embedding search working together as a hybrid retrieval system.

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

Before tagging or publishing a beta, run the release readiness preflight:

```powershell
.\release-readiness.ps1
```

The scripts no longer force `DOTNET_CLI_HOME`. They use the current local dotnet environment unless you explicitly set that variable yourself.

For the full solution, including desktop service tests:

```powershell
dotnet test .\csharp\AgentQ.sln
```

The test project targets `net10.0-windows` so it can cover desktop services without launching the WPF UI.

## Mock Service

The repository includes `AgentQ.MockService` for provider and internal Runtime-contract testing.

Run it with:

```powershell
dotnet run --project .\csharp\AgentQ.MockService
```

The mock service listens on `http://localhost:18080/` by default. Override the listener prefix with `AGENTQ_MOCK_URL` when running in containers or another host environment.

## Docker

The Windows desktop app is not containerized because it is a WPF application. Docker support targets the mock provider service used by provider and internal Runtime-contract workflows.

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
- publishes the Windows desktop app as a self-contained `win-x64` package
- uploads desktop packages and test results as artifacts
- builds the mock service Docker image on `ubuntu-latest`
- starts the mock service container and runs the internal CLI Runtime-contract smoke against it

## Release Artifacts

Tag pushes matching `v*` run `.github/workflows/release.yml`.

Example:

```powershell
git tag v0.1.0-beta.8
git push origin v0.1.0-beta.8
```

The release workflow builds and tests the solution, publishes the Windows desktop app, builds an Inno Setup installer, and creates a draft GitHub Release with:

- `AgentQ-Setup-<tag>.exe`
- `AgentQ.Desktop-win-x64-<tag>.zip`
- matching `.sha256` checksum files

For most Windows users, download and run `AgentQ-Setup-<tag>.exe`. It installs AgentQ under `%LOCALAPPDATA%\Programs\AgentQ`, creates Start Menu shortcuts, offers an optional desktop shortcut, and includes an uninstaller.

The ZIP artifact is a portable build for quick testing without installation. Extract it and run `AgentQ.Desktop.exe`.

Release drafts remain private to repository collaborators until someone opens the draft and clicks **Publish release**. Beta releases should stay marked as **Pre-release**.

The installer and desktop executable are not code-signed yet. Windows SmartScreen or Microsoft Edge may warn that the file is not commonly downloaded or has an unknown publisher. For internal beta testing, choose **Keep** or **More info -> Run anyway** only if you trust the release source.

Beta feedback is welcome. Please try the installer or portable ZIP and share bugs, rough edges, or suggestions through GitHub Issues, especially around installation, provider setup, model selection, optional embeddings, and desktop workflow stability.

Before publishing a beta release, use [docs/archive/release-readiness.md](docs/archive/release-readiness.md) for the Desktop installer, portable ZIP, checksum, smoke-test, and release-notes checklist.

## OpenCode Go

OpenCode Go can be configured in Desktop Settings using the equivalent provider values:

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

- Latest local validation: `.\build.ps1` passed
- Latest local validation: `.\test.ps1`: `267` tests passed
- Next beta target: `v0.1.0-beta.8`
- Expected release artifacts: `AgentQ-Setup-v0.1.0-beta.8.exe`, `AgentQ.Desktop-win-x64-v0.1.0-beta.8.zip`, and their `.sha256` checksum files

The repository can still be validated on a normal local machine or CI runner as the primary source of truth for repeatable build and test confidence.

## Roadmap

Near-term priorities:

- publish and QA `v0.1.0-beta.8`
- harden MCP bridge policy, observability, and server lifecycle handling
- add a visible telemetry/replay dashboard for local eval review
- improve model routing from recommendation-only to user-approved switching
- expand language workers and dependency graph support
- continue improving release trust, including code signing

See [docs/archive/language-worker-architecture.md](docs/archive/language-worker-architecture.md) for the planned C# core plus language worker design.

## Current Priority

1. run the release readiness checklist in [docs/archive/release-readiness.md](docs/archive/release-readiness.md)
2. publish `v0.1.0-beta.8` release artifacts
3. QA installer, portable ZIP, provider setup, optional embeddings, memory, verification, snapshots, telemetry, and replay
4. add code signing before broader Windows distribution
5. expand MCP, routing, replay, and language-worker coverage
