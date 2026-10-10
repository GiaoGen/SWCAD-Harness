# M15 Generic Sketch and Spatial Binding

**M15: COMPLETE within PRD Section 10 / Milestone 15's bounded P0 scope.**
Baseline: `1963839`, branch `v0.3-main-update`. Verification date: 2026-10-10.
M14's approved acceptance amendments remain unchanged. M16-M20 are not implemented by this change.

## Implemented Boundary

- Typed, closed planar sketches: lines, signed arcs, circles, polylines and slots; ordered outer/inner loops; at most 64 lowered primitives, 8 loops and 64 constraints.
- Pure analytic closure, winding, area, intersections, containment, declared constraint consistency and finite/unit/budget checks. Open, touching/overlapping, contradictory and unresolved driving geometry refuses before native mutation.
- Essential coincident, horizontal/vertical, parallel/perpendicular, concentric, equal, distance and radial/diameter declarations are checked against explicitly dimensioned coordinates. P0 is not an arbitrary symbolic solver. Coordinate positions are supplied drivers, realized by native fixed geometry/centers; circles use exactly one declared native radial/diameter dimension. No inferred dimensions or default positions are inserted.
- Explicit right-handed signed frames on unique principal planes, managed planar faces with bound in-plane direction, and positive/negative offset datums. No camera coordinates or name-based fallback.
- Public native-context methods: `CreatePlanarSketch`, `CreateOffsetDatum`, `ReadPlanarSketch`, `EditSketchRadius`. Existing construction checkpoints, semantic registration, persistent-reference adapter, rebuild and rollback are reused.
- Supported edit is an exact independent circle radius change. Primitive/profile/host ownership, native solver status, frame transform, world geometry and dimensions are revalidated. Bindings are session/configuration-local, not a generic persisted CADState/reopen protocol.

`V03ContractCapabilities.Construction` advertises only `CreateSketch` and `CreateDatumPlane` as executable operations. The explicit native context still performs preflight/binding checks. Context-free whole-program execution remains unavailable; generic extrusion/revolve/boss/cut remain unqualified. The legacy nine-operation registry is unchanged. `.axis_x` is registered logical direction metadata, not a newly constructed physical revolve axis.

## Required Corpus and Actual Evidence

Stable final native run: [native_v8 result](D:/CAD-Harness0.2/artifacts/milestone15/native_v8/result.json), [typed schedule and source hashes](D:/CAD-Harness0.2/artifacts/milestone15/native_v8/schedule.json), [immediate steps](D:/CAD-Harness0.2/artifacts/milestone15/native_v8/steps).
All 97 steps are retained with controller/time/API data; source and binary copies are frozen in that run. Current source hashes match that freeze.

| Requirement | Evidence / result |
|---|---|
| Outer + inner loops; polyline, circle, slot closure | `composite-outer-inner-slot`: PASS; outer polyline plus circular and slot inner loops |
| Arc + line closure; signed/rotated local placement | `rotated-managed-face-arc-and-line`: PASS; managed top face, origin (5,-4,8), X=(0,1,0), Y=(-1,0,0) |
| Initial/noninitial planes | Initial XY plus managed face and both offset datum sketches: PASS |
| Signed datum offsets | `datum_positive-plane-and-sketch` +12 mm and `datum_negative-plane-and-sketch` -6 mm: PASS |
| Circle dimension and supported edit | `circle-before`, `circle-supported-radius-edit`: radius 3 -> 4 mm, exactly one sketch revision, unchanged profile reference: PASS |
| References/ownership/frame survive edit and later construction | `edit-reference-stable-after-later-features`: PASS; actual host/reference resolution and unchanged native transform |
| Native solver classification | All accepted sketches report `swFullyConstrained` (3); pure corpus rejects declared under/over and unresolved circle radius |
| Independent geometry | Official `IMathPoint`/`IMathVector.MultiplyTransform` Oracle checks world endpoints/centers, radii, lengths and signed arc direction independently of production array-transform logic: PASS |
| Reversed normal | `native-reversed-face-refusal`: `SPATIAL_FRAME_MISMATCH`, unchanged feature inventory, no mutation: PASS |
| Actual spatial ambiguity | Official API creates a second coincident native origin XY plane on disposable Part. `native-spatial-ambiguity-refusal` records both distinct persistent references; public sketch creation returns `BINDING_AMBIGUOUS` before checkpoint/mutation: PASS |
| Open/intersecting/zero-area/inconsistent/unresolved/budget corpus | M15 pure 25/25: PASS |
| Supported-edit recovery | `radial-edit-rollback`: real Setter changes 4 -> 5 mm, controlled post-Setter exception, native radius restored to 4, complete readback/Oracle, revision remains 1: PASS |

Full-circle seam endpoints in the readback are canonical specification points, not measured native seam vertices. Native circle center/radius/type/length and world placement are measured. Arc endpoints and rotation direction are native measurements; equal endpoints/length alone are not treated as proof of the intended half-circle.

## Failures Preserved and Repairs

| Run | Actual disposition |
|---|---|
| `native_v1` | Invalid configured default template; no Part created. Existing `gb_part.prtdot` subsequently used. |
| `native_v2` | Interrupted controller while native creation did not return; not PASS. Exact tool-owned unsaved Part safely closed by a new controller using immutable identity + live title/path + ledger. Scoped modal dimension-input preference was added and is restored on exit. |
| `native_v3` | `IDimension` cannot supply the attempted persistent reference. Replaced with exact persistent-circle ownership and unique native dimension relation resolution; no name/value guessing. |
| `native_v4` | Test hash reader encountered native file sharing after saving pristine blank Part. Test uses existing shared-read hash implementation; delayed post-close blank identity record is explicitly preserved, not relabeled PASS. |
| `native_v5` | Host face COM handle became unusable for reference capture after sketch rebuild. Host identity now freezes before mutation and resolves/verifies the same persistent reference afterward. Rollback and cleanup passed. |
| `native_v6` / `native_v7` / `native_v8` | PASS. v7 adds signed native arc verification and scoped capability projection; v8 additionally verifies radial recovery on the stable final source. |

All namespaces, failed results and interruption/recovery audits remain untouched. No engineer original was opened or edited for M15.

## Regression and Resource Closure

Full solution Release build: **0 warnings / 0 errors**. Final isolated pure suites: **331/331**, including M15 25, M11 77, M12 48, M13 27, M14 86, State 13, construction transactions 22 and mutation transactions 33.
Evidence: [suite results](D:/CAD-Harness0.2/artifacts/milestone15/final-regression/suite-results.json) and per-suite logs beside it. Earlier scratch-input failures remain in `regression-run-1`; corrected inputs passed in `regression-run-2`. Final wrapper initially misclassified stdout while aggregating, yielding shell exit 1 despite all eight recorded suite exit codes 0; original mixed `result.json` is preserved, and `suite-results.json` extracts the actual recorded statuses without rerunning tests.

[M15 ledger](D:/CAD-Harness0.2/artifacts/milestone15/native-budget.json): **3 creation attempts / 3 new Parts / 4 open attempts / 7 closes / zero owned titles**. This satisfies the unamended 3-Part/4-open limit; failed attempts are counted. All introduced documents were controlled copies or new test Parts; native_v8's before/after document inventories are empty and cleanup is true.

[Final hash/source audit](D:/CAD-Harness0.2/artifacts/milestone15/final-audit.json): all four Factory native files and their Registry members unchanged; manual M14 original remains SHA256 `60fbd4101e4c1f3c5ca129c7e5d44b1eadf28c50b6483e0259d06b4b716f9a8b`; M14 remains 145 opens / 145 closes. [Resource follow-up](D:/CAD-Harness0.2/artifacts/milestone15/final-resource-audit.json) corrects sandbox CIM unavailability in the first audit: no running native controller or native recovery marker; 34 recovery files are intentional isolated pure-test fixtures. SOLIDWORKS remains responsive; its process was not terminated by this task.

## Source Changes

New `PlanarSketchPreflight.cs`, `PlanarSketchBackend.cs`, focused `CadHarness.PlanarSketch.Tests` project and `test-milestone15.ps1`; solution registration; scoped application context in `BackendContracts`/`SolidWorksConnection`; two-operation capability projection and M11 gate regression; bounded owned-reopen bookkeeping in existing test lifecycle helpers. PRD, external Scalar/EditSet handlers, qualification gates, tolerances and Fixture Factory are unchanged.

Next milestone is M16's qualified feature consumption. This result does not claim generic solid-feature execution, arbitrary symbolic constraint solving, persistent sketch recovery across controllers or full v0.3 completion.
