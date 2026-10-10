# M14 Close-out

**M14: PARTIAL. Executable close-out work finished; one required negative remains UNVERIFIED.**

Executed 2026-10-10 on `v0.3-main-update`, starting HEAD `aea97a3`. Third-party searching is stopped. No production/Factory changes, new Parts, original-file edits, gate promotion or tolerance changes. The explicit user-authorized amendment to PRD Section 15 removes third-party/pre-intake provenance as M14 completion gates, not technical safety requirements. Earlier reports and failures remain valid under their original scope.

## 1. Four Native Scalars

All calls below use `ExternalPartSession.Edit(ScalarEditRequest)`, the production Handler, transaction, durable store and live qualification. They are not Factory Reader results or standalone editing macros.

| Parameter | User-model public edit | Native Setter / rebuilt readback | Revision / independent cold | Corresponding negative reused |
|---|---|---|---|---|
| ExtrusionDepth | 14 -> 17 mm | ModifyDefinition=true; 17 -> 17 | R2 -> R3, one checkpoint; final R5 cold PASS | Actual equation driver, v22 qualification refusal |
| HoleDiameter | Hole B 11 -> 14 mm; later batch 14 -> 13 | SetSystemValue3 status 0; circle and solid correct | v32 R1/R2, separate-controller cold PASS; final R5 remains 13 | Actual driven dimension, v12 refusal |
| PatternCount | 4 -> 3 total instances | ModifyDefinition=true; 3 -> 3 | R3 -> R4, one checkpoint; final R5 cold PASS | Actual GeometryPattern, v12 refusal |
| PatternSpacing | 26 -> 23 mm | ModifyDefinition=true; 23 -> 23 | R4 -> R5, one checkpoint; final R5 cold PASS | Actual GeometryPattern, v12 refusal |

New [frozen schedule](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v33/schedule.json), [scalar result](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v33/results/manual-remaining-scalars/result.json), and [cold result](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v33/results/manual-final-cold/result.json). Numbered `steps/*.json` immediately preserve requests, exact references, Setter status, immediate/rebuilt values, complete Oracle differences, publication boundaries and lifecycle counters. Prior diameter and batch evidence: [v32](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v32/results/manual-scalar/result.json); no successful step was replayed merely to replace its report.

The continuing manual package really began at **R2**, not R0/R1. New controllers **21548 -> 5196** prove separate-process final cold reopening. All five feature references round-trip unchanged. Final dimensions are depth 17, seed diameter 7, B diameter 13, C diameter 6, pattern count 3 and spacing 23.

Independent full Oracle uses the frozen **144 x 96** manual specification and actual native faces, not Factory dimensions or production geometry helpers. Final volume measured/expected: **230308.17739022966 mm3**; bounds `[-72,-48,0,72,48,17]`. Five cylindrical holes have the declared diameters, Z boundaries 0/17 and centers `(-45,24),(-22,24),(1,24),(-34,-26),(38,-26)`. Baseline and each intermediate revision also passed complete material, envelope, cylinder, untouched-target and native definition checks.

Native / State / external companion / Manifest agree at R5; no recovery marker. Final working/revision native SHA256: `1910ab858d2d4df8a4ea528595fbbab5139badd661bf583a435ea6216a530561`.

## 2. Atomicity and Recovery Already Proven

| Requirement | Reused actual evidence | Result / limitation |
|---|---|---|
| User public dual-hole EditSet | v32 `manual-batch` + `manual-batch-cold`: B 14 -> 13, C 5 -> 6 | Two real Setters, one aggregate checkpoint, one R1 -> R2, complete Oracle/durable cold PASS; NOT two Scalar commits |
| Qualified cross-type EditSet | v15 `public` + `public-cold`, diameter + spacing | Public atomic transaction PASS, not a same-Handler special case |
| First-edit and postcondition faults | v14 `faults`; v15 `public`/`boundaries` | Actual partial native changes, no unintended commit, exact rollback or safe quarantine PASS |
| Real rebuild failure | v14 `native-rebuild` | ForceRebuild3=false, native error 51, restored bytes/full Oracle PASS; not mock proof |
| Native/file publication and State publication | v14 `faults` + `postpointer-recovery` | Real production-protocol injection PASS; before-pointer rollback versus after-pointer committed authority correctly distinguished |
| Rollback failure and interrupted reopen | v15 `boundaries`/`rollback-recovery`; v14 `interrupted`/`interrupted-recovery` | Quarantine/marker retained, new-controller authoritative recovery and Oracle PASS |

Paths are under `artifacts/milestone14/acceptance-v*/results/`; [M14B report](D:/CAD-Harness0.2/docs/milestone-14b-verification.md) retains detailed links. No disk-full, natural Save3 failure or OS-killed controller claim is added. These tested production mechanisms were not re-run just to change model provenance.

## 3. Source-adjusted History and Held-out Coverage

- **User source:** genuinely GUI/manual-authored, previously inspected and guided by the disclosed supported-model specification. Now valid external positive acceptance; NOT a fresh, untuned-source third-party model. Its original SHA256 remains `60fbd4101e4c1f3c5ca129c7e5d44b1eadf28c50b6483e0259d06b4b716f9a8b`.
- **Task-level Held-out PASS under the amended rule:** the new depth/count/spacing combination **17/3/23** was separately frozen before native execution and never used for production tuning. Source, tasks, order, expected geometry and controller binary are hashed in v33. This does not relabel previously disclosed source history as fresh. Production remains byte-identical to its v15 tested baseline.
- **Bounded equivalent parametric-history comparison PASS:** [direct](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v33/results/equivalent-direct/result.json) and [equation](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v33/results/equivalent-equation/result.json) each independently measure the same full 120 x 80 x 10 plate and five-cylinder geometry. Both actual volumes are `93423.89402405635 mm3`. The direct history has zero equations; the other has actual native `"D1@FF_Host" = 10mm`. The direct history's qualified public edits/cold pass in v13; the equation history remains inspectable/read-only through v22 production refusal. This tests different native driving histories, not arbitrary alternate solid-construction sequences or universal history compatibility. Both remain Factory development data; the source amendment permits their honest role.
- **A1:** previous failures/read-only status unchanged. No attempt to force A1 qualification and no replacement of its old results with the user-model success.
- Previously acquired third-party source/provenance/search records remain untouched historical material. No further download, search, screening or source-promotion was performed.

## 4. Required Negative Matrix

| Boundary | Evidence | Close-out status |
|---|---|---|
| Invalid values/units/accessors/old values, duplicate/missing batch target, selection/revision mismatch, impossible geometry/order | v13 public Scalar; v15 public batch; 86 pure tests | PASS at pre-Setter/pre-checkpoint boundary; NOT binding-ambiguity evidence |
| Deleted reference / suppression / configuration / actual source-byte drift | v25 deleted/suppressed; v28 configuration; v20 source-drift | PASS, real native or source state, public refusal and authority restoration |
| Rename/reorder, equal-size distinct exact targets | v20 identity + cold | PASS robustness; exact persistent references prevent arbitrary retargeting |
| Unsupported native driver/pattern / equation / actual Design Table | v12 boundaries; v22 equation qualification; v24 native table diagnosis + v27 qualification | PASS refusal; table editing/dynamic Excel behavior not advertised or added as a gate |
| Affected unknown descendant | v22 production refusal; new v34 raw dependency counts | PASS: native host -> 2 mm blind cut; unsupported material history refuses before mutation |
| Unrelated unsupported branch | [v34 diagnostic](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v34/results/dev_unknown_descendant-diagnostic/result.json), v22 qualification | PASS under amended provenance; explanation below, no permission for local editing |
| Real ambiguous binding refusal | v20 exact-target success, v29 wrist diagnosis; original PRD Sections 15/16 | **UNVERIFIED; sole unresolved M14 completion condition** |

The existing unknown-descendant fixture contains healthy single-circle through holes B/C, with independent consumed sketches and exact references, plus an unsupported blind cut on a separate host branch. New official `IGetParentCount/IGetChildCount` values corroborate every returned top-level dependency array. **B and C each have zero native children**, not merely a missing array interpreted as proof; the blind cut has host/profile/plane parents and no B/C parent. Host has five real children including the blind cut. No downstream dependency path starts at B/C. Source/profile references round-trip and errors/warnings are zero. This satisfies the fixture-level unrelated/affected cases; whole-Part conservative refusal in v22 remains correct and unchanged. This is NOT proof of safe editable intake beside unknown material, nor a general relaxation of dependency verification.

**Why M14 remains PARTIAL:** P0 dispatch uses document/configuration/revision/semantic ID/exact persistent reference, not dimension-based discovery. Two equal-size targets do not make that binding ambiguous. Multiple owners or accessor candidates would need real, applicable native evidence and refusal at the public boundary. Existing wrist evidence rejects an earlier unrelated driver and cannot count as that proof. No duplicate request, invalid reference string or synthetic annotation is substituted. The user authorized changing source conditions only, so the original mandatory ambiguous-binding refusal is retained. No production defect has been demonstrated; no further random search or trial loop is warranted. Closing this last item needs either a real P0 native identity/ownership/accessor ambiguity case or a separately authorized PRD clarification, not another author or forced A1 compatibility.

## 5. Preservation and Delivery

- This run adds **8 opens / 8 closes / 0 new Parts**: v33 scalars 4, cold 1, direct/equation read-only 2, v34 dependency read-only 1. Cumulative **145/145**, `OwnedTitles=[]`, no native recovery markers outside retained pure/regression fixtures. Unlimited human authorization is recorded; old counts/failed attempts are not reset. All five controllers report initial/final document sets empty.
- User-started SOLIDWORKS PID **21772** was attached, never started/terminated; both pre-existing processes 20688/21772 still respond. Lifecycle GDI 1672-2126 remains below the 7000 guard. Only owned disposable/managed copies were closed.
- All four Factory native hashes, Manifest/Reader records, A1/external sources and original manual source are preserved by [final preservation audit](D:/CAD-Harness0.2/artifacts/milestone14/final-closeout-v4/audit.json): **3,079 unchanged frozen identities**, **592 prior lifecycle events** intact. Three live source identities (PRD and the two edited test files) are explicitly superseded, with old bytes verified in immutable v29 archives and new bytes frozen in v34. They are not falsely claimed unchanged.
- The old generic offline audit refuses those intentionally changed live-source hashes. Three interrupted offline audit attempts and their actual diagnostics are retained in the final audit; no native calls, model failure or historical PASS conversion resulted. The explicit supersession audit passes without changing any old freeze or evidence.
- **91/91 nongenerated production `.cs` files match v15**. Public four-Scalar and atomic gates remain enabled only for already qualified subtype/accessor combinations; live model qualification is still mandatory. No new feature capability is introduced.
- Build zero warnings/errors; final [pure result](D:/CAD-Harness0.2/artifacts/milestone14/pure/20261010T113632519/result.json) **86/86**. Six production suites from `regression/20261010T020606270/result.json` are reused because production did not change; M14A/B native suites are not repeated. `git diff --check` passes.
- Source changes: PRD Section 15 source policy only; `NativeAcceptance.cs` adds the fixed close-out public sequence/cold checks and read-only history Oracle; `ExternalCatalog.cs` adds native count/array corroboration; `freeze-milestone14-closeout.ps1` freezes this bounded run. No production file changed. M15-M20 untouched.

**Final disposition:** supported four-Scalar/public Atomic EditSet/persistence/recovery functionality is verified and usable within the advertised bounded plate contract. This close-out stops sourcing and completes available tests. **Overall M14 remains PARTIAL solely because actual ambiguous-binding refusal has no qualifying native evidence.** M14A/B remain COMPLETE; no historical failure is erased, and no claim of general external-Part compatibility or complete v0.3 acceptance is made.
