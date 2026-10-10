# M14C3 External Native Negative Evidence Completion

**M14C3: PARTIAL. Overall M14: PARTIAL.** Executed 2026-10-10 from HEAD `5339e6e` on `v0.3-main-update`, retaining production baseline `18470cb` and the [C1 audit](D:/CAD-Harness0.2/docs/milestone-14c1-boundary-audit.md). No production/PRD/Factory changes, model edits, new Parts, random downloads, C4 positive acceptance or M14A/B native reruns.

Two bounded read-only native probes completed. Independent affected-unsupported-history evidence is strengthened; genuine same-size ambiguous binding and an independent supported target with an unrelated unsupported branch remain **UNVERIFIED**. A successful diagnostic is not a successful ambiguity-refusal test.

## 1. Execution and New Evidence

Frozen [catalog](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v29/catalog.json), source/binaries/authorization and [C3 plan](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v29/c3-plan.json) precede the two native calls. Plan SHA256: `c659e19eac5a0cbee03a177eac1fa52aceb0249bc73fad59f8dec3ad6f4adfc7`.

| Probe | Actual native observations | Result |
|---|---|---|
| [Third-party Top Plate](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v29/results/thirdparty-1-diagnostic/result.json) | Official GetParents/GetChildren, profile facts, definition/end condition, dimension state, feature health and exact persistent-reference round trips | Diagnostic PASS; one open/close, controller 31668. Independent B2 graph corroborated. |
| [Engineer wrist](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v29/results/wrist-diagnostic/result.json) | Actual multiple ProfileFeature parents, their segment/circle counts, dimension kinds and cut end conditions | Diagnostic PASS; one open/close, controller 10564. Does NOT establish a qualified P0 ambiguity-refusal case. |

Each probe uses a fresh isolated copy with OpenDoc6 Silent/ReadOnly options, no Setter, rebuild, save, rename, configuration mutation or checkpoint. Native source/copy bytes remain identical after closure. Immediate numbered `steps/*.json` retain source identities, controller, time and cumulative ledger. Existing public qualification refusals are reused, not unconditionally repeated.

Only test `ExternalCatalog.cs` changes: read-only diagnostics now record native error/warning, underlying type, reference status/round-trip, actual sketch segments/circles, extrusion definition and whether dependency arrays were returned. No new resolver, qualification exception or test editing engine was added. Missing dependency arrays are explicitly recorded, not fabricated as proven empty graphs.

## 2. C3-A: Exact Identity Versus Ambiguity

PRD Sections 8/9 select an exact semantic target within document/configuration/revision/fingerprint identity; native references must resolve to the expected interface/subtype and owner. Section 15 nevertheless requires ambiguous same-size-feature refusal. M14 forbids query discovery; Section 9's query path is P1/M18. A size/name/position-only request is not an existing P0 binding operation.

- [v20 identity](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v20/results/identity/result.json) and [independent cold](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v20/results/identity-cold/result.json) already prove two different native targets of diameter 7, then 8 after rename/reorder, remain uniquely bound. Keep this as robustness PASS, not ambiguity refusal.
- Production rejects non-round-tripping/wrong-type/suppressed references; one consuming ProfileFeature is required, and HoleDimension requires exactly one diameter/radius candidate. Native-instance geometry also must map uniquely. These are still valid fail-closed boundaries, but static guards/pure tests alone are not their native ambiguity proof.
- Wrist Cut2 reference `aEIAAAEAAAD//v8AAAAAAGQAAAA=` has two actual ProfileFeature parents, references ending `AEIAAAA=` and `AGUAAAA=`. Both contain **five active segments**, one circle, not a single-circle profile. Cut2 is **blind**, depth 23 mm. Cut3 `aEIAAAEAAAD//v8AAAAAAHEAAAA=` is through-all but again has two five-segment sketch parents. All inspected physical references round-trip and native errors are zero.
- Multiple parent sketches do not prove two consumed profiles: one can be a dependency/reference sketch. Their equal native dimension values do not make two edit targets indistinguishable. The host itself has eleven profile segments; the model is outside the qualified rectangular/single-circle history.
- [Existing public wrist qualification](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v21/results/wrist-qualification/result.json) refuses `UNSUPPORTED_PARAMETER_DRIVER` at the host sketch before any editable session/current pointer/checkpoint exists. This is correct conservative intake, **not** `AMBIGUOUS_NATIVE_DIMENSION`, nor an isolated ownership-ambiguity refusal.

**Conclusion: true PRD same-size ambiguous-binding refusal remains UNVERIFIED.** This run found no actual qualified P0 case with an underdetermined native target/driver. It does not prove native ambiguity is impossible. No duplicate EditSet, malformed ID, fabricated observation, duplicate driven annotation or direct internal accessor call substitutes for the required public-boundary evidence. No M18 query was introduced and no production defect is established. Further work needs a real applicable native identity/ownership ambiguity case or an explicit resolution of the original requirement against P0's exact-target semantics; it cannot be silently waived in C4.

## 3. C3-B: Independent Unsupported History

Source: Josh Villbrandt's 2017 Mecanumbot native `Body Top Plate.SLDPRT`; fixed Git commit `c50a655aef61ac1a6340b551ec4e837e54e871c4`, Apache-2.0. [Provenance/blob record](D:/CAD-Harness0.2/artifacts/milestone14/thirdparty-candidates-v1/provenance.json). SHA256 `e12b9af9420b5748d5eccdfa4083fe4f15806a246bfb84552f9567922fb81499`. This independently authored pre-intake model is already screened development/negative material, **not fresh Held-out**.

### B2: Potentially Affected Unsupported Descendants

Proposed target is native base `Boss-Extrude1`, reference `aEIAAAEAAAD//v8AAAAAABwAAAA=`, depth **3.175 mm**. Native definition: blind, non-thin, one direction. This is an observed subtype candidate, not an advertised editable session on this source.

| Actual native path | References / support fact |
|---|---|
| Base -> Fillet1 -> Cut-Extrude1 | Fillet `aEIAAAEAAAD//v8AAAAAAB4AAAA=`; cut `aEIAAAEAAAD//v8AAAAAAHwAAAA=`. Parent and child arrays corroborate each path edge. Fillet has no qualified M14 accessor/material contract. |
| Base -> M4 Hole Wizard -> Mirror1 | Wizard `aEIAAAEAAAD//v8AAAAAAG0AAAA=`; mirror `aEIAAAEAAAD//v8AAAAAAG4AAAA=`. Native feature-seed dependencies corroborated; neither subtype is qualified by circular-cut/linear-pattern equivalence. |
| Base -> #6 Hole Wizard | Wizard `aEIAAAEAAAD//v8AAAAAAFUAAAA=`; actual base parent confirmed. |

All listed features are unsuppressed, error 0/warning false, and round-trip their references. A depth change alters host extent and potentially dependent material/topology; no independence can be assumed for these descendants. The full plate/cylinder contract cannot validate fillet, mirror or Hole Wizard intent. No trial mutation is needed to establish this unsafe eligibility boundary.

[Existing production CreateCopy refusal](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v21/results/thirdparty-1-qualification/result.json) returns `Inspectable`, `UNSUPPORTED_PARAMETER_DRIVER`, at host `Sketch1`. No public session is returned; package has no `current.json` or `recovery.json`. **Setter=0, checkpoint=0, committed revision=none** follow the qualified-entry control flow and absent authority; these are not a newly instrumented setter count. Original hashes match both old qualification and new raw probe.

**B2: PASS for independently sourced unsafe-history visibility and conservative production refusal before mutation.** The earlier driver gate wins; this is a combined whole-Part refusal, not isolated proof that the dependency-specific error would fire after removing other blockers. No source was repaired to isolate gates. Production's stronger fail-closed policy is permitted; its downstream facts are now independently corroborated without Factory substitution.

### B1: Unrelated Unsupported Branch

For the only observed supported scalar candidate (base depth), every material feature in the examined Top Plate is downstream. The apparent #6 versus M4/mirror branches are relative to Hole Wizard targets, themselves unqualified. Several leaf GetChildren calls return no array; the diagnostic captures this fact rather than inferring a complete negative reachability proof. Top-level enumeration and inherited parent descriptors are not a certified complete subfeature-graph absence proof.

The wrist probe also offers no qualified single-circle target. Factory B/C no-child evidence remains development provenance only. Previously inspected Front/Side/UAV sources offer no demonstrated qualified target with a complete unaffected unsupported-branch proof. **B1 remains UNVERIFIED (material/evidence gap)**, not a requirement to develop local editing. An independent source must contain a supported target and an actually unrelated unsupported material branch, with complete native ownership/graph evidence; conservative whole-Part rejection remains acceptable. No new random model search or local compatibility exception was attempted.

## 4. C3-C: Original Negative Requirement Matrix

Paths in this table are relative to `D:/CAD-Harness0.2/artifacts/milestone14/`. N = actual native state; I = controlled fault in actual production protocol; P = pure test. Detailed values are retained in the linked M14A/B/C reports. Evidence is reused without replay.

| PRD requirement | Specific existing results | Nature / production boundary | Status / remaining limit |
|---|---|---|---|
| Ambiguous same-size features; Sections 8, 15-16 | v20/identity + identity-cold; new v29/wrist-diagnostic | N/public exact-target success; public wrist intake refuses a different driver | **UNVERIFIED ambiguity refusal**; explanation above, not a query-feature mandate. |
| Unrelated/affected unsupported history; Section 15 | v22/dev_unknown_descendant-diagnostic + qualification; v21/thirdparty-1-qualification; v29/thirdparty-1-diagnostic | N/Factory plus independent native graph/public qualification | B2 PASS conservative refusal; **independent B1 UNVERIFIED**. |
| Deleted/changed persistent reference; Sections 8, 15 | v25/deleted | N/DeleteSelection2, null/status 1; public edit preflight refuses STATE_DRIFT_DETECTED; zero Setter/checkpoint; exact authority/Oracle restored | PASS. v20 marker-only quarantine is not substitute evidence. |
| Suppressed target; Section 15 | v25/suppressed | N/SetSuppression2/IsSuppressed; public preflight refusal and restoration | PASS. |
| Changed configuration; Sections 8, 15 | v28/configuration; v22/suppressed-default-diagnostic | N/actual active configuration change; public stale edit refuses FEATURE_REBUILD_FAILED; restores authority | PASS. v23/v26 failed assertions remain failures. |
| External source-byte drift; Sections 8, 15 | v20/source-drift | N/disposable source actually changed; public SOURCE_FILE_DRIFT, zero native edit/checkpoint/revision; exact source restored | PASS; not just a forged request hash. |
| Rename/reorder without retargeting; Sections 8, 15 | v20/identity + identity-cold | N/ReorderFeature and rename; public batch plus separate-controller full Oracle | PASS development robustness; independent equivalent histories still C4. |
| Invalid scalar/batch, old value/units/revision, unsupported accessor, no safe order; Sections 8-9 | v13/scalar-public; v15/public; pure/20261010T035730796/result.json | Public preflight + P/contracts/dependency cycles; zero edit/checkpoint where rejected | PASS; no ambiguous-target claim from duplicate requests. |
| Unqualified native driver/pattern; Section 8 | v12/boundaries | N/actual driven dimension and GeometryPattern; public scalar refusal before Setter/checkpoint, journaled authoritative restoration | PASS; no new qualification. |
| Equation driver; Section 8 | v22/dev_equation_driver-qualification (also v8/equation) | N/actual equation-bearing source; public qualification refuses UNSUPPORTED_PARAMETER_DRIVER | PASS development negative; not independent positive. |
| Design Table driver; Section 8 | v24/vendor-cavity-diagnostic; v27/vendor-cavity-qualification | N/HasDesignTable=true, IsDesignTableDimension=true, depth 0.086 m; public intake combined-driver refusal | PASS conservative non-advertisement. Dynamic/isolation UNVERIFIED but NOT an added M14 gate; no Excel installation. |
| Native rebuild failure; Sections 9, 15 | v14/native-rebuild | N/actual ForceRebuild3=false, error 51; production backend/coordinator restores exact authority/full Oracle | PASS; internal candidate route, not an additional public or mock rebuild proof. |
| File publish/save boundary; Sections 9, 15; M14 | v14/faults | I/real setters/saved files, BeforeNativeSave/AfterNativeSave exceptions; production durable protocol rollback | PASS fault injection; no disk-full/Save3 HRESULT claim. |
| State publish, before/after commit point; Sections 9, 15 | v14/faults + postpointer-recovery | I/actual protocol; prior authority before pointer; committed R1 after pointer, quarantine then new-controller recovery | PASS. Post-commit result is not ordinary rollback. |
| First/last edit, postcondition, rollback failure; Section 9; M14 | v14/faults; v15/public + boundaries + rollback-recovery | Actual Setters + I; includes public EditSet first-edit rollback, last/postcondition boundaries and failed-rollback isolation/new-controller restore | PASS. One checkpoint/revision semantics already qualified. |
| Interrupted reopen; Sections 9, 15 | v14/interrupted + interrupted-recovery | I/real saved verification boundary; old revision/quarantine retained, separate controller verifies native/State/Oracle before recovery completion | PASS controlled interruption; not OS process termination. |

Fault injection is not mislabeled as naturally occurring disk/OS faults. Candidate-backend evidence is not mislabeled public execution. Fresh source materials and Held-out coverage remain separate from established safety behavior; no whole-matrix rerun is needed merely to change provenance.

## 5. Preservation, Accounting and Regression

- Cumulative **121 opens / 121 closes / 0 new Parts / 0 unresolved ownership**; this task adds exactly two opens and two closes to 119/119. No current recovery marker outside deliberately retained pure/regression fixtures.
- Attached only user-started SOLIDWORKS **PID 6612**, never started/terminated it. Both native controller document sets are `before=[]`, `after=[]`; each probe closes only its owned copy. Ten new lifecycle events record GDI **1516-2136**, below 7000; guards pass.
- Current ledger SHA256 `aea3b304076334e8ff49c572ec520ed428d826b0d1d12304e24d846cfa40d2ca`. Reconstructing its pre-C3 counters/slot list and removing only the ten new events yields the exact previously audited SHA256 `e7d11da2edac4cb1dcb6df8cb3f9e2705831d53d1326b5a226872a4e5efcd554`: all **506** historical events and prior budget content preserved. The first Git-based snapshot lookup failed because artifacts are untracked; its null comparison was discarded, not used as evidence.
- [Final offline audit](D:/CAD-Harness0.2/artifacts/milestone14/final-m14c3-v1/audit.json) verifies **2,504 frozen identities**, original source/Manifest/Reader records, retained failures, no native ownership/recovery leak and the existing 309-event older prefix. Separately all **17 unique catalog/Registry model sources** match their frozen hashes.
- Wrist original SHA256 remains `6522eed09f6ecea96570512324455b0bf26d2da673df8755915b9d83e25bd107`; Top Plate hash above unchanged. A1 current/old and all four Factory sources remain untouched; this task never opens A1 or Factory sources.
- All **91 nongenerated production `.cs` files** still match public-tested v15; original PRD SHA256 remains `17412193a33228e376fe4cecd24576e3d0b6a5a4a5b43f62d4dd568235026275`. M14A/B qualification flags, tolerances and persistence protocol are unchanged.
- Build: zero warnings/errors. [Pure regression](D:/CAD-Harness0.2/artifacts/milestone14/pure/20261010T035730796/result.json): **86/86**. Prior six-suite production regression reused; no unnecessary native rerun. `git diff --check` is the final delivery check.

## 6. Remaining Materials and Stage Status

**M14C3 is PARTIAL, not COMPLETE:** all original negative clauses now have explicit evidence mapping, but applicable genuine ambiguous-binding refusal and independent unrelated-unsupported-feature coverage are missing. Neither implies a demonstrated production defect; no C2 fix is justified by these probes. All executable bounded probes finished with safe cleanup, so the entire task is not labeled BLOCKED.

Materials still needed before final closure:

1. For the unresolved negative row, a real P0 identity/profile/accessor ambiguity case whose refusal can reach an applicable production boundary without earlier unrelated disqualification. Explain its relationship to the PRD same-size requirement before execution; do not substitute a new M18 query or malformed request. If no such evidence exists, retain UNVERIFIED rather than reinterpret completion.
2. An independently sourced pre-intake native Part with a supported target and a truly unrelated unsupported material feature; complete native reference/ownership/dependency evidence. Rejecting the entire Part is acceptable. Freshness is unnecessary for this negative role; it must not then be promoted to fresh Held-out.
3. C4's independently authored supported positive source covering four scalar rows, two independent targets and separate-controller cold; at least one fresh, pre-frozen untuned external history/parameter combination; a measured geometrically equivalent different-history counterpart (editable only if qualified, otherwise read-only). Roles may share material only if their facts and predeclared custody prove it.

No new C4 source was selected, tuned or formally tested. Do not ask C4 to re-run the proven deletion/configuration/recovery matrix. Preserve all rejected materials and failures; close the two specific residual negative gaps or explicitly retain M14 PARTIAL.
