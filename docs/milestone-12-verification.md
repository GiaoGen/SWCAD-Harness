# Milestone 12 Verification

## Result

**COMPLETE**, for the finite managed subset below, against `SWCAD_Harness_v0.3_PRD.md` v0.3-draft-2, sections 6/6.1, 17 and Milestone 12. Native acceptance ran on SOLIDWORKS 2024 revision `32.0.1`, attached to the existing application. No provider calls or external-model intake occurred.

The original allowance was 2 new Parts / 5 opens. After two retained failed source versions, the human explicitly authorized one additional open and completion of verification. `artifacts/milestone12/authorized-v3/authorization.json` records that exception. Actual cumulative use is **1 new Part, 6 opens, 7 closes, zero owned documents remaining**. This is completion under the authorized six-open limit, not compliance with the original five-open limit. The cumulative ledger was never reset.

## Implementation

| File | Responsibility |
|---|---|
| `src/CadHarness.State/ManagedRevisionStore.cs` | Exclusive controller lease, immutable revision packages, exact hashes, checkpoint/recovery authority, atomic small-pointer publication and strict refusal |
| `src/CadHarness.SolidWorks/ManagedContextRestore.cs` | Persisted native reference rebinding, ownership/subtype/scalar/geometry/relationship checks, reconstruction of managed operation and relation context |
| `src/CadHarness.SolidWorks/ManagedPartSession.cs` | STA-owned open/close, native save and independent saved readback, safe copy migration, existing planner projection and parameter transaction integration |
| `tests/CadHarness.ManagedRecovery.Tests/*` | Pure durable fault matrix, independent native workers, numeric oracle and persistent lifecycle budget |
| `scripts/*milestone12*.ps1` | Baseline/acceptance freezes, pure regression runs, retained original/continuation/authorized schedules and offline verification |
| `CadHarness.sln` | Registration of the M12 test project, retaining the prior M11 registration |

This is an additive implementation of `ICadStateStore` and a managed-document facade. Edits still use `MutationTransaction`, `TransactionalParameterBackend`, the existing Binder, relation engine and runtime capability projection. No parallel general-purpose transaction runtime, part-family production dispatch, new executable operation enum or external feature capability was introduced.

The supported cold-restoration vocabulary is a qualified v0.2 managed rectangular initial blind non-thin extrusion, a supported circular through-hole, and active linear/rectangular hole patterns. Persisted state/program retain strict schema `0.2`; the enclosing revision manifest is `0.3`. Unknown/unsupported histories refuse. Save As, external intake and automatic identity reconciliation are not implemented.

## Durable Protocol

`Closed -> Opening -> Inspectable -> Editable`, or `Quarantined`. Editability requires exact package hashes, selected working path, document/configuration GUID and name, persisted-reference ownership and native scalar/geometry/relationship readback. The restored dictionaries, relation program and dependencies come from the saved construction program and CADState, not prior COM handles or controller memory. Names and nearest geometry are never a recovery fallback.

Each immutable candidate contains `part.SLDPRT`, `state.json`, `program.json`, and `manifest.json`. `recovery.json` records the exact previous manifest and candidate revision before mutation. Native save, flushed sidecars and independent saved native verification precede atomic publication of `current.json`. This is **not** an atomic transaction across CAD and JSON files. Previous complete revisions and failed candidates remain preserved.

Without a journal, byte drift blocks dispatch. With a journal, only the current journaled candidate or exact prior complete revision can be authoritative; directory order is irrelevant. Recovery restores verified native bytes before reopening and verifying them, then republishes the selected pointer and clears the journal. No provable complete revision, failed rollback, or inconsistent publication leaves an editable session. A post-pointer maintenance failure retains the journal without pretending the committed revision rolled back.

Clean files freshly opened in an independent controller can supply the initial migration/recovery saved-read proof. Any edit or native save invalidates that proof, even if hashes stay unchanged. An edited publish closes and independently reopens the native document before pointer publication. SOLIDWORKS retains writable file handles on clean documents, so shared read hashing/copy is followed by exact pre/post byte checks.

Copy migration checks legacy schema and associations, opens only an owned working copy, verifies native identity/references/geometry and retains the original state as rollback. It never assigns identities to an external intake or rewrites the legacy originals. Existing engineer documents are not adopted, saved, or closed. Ownership is registered before restoration/mutation; every worker restores the original active document. The responsive-process/GDI guard remains at 7000.

## Native Evidence

All acceptance sources, binaries, template, fixture, oracle and schedule were frozen before execution. A production fix produced a new named run; neither failed run was overwritten.

| Namespace / Step | Controller PID | Result | Native evidence |
|---|---:|---|---|
| source-v1 / create | 7124 | Retained failure | Created/saved/closed the owned original correctly; hashing failed on SOLIDWORKS's writable file handle |
| source-v2 / migrate | 20500 | Retained failure | One open/close; initial base-extrude qualification was too narrow and refused |
| authorized-v3 / migrate | 20348 | COMPLETE | Legacy copy cold-opened; revision 1, thickness 8 mm, all references/relations reconstructed |
| authorized-v3 / interrupt | 1832 | COMPLETE fault test | Thickness 8 -> 10 saved; injected failure before sidecar publication; live rollback 8, pointer revision 1 unchanged, saved disk differs, journal retained, dispatch quarantined |
| authorized-v3 / recover-edit | 26644 | COMPLETE | Exact prior snapshot recovered as thickness 8/revision 1; edited to 10; independent save close/reopen; published revision 2 |
| authorized-v3 / readback | 3456 | COMPLETE | Fresh controller reads thickness 10/revision 2; actual configuration switch refused |

The required create/save/close/controller-exit/fresh-controller/rebind/edit/save/close/fresh-controller/readback sequence is proven across distinct PIDs. The original creator's later hashing failure is not counted as a successful full attempt; its actual saved/closed original is reused without repeating creation. The intentional interruption is an exception at the save/publish boundary followed by controller exit; it is not advertised as forced process-kill or power-loss testing.

Independent numeric oracle: 100 x 60 mm plate, one solid body, four diameter-6 mm through-holes at `(+-30, +-15)`, circular boundaries at `z=0` and `z=depth`, five design relations and six dependencies. At 8 mm, measured volume is `47095.22131576613 mm^3`; at 10 mm, `58869.02664470767 mm^3`. Native feature depth, body extents, volume, hole centers/diameters/boundaries and relationship validation agree. The edited feature's persistent reference is unchanged.

Native refusals cover document GUID, configuration GUID/name, selected Save As lineage, invalid persistent reference, state/native scalar disagreement, dispatch after incomplete publish, and an actual native configuration switch to `M12Other`. The identity and scalar-negative tests substitute incorrect persisted metadata against the actual native document; they do not claim arbitrary externally modified history testing.

The lifecycle ledger includes both failed attempts and the saved verification reopen. Peak recorded GDI is **2152**, below 7000. All six workers report original active-document restoration and no owned document remaining. No application shutdown or force-close of engineer documents occurred.

## Originals

All three originals remain byte-identical after success and failure:

| File under `artifacts/milestone12/legacy` | Bytes | SHA-256 |
|---|---:|---|
| `CADHarnessM12Original.SLDPRT` | 137590 | `6ac321e22000ece77e593947ddf59b7d5312f1256f52cf0f4fd4e8e61b58c19b` |
| `state-v02.json` | 16197 | `402375c026cdf2c1a39bc6d77fc6d2b8a3ce2c509b97aae214e633f5d728032f` |
| `program-v02.json` | 1468 | `b12b50d3350b75b574b90e3671cb553568973c2f0fa0db4ce0fceae5956e3862` |

## Tests And Preservation

`scripts/test-milestone12.ps1` completed a full Release solution build and the current stepwise qualification build, both with **0 warnings / 0 errors**. It then passed M12 **48/48**, M11 contracts **76/76**, M3 state **13/13**, M6 transactions **33/33**, M9C construction **22/22**, and stepwise qualification **28/28**, with zero native calls. Existing suites use an isolated fixture/evidence root so historical reports are not overwritten.

M12 pure coverage includes every durable fault point (before save, after save, after state flush, after package verification, before pointer, after pointer), native save/read failure, missing/corrupt sidecars and snapshots, hash drift, path escape, identity/configuration mismatch, exclusive lease, exact journal authority, quarantine, nonzero initial revisions, writable saved handles, finite base/boss subtype classification and edit-proof invalidation.

Final pure evidence: `artifacts/milestone12/pure/20261009T024016920/results.json`. Isolated passing regression evidence: `artifacts/milestone12/regression/20261009T024018897`. Original failed native source/binary snapshots: `native-v1-snapshot` and `native-v2-snapshot`; original/continuation reports remain PARTIAL. Accepted native reports and freeze: `artifacts/milestone12/authorized-v3`.

Historical test deficiencies are not hidden: M5 remains 36/39, with all three failures and output exactly matching independently compiled pre-M12 HEAD `7f0109ddb5db1e5c0d70132d4bcd3020f159f113` (`legacy-assertion-comparison/comparison.json`). An earlier historical parameter-mutation G2 trial aborts on obsolete `linear_edge` fixture typing; its timestamped evidence remains. M11's previously documented IR enum-order and M7 obsolete-assertion failures remain recorded. These are not advertised as passing universal v0.2 regression coverage.

Run `scripts/verify-milestone12.ps1` for a no-native, read-only audit followed by a new timestamped evidence output. It checks all 2459 baseline files (2164 historical evidence entries), unchanged M11 contract/source hashes except allowed solution registration, both failed source/binary snapshots, accepted frozen source/binaries/template, original hashes, native reports and numerical oracles, retained revision lineage, final package hashes, cumulative authorized ledger and selected pure results. It emits `artifacts/milestone12/audit/<timestamp>/verification.json` and a current `source-manifest.json`; repeat audits never overwrite existing evidence.

## Deferred Scope

Milestones 13-20 are not implemented here. No external Part import/edit, generic sketches, revolve/cut/additive vocabulary, multi-target transaction, new Playground workflow, planner run or full v0.3 release gate is claimed. Broader v0.2 history restoration is refused until separately qualified. This API is an owned managed session integration point, not a new CLI or UI workflow.
