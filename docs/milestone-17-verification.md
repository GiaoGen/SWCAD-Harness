# M17 Planner Composition And Partial-Dimension Intent

**M17: COMPLETE for the bounded scope of PRD Milestone 17.**
Baseline `c313e0b`, branch `v0.3-main-update`; verified 2026-10-11.
The PRD, M14-M16 qualifications, Fixture Factory and M18-M20 are unchanged.

## Implemented Boundary

- `ConstructionPlanner` is a separate v0.3 path; the v0.2 planner remains intact. Each request produces one complete program through one source call, never a feature-by-feature CAD/provider loop or automatic execution.
- Input is intent plus explicit typed Given/Unresolved requirement anchors. It does not claim general free-text engineering-dimension extraction. Native API names, feature order and native references need not appear in user text.
- Finite normalization rule version `1`: `layout_zero`, `layout_zero_angle`, `half_dimension`, `copy_dimension`. Each derived/defaulted fact is disclosed in an immutable requirement record. Layout defaults cannot fabricate size, fit, material, strength or tolerance requirements.
- Strict closed/tagged provider envelope distinguishes `planned`, `needs_clarification`, and `unsupported`. Provider failures, invalid plans and cancellation remain distinct outcomes. Every new dimension/placement is bound to an accepted requirement and checked against geometry; explicit given/critical facts cannot silently disappear.
- Backend-owned capabilities project the six qualified M15/M16 construction kinds, typed initial references, actual output rules and exact committed prefix. Once a body exists, another initial extrusion/revolution is not advertised. Unsupported external/reopened histories and rotational continuation do not acquire execution authority.
- Planning profile subset: one local rectangle, circle or local-X slot. Frames use positive principal normals and disclosed in-plane rotation. Native direction accessors are not implied; offset sketches require explicit tracked datums. M15 analytically declared constraints are not relabeled as native associative constraints.
- Pure preflight uses existing sketch/feature geometry proofs, checks spatial references and the complete immutable prefix, and rejects unproved material combinations before mutation.
- Preview freezes request, response, context, revision and SHA256. Explicit one-use approval rechecks the fresh native context and full preflight before the existing `ConstructGeneric` transaction. Offline new-model/snapshot previews cannot authorize a live session.
- CLI `--v03-request <request.json> (--fixtures <map.json> | --llm) [--output <preview.json>]` exposes preview only. Live execution uses `SolidWorksExecutionContext.ExecuteConstructionPlan(approval)` with a preview obtained for that owned live context.

This remains M16's **live-session/configuration-local managed construction**. Revision commit is not a generic durable manifest publication. Saving native bytes is verified; cross-controller generic cold reopen/resume is not claimed and remains a later release/recovery concern. External M14 durability is unchanged.

## PRD Evidence Matrix

Paths below are immutable local evidence under `D:/CAD-Harness0.2/artifacts/milestone17/`.

| Required fact | Evidence and result |
|---|---|
| Explicit dimensions | `live_v3/explicit-result.json`: live Planned; strict typed program and all-Given anchors |
| Omitted noncritical details | `live_v3/omitted_layout-result.json`: live Planned; 62 x 46 x 9 mm, disclosed zero layout defaults |
| Given/Derived/Defaulted second trace | `live_v5_targeted/derived_radius-result.json`: live Planned; given diameter 18 and depth 14 mm, rule-derived radius 9 mm, disclosed layout |
| Two critical-unknown refusals | `live_v3/missing_envelope-result.json` and `missing_interface-result.json`: live NeedsClarification, no program; final native refusal also leaves state unchanged |
| Held-out parameter combination | `live_v3/heldout_dimensions-result.json`: live Planned for 90 x 53 x 7 mm, not used as a dimension-specific production branch; pure independent combination 73 x 41 x 11 also passes |
| Unsupported request | `live_v3/unsupported-result.json`: live Unsupported for required strength/fatigue certification, no executable program |
| Actual current capability/prefix | Recorded provider prompts; pure capability/schema tests; live `native_v2/call-01-response.json` produces a complete continuation with exact prefix |
| No auto mutation | Pure tests; final native snapshots unchanged by planning; explicit approval required for each execution |
| Executable plan and small native smoke | Final public execution in `native_v4_final`, source hashes checked against delivered files |
| No unavailable external target | Typed spatial/material preflight and capability refusal tests; offline snapshot cannot become live authority |

Real provider: requested alias **`deepseek-chat`**, endpoint `https://api.deepseek.com/responses`; all 26 returned aliases are **`deepseek-flash`**. Returned usage totals: **168310 input / 25279 output tokens** across the development runs. Individual request/response envelopes, aliases and usage are preserved. These totals include failed plans and are not a 26/26 planning-accuracy claim.

`live_v3` passed 6/7 expectations; its circle self-reference failure remains preserved. Tagged schema and reference/output guidance were corrected, and targeted `live_v5_targeted` passed that case. The final acceptance mapping combines those explicit live results; it is not presented as a fresh full-suite accuracy measurement. Native replay/CLI fixture results never count as new model inference. Historical live schedules serialized tuple rows as empty objects; actual full requests remain in immutable `call-*-request.json`, while result previews preserve accepted request/response bytes. No old schedules were rewritten.

## Final Native Smoke

Final freeze/result: [schedule](D:/CAD-Harness0.2/artifacts/milestone17/native_v4_final/schedule.json), [result](D:/CAD-Harness0.2/artifacts/milestone17/native_v4_final/result.json).

| Operation | Native/transaction result | Independent Oracle |
|---|---|---|
| Explicit approval: initial prism | Existing production sketch/extrusion handlers; committed revision 0 -> 1 | 62 x 46 x 9 mm; expected volume 25668, measured 25667.999999999996 mm3 |
| Explicit approval: complete continuation | Exact two-operation prefix preserved; circle sketch plus through-all cut; revision 1 -> 2 | Diameter 6 mm, centered Z-axis cylinder; expected 25413.530995059227, measured 25413.530995059224 mm3 |
| Reused approval | `PREVIEW_EXPIRED` before second execution | No extra transaction |
| Critical missing continuation diameter | NeedsClarification, no preview/execution | Native/state snapshot unchanged |
| Save and close | Ordinary native Save3 succeeds, file hash retained, owned document closed | User document inventory unchanged; no cold-reopen claim |

Independent Oracle reads native mass properties, six extreme supports and all face surface types/cylinder parameters directly, without calling production material integration. Both stages have supports `[-31,-23,0,31,23,9]` mm, centroid `(0,0,4.5)` mm and volume error about `-3.64e-12` mm3. It expects zero/one cylindrical faces respectively; remaining surfaces must be planar.

Key final steps: `026-new-part-execution.json`, `027-independent-oracle.json`, `030-continuation-plan.json`, `043-continuation-execution.json`, `044-independent-oracle.json`, `047-critical-refusal-no-mutation.json`, `048-saved.json` under `native_v4_final/steps/`.

The final run uses disclosed **fixture replays** of real root/continuation/refusal responses. `native_v2` separately proves the actual live continuation and live critical refusal through the production path. Final execution is on a distinct owned copy of the pristine M17 blank, not a Factory or engineer original.

## Regression And Historical Failures

[Final regression](D:/CAD-Harness0.2/artifacts/milestone17/regression/20261011T104724041/suite-results.json): solution Release build, zero warnings/errors; **614/614 pure tests** across 12 suites.

| Suite | Passed |
|---|---:|
| IR / existing Planning | 80 / 47 |
| M11 / M12 / M13 / M14 | 77 / 48 / 27 / 86 |
| State / mutation transactions / construction transactions | 13 / 33 / 22 |
| M15 / M16 / M17 | 25 / 20 / 36 |

M17 tests cover requirement provenance, immutable records/preview, contradictory or undisclosed dimensions, two critical unknowns, strict schemas, unavailable capability, exact continuation, prefix tampering, unsafe spatial bindings, offline authority refusal, one-use approval, context/revision binding and single-call/failure behavior. [CLI fixture smoke](D:/CAD-Harness0.2/artifacts/milestone17/cli_v1/result.json): Planned, zero native Parts, execution unauthorized.

Preserved development failures:

- `live_v1`, `live_v2`, `live_v3`, `live_v4_targeted`: invalid reference suffixes, sketch constraints/arity, duplicate circle drivers or binding IDs safely rejected. No native mutation was used to repair provider output. Each new request followed a concrete prompt/schema diagnosis, not hidden automatic retries.
- `native_v1`: live plan rejected before mutation; blank Part saved and closed. `native_v2`: live continuation PASS. `native_v3_final` and `native_v4_final`: recorded-response public execution PASS.
- Earlier isolated regression namespaces retain missing CLI import, script argument and baseline schema/assertion failures. Three legacy schema enum arrays now match generator ordering; allowed values/version are unchanged. Six stale legacy M7 assertions now acknowledge already-qualified M9 circle/pattern/direction capabilities; production legacy gates were not changed.

## Source And Resource Audit

New production files: `ConstructionRequirements.cs`, `ConstructionPlanner.cs`, `ConstructionPreflight.cs`, `SolidWorksConstructionPlanningRuntime.cs`. Existing CLI gains the explicit v0.3 route. New M17 tests/scripts and solution registration provide focused verification. No production native Handler, qualification flag, Factory or external editing code was changed.

[Final audit](D:/CAD-Harness0.2/artifacts/milestone17/final-audit-v1/result.json) confirms production/native-test hashes match the final native freeze, all regressions pass and protected hashes remain intact. PRD, user `M14测试用夹具.SLDPRT`, Factory frozen registry/Parts/manifests/proofs/ready files and M14-M16 ledgers were protected. User Part SHA256 remains `60fbd4101e4c1f3c5ca129c7e5d44b1eadf28c50b6483e0259d06b4b716f9a8b`.

M17 cumulative ledger: **1 creation attempt, 1 Part created, 3 open attempts, 4 closes, 0 unresolved ownership**. The initial creation and three distinct working-copy sessions account for all four closes. Native STA, responsiveness/GDI and owned-document guards passed; no user document was closed and SOLIDWORKS was not started or terminated by the harness. Unlimited-budget authorization is recorded; totals also stay within M17's original 2-Part/3-open smoke allowance.

## Outcome

**M17 COMPLETE**: supported bounded requests produce strict executable/preflighted plans; omitted noncritical construction details have disclosed finite provenance; missing critical and unsupported requests refuse without mutation; real-provider and final public native smoke evidence exist.

This does not complete M18-M20, arbitrary external-Part continuation, natural-language engineering inference, generalized native associative sketches or generic cross-controller durable reopen. Those capabilities were not added or claimed.
