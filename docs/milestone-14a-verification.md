# M14A Native Scalar Qualification

**M14A: COMPLETE. Overall M14: PARTIAL.** Verified 2026-10-09 UTC (2026-10-10 local), against `c9de151` on `blocked-code-version`. This qualifies the four bounded scalar accessor combinations, not EditSet, engineer-model compatibility or held-out acceptance. No M14B/M14C acceptance, M15+ implementation or Factory extension was executed.

## Qualification Matrix

All changes use production observed Handlers, `ExternalEditTransactionBackend`, the shared transaction coordinator and `ManagedRevisionStore`. Public changes additionally use the actual `ExternalPartSession.Edit(ScalarEditRequest)` entry. Each successful scalar has one checkpoint, one new revision, successful native rebuild, complete geometry verification and saved-document close/reopen.

| Parameter / native subtype | Candidate evidence | Public scalar / setter result | Saved / independent cold | Corresponding native refusal |
|---|---|---|---|---|
| ExtrusionDepth / StraightBlindBossExtrude | v3 `depth`: 10 -> 12 mm; AccessSelections/ModifyDefinition; complete Oracle | v13: 12 -> 11 mm, Revision 11; ModifyDefinition Boolean true | PASS: v3 saved, v6 fresh controller at depth 12, v13 fresh controller at depth 11 | v8 equation driver: UNSUPPORTED_PARAMETER_DRIVER |
| HoleDiameter / SingleCircleThroughAllCut | v6: 9 -> 10 mm, Revision 5; driving diameter SetSystemValue3, immediate/rebuilt readback | v13: 10 -> 9 mm, Revision 8; SetSystemValue3 status 0 | PASS: per-edit saved Oracle, v6 and v13 fresh controllers | v12 actual driven dimension: UNSUPPORTED_PARAMETER_DRIVER; v8 blind/unknown history refused |
| PatternCount / SingleDirectionLinearPattern | v6: 4 -> 3, Revision 6; D1TotalInstances | v13: 3 -> 4, Revision 9; ModifyDefinition Boolean true | PASS: all instance centers, saved reopen, v6/v13 fresh controllers | v12 actual GeometryPattern: UNSUPPORTED_NATIVE_SUBTYPE |
| PatternSpacing / SingleDirectionLinearPattern | v6: 20 -> 24 mm, Revision 7; D1Spacing | v13: 24 -> 20 mm, Revision 10; ModifyDefinition Boolean true | PASS: seed/count/direction retained, centers and volume, saved reopen, v6/v13 fresh controllers | v12 actual GeometryPattern: UNSUPPORTED_NATIVE_SUBTYPE |

Core persistent references were re-resolved and round-tripped before/after mutation and reopen: host `aEIAAAEAAAD//v8AAAAAAFEAAAA=`, seed `aEIAAAEAAAD//v8AAAAAAFoAAAA=`, pattern `aEIAAAEAAAD//v8AAAAAAGEAAAA=`. Binding does not use fixture names. The preserved v3 depth success is reused; v3's later diameter failure remains a failure. The interrupted v5 run and Revision 4 disk contents have not been relabeled PASS. v6 explicitly started from Revision 4 and measured its baseline.

## Evidence

- [Historical depth](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v3/results/core/result.json): steps `depth`, `depth-saved-cold-readback` only.
- [Candidate scalars and mismatch rollback](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v6/results/core/result.json), [independent Revision 7 cold read](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v6/results/core-cold/result.json).
- [Public four-scalar edits](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v13/results/scalar-public/result.json), [independent Revision 11 cold read](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v13/results/public-cold/result.json).
- [Origin edit](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v11/results/origin/result.json), [independent Origin cold read](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v11/results/origin-cold/result.json), [exact native origin-source diagnosis](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v10/results/probe/result.json).
- [Equation refusal](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v8/results/equation/result.json), [blind/unknown refusal](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v8/results/unknown/result.json), [native dimension/pattern boundary checks and restoration](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v12/results/boundaries/result.json).

Every new run retains hashed source/binaries, frozen inputs/schedule/authorization and immediately flushed numbered `steps/*.json` beside its result. These record native references, old/requested values, setter enum/Boolean status, immediate/rebuild readbacks, Oracle differences, transaction outcomes, revisions and cumulative lifecycle counts. Factory Registry/Manifest/Reader proofs establish input identity/history only; they are not edit-result Oracles.

## Geometry And Persistence

The independent `NativeEditOracle` calls official native APIs without production geometry/accessor helpers or Factory Reader. It checks one solid, all planar/cylindrical surfaces, complete cylinder count and centers, radii, axes, both through boundaries, bounds, volume, healthy features, seed ownership and exact persistent references. Existing tolerances are unchanged.

| v13 saved step | Revision | Actual / expected volume (mm3) |
|---|---:|---|
| HoleDiameter | 8 | 110195.44290283143 / 110195.44290283146 |
| PatternCount | 9 | 109432.03588800911 / 109432.03588800914 |
| PatternSpacing | 10 | 109432.03588800911 / 109432.03588800914 |
| ExtrusionDepth | 11 | 100312.69956400836 / 100312.69956400838 |

Fresh controller 15064 independently reopened final Revision 11: depth 11 mm, seed diameter 9 mm, count 4, spacing 20 mm; bounds [-60,-40,0,60,40,11] mm. Pattern centers are (-40,20), (-20,20), (0,20), (20,20); independent holes remain diameter 12 at (-30,-20) and (35,-20). No recovery marker remains. Cold means closed saved document, a new controller/process and fresh native API resolution, not a SOLIDWORKS application restart. Only the user-started PID 22952 was attached; it remains running.

## Public Gate And Safety

The exact four subtype/parameter/accessor rows are scalar-qualified. `RequireExecutable` now permits a matching qualified row instead of always throwing. Production intake/live verification still requires complete healthy history, exact references, known dependencies, supported drivers and full plate geometry. Qualification is not granted from file names, Registry membership or Factory provenance. Unknown/Hole Wizard/two-direction patterns and other accessor combinations remain unsupported. Public EditSet explicitly returns CAPABILITY_UNAVAILABLE; scalar promotion cannot accidentally enable it.

- v13 public entry refuses invalid value, invalid unit, unsupported key and altered source fingerprint for each scalar; no native edit/checkpoint/revision. Its valid two-target request proves only that the EditSet gate remains closed, not atomic batch acceptance.
- v8 equation and blind/unknown sources refuse before initialization/mutation. The unknown history remains visible/read-only.
- v12 preparation changes only a disposable owned working copy: actual IDimension.DrivenState=1, then actual GeometryPattern=true with native ModifyDefinition and rebuild. Public scalar preflight refuses the relevant keys, with zero setter steps/checkpoints. Production journaled restoration reopens exact authoritative Revision 0 bytes; pointer/native hashes and complete Oracle are unchanged. These are negative preparations, not alternate scalar-edit implementations or M14B fault evidence.
- v6 official reference resolution returns null/nonzero status for a syntactically valid unresolvable reference; production Apply refuses STALE_REFERENCE before setter. This is not a genuine deleted-reference history test.
- v6 deliberately inconsistent scalar postcondition refuses STATE_DRIFT_DETECTED and rolls back exactly to Revision 7 native/pointer hashes and full geometry. This is not the M14B first-batch-edit failure matrix.
- Actual source-byte drift on disposable pure-test files is rejected before checkpoint. Native tests use forged request fingerprints and recheck all original hashes; no original CAD source was altered to manufacture a drift test.
- Unsupported accessor, partial inventory, missing reference, suppression, unknown dependency and false local-origin facts remain refused in pure/regression tests. Rich design-table/configuration/real-deletion/ambiguity cases remain M14C.

Origin recognition now requires a coincidence between the unique circle center and a point whose GetDefinitionEntities2 reference exactly matches the document's OriginProfileFeature point. A Type=10 proxy at coordinates zero alone is insufficient. v11 edits diameter 10 -> 11 mm, preserves center (0,0), saves Revision 1 and passes independent cold Oracle. Earlier origin failures in v6/v8 and read-only probes v7/v9/v10 are retained, not overwritten.

## Ledger And Originals

[Cumulative ledger](D:/CAD-Harness0.2/artifacts/milestone14/native-budget.json): **39 opens, 39 closes, 0 new Parts, 0 unresolved ownership**. M14A adds 25 opens to the retained 14; namespace totals are v6=7, v7=1, v8=3, v9=1, v10=1, v11=3, v12=3, v13=6. Failed opens/diagnostic attempts are included. The explicit human no-limit authorization is preserved in the new freezes; int.MaxValue is the ledger's representation, not a reset. Individual slots remain bounded. Core Revision 11, Origin Revision 1 and boundary Revision 0 have no recovery markers; all controllers exited and native before/after document sets are empty.

All four frozen Registry native hashes still match: core `7c13241a63799595b8259063b1ad2b5f45fe96b225a1c3d41cee7daf888339f9`; origin `5981106bd0086532a490d052dbdaac552159827ecbb9e22790a18e6b084214a9`; unknown `cd1a0d2f87bc15607c3459c7a6ca5bd75b60408660171596952ff0bf8a576100`; equation `9b11fa65142b2013d4ed0216567f3a99c9f881c561617a4965dd3bccc4645fbe`.

Current A1 remains `aae0907a25a0e4068e3d5362f85a7f9529525b4419d1d597b9db7f8449f64bac`; retained old A1 remains `7182c79520636053c4fed3bcde3c5b947e57f7550c921645db22f638d3af7eef`. Neither was edited or evaluated for compatibility in M14A. All four inputs are development fixtures, not engineer-authored or held-out qualification. Their success does not resolve A1's historical failures.

## Source And Regression

Production changes from c9de151: `ExternalNativeQualification.cs` resolves the actual local-origin source narrowly; `ExternalProfileOwnership.cs` encodes fail-closed relation facts; `V03NativeQualificationCandidates.cs` implements the exact qualified scalar gate; `ExternalPartSession.cs` dispatches scalar through that gate and explicitly keeps EditSet closed. The existing SetSystemValue3/cache-timing and durable transaction fixes were verified, not rewritten. No tolerance, Factory model, original, PRD acceptance requirement or unsupported-history policy was weakened.

M14-only runner changes add Revision-4 continuation, scalar/public/cold/boundary slots, no-limit authorization validation, immediate evidence and source-only small freezes. Contract tests track the actual gate. [Pure tests](D:/CAD-Harness0.2/artifacts/milestone14/pure/20261009T161345626/result.json): **83/83**. [Production regression](D:/CAD-Harness0.2/artifacts/milestone14/regression/20261009T161151723/result.json): all six suites pass (M11 76/76, M12 48/48, M13 27/27, state 13/13, transaction and construction); zero build warnings/errors. `git diff --check` passes. One intermediate test compile failed on a nonexistent enum member, was corrected before freezing, and consumed no native open.

## Deferred Work

**M14B:** real two-target atomic EditSet, aggregate checkpoint/revision, first-edit/rebuild/file/state/reopen fault matrix and authoritative recovery. Its public gate remains closed.

**M14C:** A1 current/old independent compatibility, engineer-authored/held-out supported histories, actual design tables/configuration changes, true native deletion/ambiguity, rename/reorder and complete negative/production acceptance. No held-out evidence is claimed. M14A COMPLETE does not mark original M14 COMPLETE or remove any PRD requirement.
