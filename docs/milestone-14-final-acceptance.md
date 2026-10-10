# M14 Final Acceptance and M15 Entry

**M14: COMPLETE under the explicitly amended acceptance scope. M15: eligible to begin, not implemented or accepted.** Decision dated 2026-10-10, production baseline `cd5e8e5` on `v0.3-main-update`. This is a new disposition; it does not rewrite the earlier [PARTIAL close-out](D:/CAD-Harness0.2/docs/milestone-14-closeout.md), historical failures or native measurements.

## Approved Scope

The user authorized two separate PRD revisions: accepting the manual model/existing development fixtures with honest provenance, and allocating ambiguity acceptance to the actual interfaces. PRD Sections 15/16 and M14/M18 now record both decisions. The manual source is previously inspected/guided, **not** a fresh third-party Held-out model. Its frozen, untuned task combination 17 mm depth / 3 instances / 23 mm spacing satisfies the amended task-level Held-out rule without production tuning.

M14 exact-target binding must survive equal sizes, rename/reorder and cold reopen. M18 must verify under-specified query/selection ambiguity and preview confirmation. Genuine P0 nonunique inventory/owner/accessor guards remain mandatory; their actual native ambiguity branch is **UNVERIFIED**, not PASS, but manufacturing an unavailable case no longer independently blocks M14. M15's spatial-reference ambiguity refusal remains unchanged.

## Required Evidence

All paths below are under `D:/CAD-Harness0.2/artifacts/milestone14/`. PASS refers to the recorded native trial, not this offline review.

| Required condition | Actual evidence | Result |
|---|---|---|
| ExtrusionDepth: supported subtype, exact reference, before/after, Setter, full geometry, save/cold, negative | v13 `scalar-public`/`public-cold`; v33 `manual-remaining-scalars`/`manual-final-cold`; v22 equation refusal | PASS; manual 14 -> 17 mm, R2 -> R3 |
| HoleDiameter: unique driving circle dimension and complete through-hole geometry | v32 `manual-scalar`/`manual-scalar-cold`; v12 driven-dimension refusal | PASS; B 11 -> 14 mm, R0 -> R1 |
| PatternCount: single direction, seed/direction retained, all instance positions | v13 public/cold; v33 manual sequence/cold; v12 GeometryPattern refusal | PASS; 4 -> 3, R3 -> R4 |
| PatternSpacing: spacing/centers, untouched count/seed, complete geometry | Same pattern evidence | PASS; 26 -> 23 mm, R4 -> R5 |
| Public two-target batch, one aggregate checkpoint/revision, saved Native/State/Companion/Manifest | v32 `manual-batch`/`manual-batch-cold` | PASS; B 14 -> 13 and C 5 -> 6, one R1 -> R2, not two Scalar commits |
| Cross-type atomic batch, legal final geometry and safe ordering | v15 `public`/`public-cold`, diameter + spacing | PASS |
| First-edit/late-edit/postcondition failure, exact restoration or quarantine | v14 `faults`; v15 `public`/`boundaries`/`rollback-recovery` | PASS; real partial native edits and new-controller recovery |
| Actual native rebuild failure | v14 `native-rebuild` | PASS; ForceRebuild3=false/error 51, full rollback Oracle |
| Native/file and State publication before/after commit point | v14 `faults`/`postpointer-recovery` | PASS; real protocol injection, correct authoritative Revision; not a natural disk-full claim |
| Interrupted reopen and durable recovery | v14 `interrupted`/`interrupted-recovery` | PASS; new controller, preserved scene; not an OS-killed SOLIDWORKS claim |
| Invalid values/units/targets, mismatch, dependencies, impossible final/intermediate geometry | v13 public Scalar; v15 public batch; 86 pure tests | PASS; pre-Setter/pre-checkpoint refusals |
| Deleted reference, suppressed target, configuration/source drift | v25 `deleted`/`suppressed`; v28 `configuration`; v20 `source-drift` | PASS; actual native/source changes on isolated copies |
| Equal-size exact targets, feature rename/reorder, no arbitrary fallback | v20 `identity`/`identity-cold` | PASS robustness, **not** ambiguity refusal |
| Equation and real Design Table drivers, unknown/unrelated/affected history | v22 equation/unknown qualification; v24 native table diagnosis + v27 refusal; v34 dependency diagnostic | PASS refusal; whole-Part conservative safety unchanged |
| Geometrically equivalent, differently driven native histories | v33 `equivalent-direct`/`equivalent-equation`; v13 direct public/cold + v22 equation refusal | PASS bounded direct/equation comparison; not arbitrary structural histories |
| External manual Part and untuned task combination | v32 manual public Scalar/batch/cold; frozen v33 schedule and manual sequence/cold | PASS under approved source/task rule, not independent-source relabeling |

Detailed parameter, fault and negative evidence remains in [M14A](D:/CAD-Harness0.2/docs/milestone-14a-verification.md), [M14B](D:/CAD-Harness0.2/docs/milestone-14b-verification.md) and [close-out](D:/CAD-Harness0.2/docs/milestone-14-closeout.md). A1's original failures/read-only disposition remain unchanged and do not require a special compatibility branch.

## Geometry and Authority

The user-model Oracle uses its frozen **144 x 96** specification and independently reads actual native surfaces/definitions, not Factory dimensions. Every accepted mutation records full intermediate geometry. Final R5 has depth 17, A/B/C diameters 7/13/6, count 3, spacing 23; volume actual/expected **230308.17739022966 mm3**, bounds `[-72,-48,0,72,48,17]`, five through cylinders at `(-45,24),(-22,24),(1,24),(-34,-26),(38,-26)`.

Separate mutation/cold controllers **21548/5196** verify final R5, exact references, Native/State/Companion/Manifest consistency and complete Oracle: [scalar sequence](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v33/results/manual-remaining-scalars/result.json), [independent cold](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v33/results/manual-final-cold/result.json). Authority is the v32 manual package's [current pointer](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v32/packages/manual/current.json), not R0/R2. Final native SHA256 is `1910ab858d2d4df8a4ea528595fbbab5139badd661bf583a435ea6216a530561`. Original manual SHA256 remains `60fbd4101e4c1f3c5ca129c7e5d44b1eadf28c50b6483e0259d06b4b716f9a8b`.

## Safety and Revalidation

The [new offline audit](D:/CAD-Harness0.2/artifacts/milestone14/amended-acceptance-v2/audit.json) rehashes 3,079 preserved identities, the old PRD archives and explicitly superseded live source files; verifies 32 reused qualification result slots, R5 package members, original sources and the unchanged ledger. Previous PRD bytes remain in v29/v34 archives; no old freeze or result is altered. The first offline attempt's UTF8-path decoding/array-count errors are preserved in v1 and corrected in v2; they consumed no native calls and do not replace any failed native trial.

**145 opens / 145 closes / 0 new Parts / no unresolved owned titles.** This decision adds zero native calls and preserves all **626** ledger events. No native `recovery.json` remains outside intentional pure/regression fixtures. This offline audit makes no new claim about user-open SOLIDWORKS documents and neither attaches to nor starts/closes the application.

All **91** nongenerated production `.cs` files match the public-tested v15 freeze. Public Scalar and EditSet dispatch still require live healthy subtype/accessor/driver/reference/dependency qualification; no gate, tolerance or unsupported subtype is changed. Reused pure result is **86/86**, six recorded regression suites all exit 0. Documentation-only changes do not justify repeating M14A/B native matrices.

## M15 Entry Decision

**Proceed to M15 as a separate implementation task.** No remaining condition blocks M14 under the two approved revisions. The unresolved native ambiguity coverage remains disclosed, and production must still reject any such unsafe binding encountered.

M15 retains its own initial/noninitial-plane native sketches, spatial-reference ambiguity refusal, solver status, transformed world geometry, supported edit continuity, full loop/arc/slot/local-frame corpus and **3 new Parts / 4 native open cycles** schedule unless separately authorized otherwise. M14's unlimited historical authorization does not silently amend M15's PRD budget. No M15 code or tests were executed here.

This decision is **not** `SWCAD-HARNESS v0.3 CORE COMPLETE`: M15-M20 construction/composition/provider/final-release obligations remain. No universal external-Part, Hole Wizard, two-direction-pattern, arbitrary-frame or unknown-material-history edit support is claimed.
