# ADR 0001: Extract the AgentQ Runtime outside WPF

- Status: Accepted
- Date: 2026-07-11

## Context

`DesktopAgentService` is a 6,701-line service that currently owns turn understanding, provider sessions, context assembly, deterministic dispatch, the model tool loop, completion guards, repair, evidence, snapshots, and final-answer correction. The Desktop composition root registers most mutable services as application singletons. This makes orchestration difficult to test without WPF and makes integrations from the CLI, scaffold, and test workstreams depend on Desktop implementation details.

## Decision

Create `AgentQ.Runtime` as a `net10.0` project with no WPF or Desktop reference. Runtime owns orchestration contracts and policies; Desktop remains the WPF composition root and supplies adapters for UI, permissions, files, processes, providers, and verification.

The first extracted responsibility is the run state contract and transition policy. State transitions are explicit records containing run, contract, reason, policy, evidence, and time metadata. Execution states require a contract. A verification path cannot become `Completed` without evidence. Conversation-only runs may complete without mutation evidence.

The Runtime contract envelope is versioned and immutable. It includes workspace identity, semantic intent, goal, target paths, capabilities, expected mutations, verification requirements, completion conditions, expiry, and a SHA-256 hash over canonical scope fields. Existing scaffold `planId`/`planHash` are retained as external identities during migration; Runtime does not generate an authorization or weaken the scaffold executor's validation.

Runtime evidence is collected per run through an append-only in-memory port with mutation, command, verification, snapshot, approval, recovery, and final-answer categories. A later run-journal adapter persists those records below `.agentq/runs/<runId>/`; the collector must remain run scoped so evidence cannot leak across workspaces or runs.

Completion evaluation consumes only a Runtime contract and its contract-scoped evidence. It never accepts a model response as completion evidence. Every declared completion condition must match the required evidence before a verification path can transition to `Completed`; missing conditions remain explicit in the evaluation result for repair or blocker reporting.

The Runtime model-tool loop is bounded orchestration over provider and tool ports. It enforces cancellation and a maximum number of model/tool steps but does not grant tool authority or decide completion. Deterministic dispatch validates contract expiry, capability, target, and handler registration before an adapter can act. Repair is separately bounded by attempt count and failure fingerprint, and scope expansion returns an approval blocker rather than silently widening authority.

The target extraction order is:

1. `AgentRunCoordinator`
2. `IntentRoutingPipeline`
3. `TaskContractFactory`
4. `DeterministicActionDispatcher`
5. `ModelToolLoop`
6. `CompletionEvaluator`
7. `RepairCoordinator`
8. `RunEvidenceCollector`
9. `FinalAnswerConsistencyGuard`
10. `ProviderSessionFactory`

Each extraction must first preserve behavior with characterization tests. `DesktopAgentService` remains a facade during migration; no large rewrite or parallel replacement loop is permitted.

The first Desktop facade seam is `IDesktopIntentRoutingAdapter`. It owns the bridge from legacy `UserTurnUnderstanding`/`TurnIntentClassification` records to the existing `TaskContract` translator while depending on the portable Runtime routing port. Until the Runtime request model represents every legacy safety distinction, this adapter returns the legacy router's result unchanged; its equivalence tests are the gate for moving policy ownership.

`DesktopProviderSessionFactory` owns provider-specific HTTP client setup, provider selection, and retry presentation. `DesktopAgentService` retains its public provider-factory interface as a facade because existing callers depend on it, but it no longer constructs provider concrete types directly. Model-name resolution stays in the facade temporarily because prompt/classification paths share it outside provider creation.

## Dependency and lifetime boundaries

| Responsibility | Current owner | Target owner | Lifetime |
| --- | --- | --- | --- |
| Turn understanding and safety merge | `DesktopAgentService`, `LlmFirstIntentRouter` | Runtime intent pipeline | Run |
| Task normalization and completion rules | Desktop `TaskContract` static helpers | Runtime contract factory/evaluator | Run, immutable contract |
| Tool/model iteration | `DesktopAgentService` | Runtime model tool loop | Run |
| Scaffold and local-server execution | Desktop deterministic services | Execution/Scaffolding adapters behind Runtime ports | Explicit process/workspace owner |
| Repair selection and limits | `DesktopAgentService`, verification services | Runtime repair coordinator | Run |
| Replay, mutation, verification evidence | Desktop services | Runtime evidence port and collector | Run journal |
| Provider HTTP/session creation | `DesktopAgentService` | Provider session factory port | Run/session |
| Settings and catalogs | Desktop configuration services | Composition root adapters | Application singleton |
| Workspace index, memory, snapshots | Desktop services | Workspace-scoped adapters | Workspace |
| UI timeline, dialogs, ViewModels | WPF Desktop | WPF Desktop only | Window/application |

## Common integration contract

- Desktop is the primary host and will compose Runtime ports.
- CLI may call Runtime only as an internal smoke/debug thin host; it must not duplicate orchestration.
- Scaffold implementations keep ownership of `planId`, `planHash`, authorization, overwrite, and deterministic executor behavior. Runtime coordinates those contracts but does not weaken them.
- Test infrastructure may instantiate Runtime directly without WPF.
- Runtime does not directly access files, processes, WPF dispatchers, provider SDKs, or permission dialogs.

## Alternatives considered

- Rewrite `DesktopAgentService` in one change: rejected because it would remove characterization coverage and make safety regressions hard to localize.
- Move orchestration into `AgentQ.Core`: rejected because Core should remain common models/contracts rather than own execution policy.
- Keep orchestration in Desktop and add interfaces only: rejected because CLI and tests would still depend on a Windows/WPF target.

## Consequences

Runtime contracts can be tested on plain `net10.0`, and other sessions gain a stable integration direction. During migration there are temporarily two state vocabularies: the legacy Desktop presentation state and the Runtime source-of-truth state. The next coordinator/adapter extraction must map Runtime transitions to UI events, after which UI status must no longer independently infer completion.

## Migration and rollback

Migrate one responsibility at a time behind an interface, retain the facade signature, run focused characterization tests, then run the Desktop build. A responsibility can be rolled back by removing its facade delegation while retaining the Runtime contract; existing deterministic safety paths remain unchanged throughout.
