# Milestone 13 Verification

## Status

**PARTIAL**, against `SWCAD_Harness_v0.3_PRD.md` v0.3-draft-2, sections 7/7.1, 17 and Milestone 13. The read-only implementation and supplied-fixture checks pass. Two required native cases remain unverified: a suppressed feature and a changed configuration. Both supplied originals contain only their saved default configuration and no suppressed feature. Pure tests cover these cases but do not replace native evidence.

The user explicitly authorized one additional open after the four successful native slots. This is recorded in `artifacts/milestone13/authorized-v2/authorization.json` with the prior ledger snapshot. The cumulative allowance is now **0 new Parts / 6 opens**; actual use remains **0 new Parts / 4 opens / 4 closes / zero owned documents remaining**. A supplemental file was subsequently supplied, but read-only metadata queries found that it is still an open engineer document and also has only one default configuration. It has not consumed a native open slot. No source fixture was changed to fabricate suppression or configurations.

## Implementation

| File | Responsibility |
|---|---|
| `src/CadHarness.State/ExternalObservation.cs` | Two-stage bounded inventory/observation, strict v0.3 overlay, local reference-derived identities, unknown dependency completeness, limit diagnostics |
| `src/CadHarness.SolidWorks/ExternalPartInspection.cs` | Owned read-only inspection copy, native feature/subfeature traversal, subtype scalar readers, body/face geometry, byte/configuration/dirty-flag checks and safe cleanup |
| `tests/CadHarness.ExternalObservation.Tests/*` | 27 pure tests, fresh native controllers, independent native inventory/scalar oracle, persistent open-cycle ledger |
| `scripts/freeze-milestone13.ps1` | Exact pre-implementation source and historical evidence baseline |
| `scripts/test-milestone13.ps1` | Release build, isolated M11 contracts and M12 pure regression |
| `scripts/test-milestone13-native.ps1` | Frozen five-slot maximum schedule using the user-selected external originals |
| `scripts/test-milestone13-authorized.ps1` | Separate explicit extra-open grant and optional two-configuration supplemental schedule |
| `scripts/verify-milestone13.ps1` | No-native baseline, original, frozen-source/binary, numeric-result, lifecycle and fixture-coverage audit |
| `CadHarness.sln` | Registration of the new test project only; prior registrations retained |

Existing production files and all nine M11 schemas remain unchanged. This reuses `ObservedModel`, `ObservedStateValidation`, `ObservedIdentity`, the existing STA connection and native file fingerprinting. There is no synthetic construction `CadProgram`, no execution context returned for external editing, no new operation kind, and no external capability promoted to executable. `NativeQualificationCandidates.RequireExecutable` still refuses every M13 target.

## Read-Only Contract

The caller supplies an original Part and a separate fresh copy path. The service refuses existing open originals/copies, fingerprints the original, creates and verifies a byte-identical copy, and opens that copy with `swOpenDocOptions_ReadOnly | Silent`. It verifies native read-only state and path before inspection. The native original is never opened, adopted or modified. Observation sidecars belong to the inspection copy, not the engineer's original directory. Repeated inspection uses a fresh copy with the same source lineage.

Inventory is lazy and finishes before subtype readers start. It captures unique `IFeature` objects through `FirstFeature/GetNextFeature` and nested subfeatures. At the configured feature bound plus one, it stops without scalar qualification and returns `OBSERVATION_LIMIT_EXCEEDED`, a partial noneditable report and an explicit native-count lower bound. Parameter, geometry and dependency overflows also prevent a complete/editable claim. Limits can be reduced for inspection/tests but cannot exceed the frozen contract (256 features, 4096 parameters/dependencies, 1024 geometry observations).

Each node records native `GetTypeName2`, suppression/error/reference health, verified persistent reference where available, native-definition evidence, subtype/read-only reason, measured scalar unit/accessor provenance, geometry and dependency completeness. Only successful exact COM-identity re-resolution constitutes a binding reference. Missing references yield an explicitly unbound diagnostic identity, never an editable target. Names and ordinals are display information, not binding evidence.

Document lineage GUIDs are deterministic local hashes of the explicitly selected original path; configuration GUIDs additionally include the actual native configuration name. They are not properties written into the file or a claim of an engineer-authored stored GUID. Verified feature identities use these IDs plus persistent-reference bytes. A Save As path gets a different local lineage without guessing. Source and copy hashes are independently retained. Configuration changes are selected at fresh open, never synthesized by rebuilding or mutating the fixture.

Parent/child arrays are captured through native readers. Unavailable/null evidence remains `Unknown`, not an empty proven set. Dangling endpoints lower completeness rather than inventing features; native edges do not become declared design relations. Suppressed nodes stay in inventory and withhold scalar/geometry qualification. All nodes, including recognized scalar candidates, are `read_only` in M13.

Finite scalar readers observe blind non-thin one-direction boss/base depth, an unambiguous one-circle through-all cut diameter, and direction-1 linear-pattern count/spacing where native definitions are available. Hole Wizard is explicitly separate; unsupported histories remain unknown/read-only. These readers do not qualify writes, equations, external design tables, linked dimensions or dependent intent. Geometry records bounded body volume/approximate boxes and native planar/cylindrical face observations; no functional CAD semantics or arbitrary coordinate frame is inferred.

There are no property writes, feature renames, parameter setters, `ModifyDefinition`, explicit rebuild, configuration-switch mutation, save, or external edit calls. Dirty state immediately after native open is recorded, not silently cleared; inspection must not change it. Original/copy bytes and selected configuration are checked again after inspection/close. Resource guards require responsive SOLIDWORKS and GDI below 7000. Cleanup closes only the owned copy and restores the original active engineer document without rebuilding it.

## Native Results

SOLIDWORKS revision `32.0.1` was attached through the existing connection. The frozen `source-v1` schedule ran every applicable slot once, with independent controller PIDs:

| Slot | Result | Evidence |
|---|---|---|
| `history-a` | PASS | Part A: 25 unique feature/subfeature nodes, one observed blind extrusion depth of 2 mm, one solid body |
| `history-b` | PASS | Part B: 31 nodes, one observed blind extrusion depth of 12 mm, three visible unknown `ICE` cut-history nodes |
| `reopen` | PASS | Unchanged Part A opened in another controller; source/configuration identity and feature semantic IDs are identical |
| `overflow` | PASS | Actual 25-node Part A inspected with a bound of 1; count lower bound 2, partial/noneditable overlay, no scalar qualification |
| `configuration-if-present` | NOT RUN | Neither original contains another configuration; no extra open consumed |

The independent oracle uses `IFeatureManager.GetFeatures(false)`, not the production linked traversal. Its full counts are **25/25** and **31/31**. `IModelDoc2.GetFeatureCount()` separately returns **19** and **23**; that API uses a different counting convention and is not substituted for the all-subfeature count. Native subtype, suppression and measured scalar values are cross-checked against independent native definition/topology reads while the same owned read-only document is open.

Part A body bounds are approximately `(-5.5, -15.271359530245363, 0)` to `(5.5, 15.728640469754637, 2)` mm, volume `516.9690200129498 mm^3`. Part B bounds are approximately `(0, -27, 0)` to `(30, 0, 12)` mm, volume `6837.879381975312 mm^3`. These are reported observations, not invented input specifications.

Part B retains three native edges from the recognized extrusion to unrecognized `ICE` descendants. Their display labels are not used to infer supported cut semantics; all three remain `unrecognized/read_only`. The distinct native histories are one extrusion versus an extrusion followed by these three cut-history nodes, with owning sketches retained. Unknown/folder nodes and unavailable references are not silently discarded.

All four workers passed copy/original checksum, native read-only flag, unchanged dirty state, owned-close and original-active restoration checks. Peak recorded GDI is **2539**. No open test document or running native controller remains. The original frozen schedule and its reports are not overwritten by later supplemental runs. Supplemental preflight queried only existing-document metadata (revision/process ID/open status/configuration names/dirty flag), with zero native opens; the evidence is `authorized-v2/fixture-preflight.json`. The engineer's still-open document was not closed or modified.

## Original Checksums

The two user-selected originals under `D:/Desktop/solidworks` remain byte-identical:

| Original | Bytes | SHA-256 |
|---|---:|---|
| Part A, first supplied path | 69199 | `361a5f1fce13f694a481888266af497d093341e62ce074bd1e2818cb227b86d8` |
| Part B, second supplied path | 110808 | `6522eed09f6ecea96570512324455b0bf26d2da673df8755915b9d83e25bd107` |

Exact paths are recorded in `artifacts/milestone13/source-v1/originals.json`. Inspection copies, strict observed overlays and independent worker reports are in the same namespace. The before-authorization ledger and explicit extra-open grant are in `authorized-v2`.

## Pure Tests And Preservation

The full Release solution build passed with **0 warnings / 0 errors**. M13 pure tests pass **27/27**, M12 durability tests **48/48**, and isolated M11 frozen-contract tests **76/76**, without activating SOLIDWORKS. An additional build of the final native oracle code passed with zero warnings/errors before its native freeze.

Pure coverage includes strict observed JSON roundtrip; unknown descendants and dependency edges; scalar units/accessor evidence; suppression and missing dependencies; stable reference identity after unchanged reopen, rename and reorder; same-size/name ambiguity; configuration/Save As namespace changes; source-copy drift; absent/invalid references; exact/overflow limits; parameter/native-geometry overflow; dangling/duplicate/cyclic dependencies; and mutation-capability refusal. Native configuration/suppression coverage remains explicitly missing despite these passing tests.

M13 pure evidence: `artifacts/milestone13/pure/20261009T033702816/results.json`. M12 pure regression evidence: `artifacts/milestone12/pure/20261009T033702905/results.json`. Isolated M11/build summary: `artifacts/milestone13/regression/20261009T033704585/result.json`.

Offline verification audits all **15,565** frozen baseline entries and the current accepted source/binaries. Historical evidence and pre-existing production source are unchanged; only solution project registration changed among prior source files. The audit hashes with read sharing so an engineer's open clean SOLIDWORKS file is not force-closed for hashing. `scripts/verify-milestone13.ps1` emits a new timestamped `audit/<timestamp>/verification.json` and current `source-manifest.json`, never overwriting prior evidence. It distinguishes successful execution from complete PRD fixture coverage.

## Remaining Gate

Provide an existing native Part containing a suppressed feature and at least two configurations, and close its original manually before inspection. The currently supplied supplemental file has no second configuration and remains open; its suppressed-state case has not yet been inspected. `scripts/test-milestone13-authorized.ps1 -UserAuthorizedAdditionalOpen -Fixture <path> -ConfigurationA <name> -ConfigurationB <name>` uses the new namespace and remaining two authorized open slots, with no Part creation or mutation. Configuration names may be omitted to select the saved active configuration followed by the first actually existing alternative; if none exists, the second slot is skipped and the report remains PARTIAL. Final coverage audit can mark COMPLETE only after actual suppressed-state and same-file configuration-identity evidence passes and originals remain unchanged. The added open authorization alone does not supply this missing native configuration.

Milestones 14-20 are not implemented. No external scalar edit, multi-target transaction, semantic creation-program reconstruction, UI workflow or arbitrary external-model competence is claimed. Historical unrelated regression issues remain as documented in M11/M12; this report does not claim universal v0.2 test completion.
