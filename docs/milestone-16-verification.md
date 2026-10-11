# M16 Generic Solid Feature Construction

**M16: COMPLETE for the bounded native scope of PRD Section 11 / Milestone 16.**
Baseline: `e2cabef`, branch `v0.3-main-update`. Verified 2026-10-11.
M17-M20, Fixture Factory and external M14 qualifications are unchanged.

## Implemented Boundary

- Public STA-context APIs: `ConstructGeneric(ConstructionProgram)`, `EditGenericScalar(GenericScalarCommand)` and `CaptureGenericConstructionState()`.
- M15 fixed-coordinate profiles and independently driven circles are consumed through the existing sketch backend. Declared geometric relationships are not relabeled as native associative constraints. Polygon coordinate/constraint editing remains unqualified.
- Initial generic prism; single-body full/finite revolution about the exact profile-local X axis; parallel-plane additive boss and blind/through-all cut on a tracked managed body. References are explicit, never names or nearest topology.
- Analytic profile legality, signed frames, revolution first moments, and laminar prismatic material integration precede mutation. Detached/no-change/deleting sweeps, partially intersecting footprints, whole support-footprint removal, mixed normals, mixed rotational/prismatic histories and unproved multibody behavior refuse.
- Exact native profile consumption, persistent references, actual `GetParents` dependencies, solver status, axis/angle, extrusion modes/depths, single-body count, volume and support envelope are checked after rebuild. Equation/table/external drivers and multiple configurations refuse.
- Shared `RequestMutationTransaction` now accepts a typed state policy; its existing v0.2 interface remains a wrapper over the same coordinator. New feature kinds are genuine v0.3 records, not fictitious v0.2 extrusions. Existing native construction checkpoints own rollback of features, absorbed sketches, datums, native parameters and session metadata.
- Qualified edit pairs are only circle-driven `RevolveRadius`, blind `AdditiveBossDepth`, and blind `CutDepth`. Runtime state lists only bindings actually present and verified. Root depth, revolve axial edits, arbitrary profile edits and through-all blind-depth edits remain closed.

This is **session/configuration-local managed construction**, not a new generic durable reopen protocol. `StateCommitted` means the authoritative live generic revision committed, not a disk manifest publication. Successful native files are saved without an identity-changing Save As during the managed transaction sequence. Cross-controller generic-state recovery, unsupported Save As reconciliation and whole-release durable acceptance are not claimed. The existing M12/M14 durability paths are unchanged. Context-free contract admission still cannot authorize native mutation; the public verified context is required.

## Native Qualification And Public Evidence

Stable public source/binary freezes: [rotation schedule](D:/CAD-Harness0.2/artifacts/milestone16/native_v11/schedule.json) and [composition schedule](D:/CAD-Harness0.2/artifacts/milestone16/native_v12/schedule.json). Final audit confirms their production and test source hashes match the delivered version.

| Required fact | Actual evidence / result |
|---|---|
| Independent minimal revolution | `native_v4`: candidate create/radius edit/recovery PASS; `native_v11`: public create PASS, exact RefAxis derived from declared frame, 360 degrees, major radius 15 mm, initial minor radius 3 mm |
| Finite angle and measured spans | `native_v6`: 90-degree cylinder-sector PASS; X span 20 mm, Y/Z bounds 0..10 mm, volume `500*pi` mm3; exact axis/profile references survive rebuild |
| Independent minimal additive boss | `native_v9` and public `native_v12/steps/078-minimal-additive-boss.json`: PASS before any cuts; one merged body, root volume 48000 mm3 plus `25*pi*6` mm3 |
| Independent minimal extruded cut | `native_v10`: PASS; only root, datum, slot sketch and blind cut; one retained body; subtraction `(32+4*pi)*4` mm3, then depth edit and restoration |
| Multi-feature composition | Public `native_v12/steps/132-composition.json`: PASS; generic root, +10 mm datum, circular boss, slot pocket and independent through-all circular cut |
| Native/state agreement | Exact feature/profile/axis references, actual native parents, scalar accessor values and measured body observations in every successful transaction's state; final public revision 7 |
| Save and identity consistency | `native_v11/steps/034-saved-native.json`, `native_v12/steps/154-saved-native.json`: PASS; ordinary Save3, unchanged live identity/state/geometry, saved native SHA256 |

### Scalar Matrix

| Pair | Native accessor and old/new | Independent result | Recovery / public status |
|---|---|---|---|
| RevolvedBoss / RevolveRadius | Exact persistent circle relation; `IDimension.SetSystemValue3`; 3 -> 4 mm | Torus volume 2664.793188294127 -> 4737.410112522892 mm3; radial envelope 18 -> 19 mm, axial span 6 -> 8 mm | Public `native_v11` PASS; injected 4 -> 5 restored to 4, revision stays 2 |
| AdditiveBoss / AdditiveBossDepth | `IExtrudeFeatureData2.AccessSelections/SetDepth/ModifyDefinition`; 6 -> 8 mm | One merged body; positive volume and Z envelope 16 -> 18 mm; independently measured cylinder and centroid | Public `native_v12` PASS both before and after cuts; injected 8 -> 9 restored to 8, no new revision |
| ExtrudedCut / CutDepth | Same exact extrude definition accessor; 4 -> 5 mm | Slot subtraction, cap positions/radii, volume, centroid and retained envelope PASS; composition final volume `47840+90*pi` mm3 | Minimal `native_v10` and public `native_v12` PASS; injected 5 -> 6 restored to 5, revision stays 7 |

The independent test Oracle reads native `GetMassProperties`, extreme points and torus/cylinder surface parameters directly. Its case formulas do not invoke production material integration or Factory constants. Public `independent-native-oracle.json` steps retain expected/measured volume, difference, centroid, six supports and surface parameters. Full native readback is separate from the independent Oracle.

## Failure Containment And Refusal

- Public [late failure](D:/CAD-Harness0.2/artifacts/milestone16/native_v12/steps/062-late-feature-failure.json): after actual boss and pocket creation, a controlled post-feature exception prevents commit. Both features, both absorbed sketches and the datum disappear; original root inventory, native references, full geometry and revision 1 restore. This is not claimed as a real native rebuild error.
- Public scalar fault steps `native_v11/030`, `native_v12/090` and `150`: real Setters run, then controlled failure; native parameters, checkpoint geometry and authoritative state restore. No unresolved quarantine or recovery remains.
- Public invalid axis, radius zero, unqualified axial edit, unproved footprint overlap and through-all blind-depth edit refuse at `resolve inputs`, before checkpoint/Setter. Native/state remain unchanged. Pure tests cover crossing axes, detached material, untracked hosts, deletion and unsupported overlap/normal proofs.

Historical outcomes are preserved, not replaced by later PASS:

| Run | Disposition |
|---|---|
| `native_v1` | Axis sketch segment selected through an adapter that did not support its native interface; rollback/close PASS |
| `native_v2` | Test-only slash-normalization identity check failed before open; actual blank hash unchanged |
| `native_v3` | Separate sketch-line axis selection yielded null FeatureRevolve2; rollback/close PASS. Official API datum axis selection replaced it, with exact axis readback |
| `native_v4` | Candidate full revolution/edit/recovery PASS |
| `native_v5` | Template InkMarkupFolder incorrectly treated as physical unknown; safe refusal/close. Neutral UI inventory is now bounded and rejects dimensions/material faces |
| `native_v6` | Finite revolution PASS |
| `native_v7` | Initial base extrusion was incorrectly checked using only IsBossFeature; corrected to distinguish native base-or-boss from cut. Rollback/close PASS |
| `native_v8` | Boss creation and late rollback PASS; next edit refused before Setter because exact JSON compared kernel zero noise (`1e-37` / `1e-43`) as drift |
| `native_v9` | Composition, boss/cut scalar edit/recovery and whole-addition rollback PASS after targeted repair |
| `native_v10` | Independent minimal cut, edit/recovery and ordinary save PASS |
| `native_v11` / `native_v12` | Stable public-entry rotation / composition, refusal, scalar recovery and save PASS |

The snapshot repair reuses the existing checkpoint's SI comparison bound `max(1e-12, abs(value)*1e-8)` for mass/support numerical roundoff. Identities, references, program and parameter associations stay exact; construction/Oracle geometric tolerances were not relaxed. Pure tests distinguish zero noise from real volume/support/identity drift.

## Regression, Lifecycle And Sources

Full solution Release: **0 warnings / 0 errors**. Isolated affected pure suites: **351/351** (M16 20, M15 25, M11 77, M12 48, M13 27, M14 86, State 13, construction transactions 22, mutation transactions 33).
Evidence: [suite results](D:/CAD-Harness0.2/artifacts/milestone16/final-regression/20261011T093127444/suite-results.json). An earlier regression setup stopped on an unrelated reserved `aux.json` device-name fixture before tests; its incomplete directory remains, and the corrected run copies only affected test inputs. No M14/M15 native suite was repeated.

[M16 ledger](D:/CAD-Harness0.2/artifacts/milestone16/native-budget.json): **4 creation attempts / 4 Parts / 7 opens / 11 closes / zero owned titles**. Failed opens are included. The original 4-Part ceiling is met. Opens exceed the original 5-cycle ceiling under the user's explicit unlimited-open amendment, recorded in [authorization](D:/CAD-Harness0.2/artifacts/milestone16/open-budget-amendment-20261011.json); no counter/history was reset. No additional Part was created after that amendment.

[Final audit](D:/CAD-Harness0.2/artifacts/milestone16/final-audit.json): PASS. Four frozen blank sources, all Factory native/Manifest/Reader/ready members, and the manual M14 original remain byte-identical. Manual original SHA256 remains `60fbd4101e4c1f3c5ca129c7e5d44b1eadf28c50b6483e0259d06b4b716f9a8b`. M14 stays 145 opens / 145 closes; M15 stays 3 Parts / 4 opens / 7 closes. Public-run before/after document inventories are empty. No running native controller, native recovery marker or unresolved ownership remains; SOLIDWORKS is responsive and was not terminated.

Production changes: `FeatureConstructionPreflight`, profile region relation support, `GenericConstructionState`, typed shared transaction adaptation, `GenericFeatureConstruction`, existing checkpoint/session integration, transaction-authorized M15 calls, and explicit finite capability descriptors. Focused M16 test project/script, solution registration and M11 capability regression accompany them. PRD, legacy enums/parser, external editing, Fixture Factory and unrelated files are not modified.

M16 completion is not v0.3 release completion. M17 planning/defaults, M18/M19 and M20's independent composition, durable reopen and frozen release gates remain unimplemented/unverified here.
