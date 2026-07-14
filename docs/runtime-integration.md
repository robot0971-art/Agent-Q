# AgentQ.Runtime integration contract

`AgentQ.Runtime` is a WPF-free `net10.0` policy and orchestration-contract project. It must not access files, processes, provider SDKs, WPF dispatchers, dialogs, or workspace configuration directly.

## Contract ownership

| Contract | Owner | Lifetime | Host adapter responsibility |
| --- | --- | --- | --- |
| `IAgentRunStateMachine`, `IAgentRunCoordinator` | Runtime | coordinator/session per run | persist transitions and map them to UI timeline events |
| `IIntentRoutingPipeline` | Runtime | singleton policy | supply current-request and safety facts; do not execute from classification alone |
| `ITaskContractFactory` | Runtime | singleton policy | translate Desktop/CLI intent to target/capability/verification scope; preserve scaffold external plan IDs/hashes |
| `IDeterministicActionDispatcher` | Runtime | per run | register Desktop handlers only after existing approval, workspace, plan-hash, and path checks remain in force |
| `IModelToolLoop` | Runtime | singleton policy | provide provider/tool ports; apply permission and replay/snapshot policies outside the loop |
| `IRunEvidenceCollector` | Runtime | per run | persist immutable evidence to the run journal and include artifact references |
| `ICompletionEvaluator`, `IFinalAnswerConsistencyGuard` | Runtime | singleton policy | feed only contract-scoped evidence; never use a model answer as evidence |
| `IRepairCoordinator` | Runtime | singleton policy | stop on scope expansion and request approval rather than widening authority |

## Integration order

1. Keep Desktop deterministic services authoritative for scaffold, local server, snapshot, verification, and permissions.
2. Build a Runtime task contract beside the legacy Desktop `TaskContract`; do not replace `planId`/`planHash` validation.
3. Start a run-scoped coordinator and evidence collector at the beginning of a Desktop/CLI run.
4. Route intent through the Desktop adapter. During migration, the legacy router remains the detailed translator, while Runtime is a non-bypassable narrowing gate: it may convert an executable legacy result to conversation/clarification, but may never create or broaden an executable result.
5. Register deterministic action handlers behind existing Desktop services. A handler may not bypass workspace boundary, approval, authorization, or verification checks.
6. Adapt provider/tool loop into `IModelToolLoopPort`; retain Desktop permission checks, snapshots, replay, read-only loop guards, and tool truncation.
7. Evaluate completion from evidence; the Desktop completion adapter applies the Runtime evidence floor in addition to (never instead of) the legacy completion checker. Pass the result through the final-answer consistency guard before reporting success.
8. Persist state transitions, approvals, evidence, verification, and result under `.agentq/runs/<runId>/` before recovery/resume is enabled.

## Host-specific notes

- Desktop is the production composition root. Its current adapter seams are `IDesktopIntentRoutingAdapter`, `IDesktopTaskContractCompletionAdapter`, and `IDesktopProviderSessionFactory`.
- CLI is an internal smoke/debug host. It may reference Runtime contracts but must not duplicate orchestration or become a user-facing execution path.
- Scaffold retains ownership of `planId`, `planHash`, manifest, authorization, overwrite policy, executor, and verification command allowlist.
- Tests should instantiate Runtime directly for state/contract policy and use Desktop adapters only for characterization of legacy behavior.

## Lifetime rules

- Application singleton: stateless evaluators, factories, policy tables, catalogs.
- Workspace scope: project memory, index, snapshot repository, workspace configuration, long-lived local-server metadata.
- Run scope: coordinator, evidence collector, repair history, cancellation token, provider/tool loop context.
- Explicit lifecycle owner: child process, browser session, local server process, file mutation transaction.

No adapter may convert a missing approval, missing evidence, failed verification, cancellation, or guard stop into `Completed`.
