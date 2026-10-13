# Playground V0 architecture

## M19 v0.3 Extension — 2026-10-13

The original V0 description below remains the legacy workflow. M19 adds bounded model/relationship DTOs and thin Generic/External adapters in the same host; no second Binder, solver, or transaction backend.

New routes: `GET /api/model`, `GET /api/model/relations?target=<exact-id>`, `POST /api/model/open`, `/api/construction/plan`, `/api/construction/preflight`, `/api/scalar/preview`, `/api/batch/preview`, and `/api/modern/execute`. Execution receives only a server preview ID/hash/revision/exact ordered targets/confirmation, never browser executable IR. Model intake requires an absolute Part path, supported mode, explicit confirmation, and a package root for restores.

Construction reuses the production ConstructionPlanner and GenericPartSession; new-Part execution deterministically revalidates the original strict/semantic response in the owned context, never repaired or expanded semantic input. External inspection/copy/restore, Scalar, and M18 Batch query/approval reuse their production public entries. Generic display reads the authoritative package rather than a quarantined in-flight native capture. GET polling reads cached DTOs only.

The UI distinguishes source/copy, managed/external, observed/committed/provisional/quarantined, read-only/qualified/ambiguous query, and Given/Defaulted/DerivedByRule. Native dependencies and declared design relations have separate provenance; graph display does not infer mechanical intent. Legacy and modern plans share one admission/STA and invalidate each other's approvals.

Selected Lucide v1.8.0 ISC assets are vendored locally for tool icons; CSP remains self-only. `scripts/test-milestone19.ps1 -Mode All` runs pure/mock API and headless browser qualification with 0 native creations/opens. Native smoke is not part of this automated qualification. See [M19 verification](D:/CAD-Harness0.2/docs/milestone-19-verification.md).

Local ASP.NET Core + dependency-free static HTML/CSS/JS over completed v0.2. All new production code is in `src/CadHarness.Playground`; no existing IR/Planning/State/SolidWorks semantics changed.

## Reused modules

| Action | Production entry |
|---|---|
| Planning | `CadPlanner`, shared `PlannerResponseSchema`, strict envelope, `CadProgramJson` |
| Projection | `SolidWorksPlanningRuntime.ForConstruction` / live `ForEdit`, `RuntimeCapabilityCatalog.ToPromptJson` |
| Provider | `DeepSeekPlanSource` / `OpenAiPlanSource`, strict Responses json_schema, no retry/fallback |
| Explicit preflight | `IPlanningRuntime.Preflight` → capability / DesignRelationEngine / `RelationBackend.Preflight` |
| Create | `RelationBackend.Create(context, program, AtomicStateStore)` → construction transaction and final validation |
| Edit | `TransactionalParameterBackend` + `MutationTransaction` → Binder, ChangeSet/DirtySet, incremental/full validation, rollback, atomic commit |
| State/timing | committed `AtomicStateStore.Load`, typed `CadState`, `ExecutionTelemetry` |

No second solver, schema, Binder, validator or mutation coordinator is implemented. Edit current/requested display is a typed projection of plan and existing binding. ChangeSet/DirtySet appear only when the native transaction actually returns them.

## Endpoints

All API responses: `{ success, stage, failureCode, message, details }`. DTOs reject unknown fields. JSON/body errors are uniform. Request cap 64 KiB; intent remains 8192 UTF-8 bytes; provider bounds stay in the existing adapter.

| Method / path | Responsibility |
|---|---|
| GET /api/status, /api/solidworks/status | cached status/current plan/last result, no COM polling |
| GET /api/capabilities | pure construction projection |
| POST /api/llm/configure | provider/key/endpoint/model/timeout/max tokens/env choice, memory only |
| POST /api/llm/test, /api/llm/clear | one provider-only connectivity call / clear credential and plan |
| POST /api/plan, /api/edit/plan | intent + edit revision → existing CadPlanner |
| POST /api/preflight, /api/edit/preflight | current planId + edit revision → pure aggregate preflight → new preflightId |
| POST /api/solidworks/connect | explicit attach/start |
| POST /api/execute, /api/edit/execute | planId/preflightId/confirmed/edit revision; one-use approval |
| POST /api/part/close | owned document close/discard and active-document restoration |
| GET /api/state, /api/session/events | committed state DTO / bounded 500-event log |
| POST /api/session/export | sanitized current session report |

Only 127.0.0.1 bound; loopback IP Host and exact same-origin URL required. No CORS/public exposure/remote assets. CSP self-only; no-store responses; no body/header/request logging. Not a remote multi-user service.

## Authoritative session and plan lifecycle

`PlaygroundSession` holds immutable StoredPlan references, internal planId/preflightId, cached native status/state and sanitized result/event projections. Execute accepts no browser IR or native document name. New planning attempts (even failed ones), configuration changes and close invalidate older plans. Explicit preflight clears older approval, issuing a new token only on success.

A nonblocking semaphore admits planning/configuration/preflight/connect/native/edit/close/export. Concurrent requests receive SESSION_BUSY; double native execute is never queued. Execute verifies mode/tokens/confirmation/current revision/lifecycle, consumes approval before dispatch and calls one native action. UI separately requires a second confirmation-modal click. No auto planning→execution chain.

Provider cancellation propagates; the Playground wrapper distinguishes a provider deadline from user cancellation. Once native dispatch begins, HTTP disconnection does not detach the transaction or release admission early. Native work completes its existing validation/rollback path. Unknown request outcomes require inspection before manual retry.

## Secrets

Credentials are memory-only; settings retain ApiKey=null. Environment variable names are whitelisted and read only on explicit configure. Browser password clears on submit. Only masked suffix is returned. Results/events are sanitized before retention and again before serialization. Executable IR containing the current credential is rejected rather than modified. Clear removes key/settings/plan; host disposal clears memory. No persisted credential; localStorage only stores theme.

Provider factory reuses existing strict adapters. ObservedSource reads actual response model alias and times the provider call; planning wall time is separate. Missing usage remains null. Connectivity output is discarded, never becomes a plan.

## Native lifecycle and threading

`StaDispatcher` owns one long-lived background STA with a Windows message pump. NativeSession construction does not activate COM. Connect and all contexts/native calls/close/dispose remain on that STA; HTTP receives only DTOs, never RCWs. Live ForEdit creates a pure snapshot runtime for async planning. GET state/status reads cached records.

Before NewDocument: explicit connection, no owned context, responsive process, readable GDI <7000, no known open CADHarnessM*/Playground_* test Parts, valid template. Exactly one NewDocument call; ownership registered immediately before title/state/geometry work. Original active document is retained internally, titles cannot be provided by browser.

Creation commits a provisional empty state in a unique per-Part AtomicStateStore, then calls the existing construction transaction. Only successful final validation/commit publishes editable state. Failed construction stays owned for inspect/close, without editable state. Inspect mode retains one successful Part; optional auto-close happens only after successful validation and preserves observed state in the result while releasing live editing.

Edit verifies the exact owned live document and revision; the existing transaction independently checks native identity, binding, drift and legality. Rollback failure or state drift invalidates editing. Proposed current-program metadata changes only after success. Failed edits retain prior committed state and actual transaction flags.

Close checks the exact owned RCW against document inventory before CloseDoc(title), restores original active document if available and reports restoration failure otherwise. External closure requires releasing the old session. Normal async host disposal waits for admission, cleans owned Part on STA, then uses SolidWorksConnection disposal ownership: attached user applications never exit, started applications exit only when empty. Force-kill/cross-process recovery is not implemented.

## Observability and verification

Cards/relations derive from serialized strict IR; Capabilities is the actual planner projection. Preflight is aggregate, without fabricated check statuses or duplicate UI geometry solve. Native output shows actual operations, transaction flags/scopes/read sets and existing exclusive telemetry. Persistent reference payloads are advanced/collapsed; no COM pointer is exposed. Events are real action completion/authorization records, not simulated realtime native events. Exports contain current plan and last result, not unlimited history.

`tests/CadHarness.Playground.Tests` injects an HTTP fake into the shared Responses adapter and a native double incapable of COM. Real parser/capability/relation preflight and snapshot edit projection still run. Tests cover credentials, provider failures/deadlines/cancellation, input JSON/size, guards/tokens/revisions, concurrent execution, state/failure projection and offline assets.

Run `scripts/test-playground.ps1`. Test executable `--serve` starts MOCK ONLY browser host on 5187; not a production startup option. Normal startup is `scripts/start-playground.ps1`. See [user guide](PLAYGROUND-USER-GUIDE.md) for manual-only native smoke, safety and finite limitations.
