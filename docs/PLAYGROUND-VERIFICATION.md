# Playground / Debug Console V0 verification

Status: **PLAYGROUND V0 COMPLETE** (2026-10-06). Baseline: completed GENERALIZED CAD HARNESS v0.2, current HEAD `a2edad2`, plus existing untracked formal M10 acceptance work. This task is not M11.

## Scope and changes

Browser → loopback ASP.NET Core host → existing Harness libraries. Shared strict provider/schema/planner, actual executable capability projection, explicit production pure preflight, construction/parameter transactions, atomic state and telemetry reused. No core CAD semantics/capability/preset/backend/Binder/transaction changes.

Added:

- `src/CadHarness.Playground/CadHarness.Playground.csproj`
- `src/CadHarness.Playground/Program.cs`, `Contracts.cs`, `Provider.cs`, `PlaygroundSession.cs`, `StaDispatcher.cs`, `NativeSession.cs`
- `src/CadHarness.Playground/wwwroot/index.html`, `app.css`, `app.js`
- `tests/CadHarness.Playground.Tests/CadHarness.Playground.Tests.csproj`, `Program.cs`, `Fakes.cs`
- `scripts/start-playground.ps1`, `scripts/test-playground.ps1`
- `docs/PLAYGROUND-USER-GUIDE.md`, `PLAYGROUND-ARCHITECTURE.md`, this verification

Changed only root `CadHarness.sln` (register two projects) and `README.md` (minimal Playground section). Existing production Harness source files changed: **0**. Preexisting untracked formal benchmark files and system walkthrough retained.

## Acceptance matrix

| Check | Result / evidence |
|---|---|
| Full registered solution Release build | PASS, 20 projects, 0 warnings / 0 errors; build only, no historical test execution |
| Playground pure/API integration | PASS, **97 assertions**, including per-response credential absence assertions |
| Strict Planner / shared schema / provider mock | PASS, injected HTTP provider via existing Responses adapter |
| Relation/layout preflight | PASS, actual production preflight rejects overlapping-hole program before mutation |
| Workflow guards | PASS, before plan/preflight, failed preflight, replaced/consumed approval, wrong revision, double/busy execution, second owned Part, no live edit state |
| API failures | PASS, invalid/duplicate/unknown-field JSON, empty/UTF8 oversized intent, provider HTTP error, provider deadline and caller cancellation |
| Secret lifecycle | PASS, memory-only config, masked status, clear, logs/retained results/export contain no credential, repeated export bounded |
| Mock state/transaction projection | PASS, actual revision preserved, failed mock edit reports no state commit, prior state retained |
| Browser frontend | PASS, meaningful 3-column view, visual dimension formatting, Raw IR/Relations/Capabilities/Planner Details, explicit preflight, native Execute disabled while disconnected |
| Theme / offline assets | PASS, dark/light inspected, local HTML/JS/CSS only, no browser error/warning logs |
| Production startup script/API | PASS, 127.0.0.1:5186, LLM not configured, SW disconnected, Parts=0; all three assets HTTP 200 |
| Historical evidence audit | PASS, 1741 existing milestone verification/evidence MD/JSON/JSONL hashes unchanged across final verification; no original evidence file edited |
| Native smoke | NOT RUN, explicitly reserved for user manual testing |

The assertions simulate one created/closed mock document and mock edits. They do not create a SOLIDWORKS Part or claim native geometry/cleanup verification of this new UI adapter. Native lifecycle code was compiled and reviewed against existing production APIs; its actual smoke remains manual by instruction.

## Commands and isolated evidence

```powershell
.\scripts\test-playground.ps1
.\scripts\build.ps1 -InteropDir 'D:\Solidworks Crops\SOLIDWORKS\api\redist'
.\scripts\start-playground.ps1
```

New evidence only in `artifacts/playground/`:

- `release-build.txt`, `pure-api-tests.txt`
- `production-startup.json`
- `historical-evidence-hashes.json`, `historical-evidence-verification.json`
- `ui-dark.png`, `ui-light.png`

Browser qualification used the isolated test executable `--serve` on 5187, with fake provider/native dependencies. It never clicked native connect/execute controls. Production startup verification made only status/static GET requests. Temporary test hosts and browser tab were closed after verification.

**SOLIDWORKS launched: NO**

**Native Parts created: 0 / closed: 0**

**Real LLM calls: 0**

No benchmark, historical native tests, stress tests or M11. Qualification timings are not performance conclusions.

## Remaining manual verification and limitations

Follow [user guide](PLAYGROUND-USER-GUIDE.md) for simple 80×50×10 plate, full G2, thickness 8→10 and hole Ø6→Ø8, then close the owned Part. Human intent inspection and the explicit plan → preflight → confirmation gate remain mandatory.

One host/session/owned Part; finite production capability only; aggregate pure preflight; native feasibility checked by existing transaction; no cross-host recovery, arbitrary user document editing, Stepwise UI, automatic repair/rename/retry. Secrets persist only for host lifetime; normal shutdown cleans up on the owning STA, force termination may leave an orphan Part that the user must close.
