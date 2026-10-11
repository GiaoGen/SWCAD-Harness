# M14 External Candidate Screening

2026-10-10, production baseline `aea97a3470321f25210ee095ecdb7549f1e765d0`.
**Bounded screening finished. No candidate qualifies for positive M14 editing; overall M14 remains PARTIAL.** No production/test/PRD source edits, no new models, no formal editing or geometric-equivalence acceptance.

## Results

| Candidate | Actual read-only native history | Actual production qualification result |
|---|---|---|
| CMU `example-1-base` | One `Default` configuration; 6 mm blind boss, `Fillet1`, `ICE` servo-dowel cut and `Cut` pivot hole. Both cut definitions have end condition 2, not the advertised single-circle through-all subtype. No linear pattern. | `Inspectable / UNSUPPORTED_PARAMETER_DRIVER`: native external file reference count is **2**. One configuration, no design table, zero equations, no linked equation file. |
| CMU `example-2-base` | Same observed feature-type sequence and 6 mm depth, one `Default` configuration. No linear pattern. Matching names/types do not establish equivalent geometry or different authoring histories. | Same actual external-reference refusal, count **2**. |
| NCKU 2014 section 5.1 `Plate` | One `Default` configuration; `Sketch1`, 10 mm blind `Boss-Extrude1`, then `Fillet1`. No separately authored single-circle cut or linear pattern. | `Inspectable / UNSUPPORTED_PARAMETER_DRIVER`: **External sketch relation is not a verified local origin coincidence: Sketch1**. |

All three read-only opens had zero warnings, healthy observed material features with native references, and unchanged false dirty flags. Existing production inspection records native reference roundtrips and `GetParents/GetChildren` relationships. All three have an actual `Boss-Extrude1 -> Fillet1` edge, so these fillets are affected downstream material features, **not** unrelated unsupported branches.

The additional unsupported history is an observed compatibility barrier; production stopped earlier at driver validation. This run does not claim the history gate itself was the actual refusal point. A 6/10 mm depth reader candidate is not qualified depth editing. Native Circle/Dimension setters and cold edited readback were not executed.

## Evidence

Root: `D:/CAD-Harness0.2/artifacts/milestone14/screening-v1/`.

- [catalog.json](D:/CAD-Harness0.2/artifacts/milestone14/screening-v1/catalog.json): exact source SHA256, provenance, authorization continuation, frozen controller binaries and production/source identities.
- [screening-summary.json](D:/CAD-Harness0.2/artifacts/milestone14/screening-v1/screening-summary.json): parameter/reference/type inventory, named native dependency edges, source/copy hash checks and exact first refusal for all three candidates.
- Each `results/<id>/steps/00001.json` retains source and native version history; `00002.json` retains the actual read-only inventory, configurations, body observations and lifecycle results. IDs are `cmu-base-1`, `cmu-base-2`, `ncku-plate-5-1`.
- Each `results/<id>-qualification/steps/00002.json` retains actual `ExternalPartSession.CreateCopy` status/code/message. These qualification checks used separately owned working copies, not original or read-only inspection files. No `Edit` call or native save was issued; production refused before managed initialization. `current.json` and `recovery.json` are absent, and each working copy remains byte-identical to its source.
- `result.json: passed=true` means **successful safe inspection or expected refusal**, never positive editing acceptance. Both controllers for each source are recorded; no same-size ambiguity, whole-body edit Oracle or independent edited cold reopen is claimed.

## Protection and Scope

Ledger: **131/131 -> 137/137 opens/closes**, six opens in total (three read-only inventories plus three admission/refusal checks), zero new Parts, zero remaining owned titles. Historical event prefix unchanged. Three downloaded source hashes match their acquisition records, and all inspection/qualification copies remain unchanged. Existing controllers restore the previously active document and compare before/after user document titles; no user application was terminated.

[Final audit](D:/CAD-Harness0.2/artifacts/milestone14/final-source-screening-v1/audit.json) verified 2,700 historical frozen identities and no unresolved native recovery marker. The new screening catalog additionally reverified 135 frozen files. Current code built with zero errors/warnings; no need to rerun M14A/B native matrices for this evidence-only task.

These independently published sources can supplement negative/material-history research. None supplies an editable four-scalar/batch positive, verified equivalent-history counterpart or truly unrelated unsupported feature. They have now been screened and are **not relabeled fresh Held-out**. Exact individual authorship/licensing uncertainties in the acquisition report remain unchanged.

**Stop point:** do not modify these models, relax production driver/history gates, or automatically resume random downloads. The independent supported-positive and fresh Held-out material gaps remain explicit; these refusals do not undo the user's earlier successful manual-model compatibility test.
