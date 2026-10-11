# M14C4 Manual Part Native Editing Trial

**MANUAL COMPATIBILITY TRIAL: PASS. Overall M14: PARTIAL. Formal Held-out C4: not completed by this trial.** Executed 2026-10-10 on `v0.3-main-update`, HEAD `aea97a3470321f25210ee095ecdb7549f1e765d0`. No production code, qualification gate, tolerance, PRD or Factory changes.

## Frozen Input and Execution

Source: `D:\CAD-Harness0.2\M14测试用夹具.SLDPRT`, 76,063 bytes, configuration `默认`, SHA256 `60fbd4101e4c1f3c5ca129c7e5d44b1eadf28c50b6483e0259d06b4b716f9a8b`. Original and byte-identical frozen snapshots were protected; normal edits targeted a new owned package.

Initial geometry: 144 x 96 x 14 mm rectangular host; A diameter 7 at (-45,24), B diameter 11 at (-34,-26), C diameter 5 at (38,-26); A feature pattern has four total instances in +X, spacing 26 mm. B/C have separate single-circle sketches, writable driving diameters and no mutual dependency. These are this manual model's dimensions, not Factory dev_core dimensions.

| Target | Frozen native persistent reference |
|---|---|
| Host | `aEIAAAEAAAD//v8AAAAAABkAAAA=` |
| A seed | `aEIAAAEAAAD//v8AAAAAADQAAAA=` |
| Pattern | `aEIAAAEAAAD//v8AAAAAADsAAAA=` |
| B | `aEIAAAEAAAD//v8AAAAAAEUAAAA=` |
| C | `aEIAAAEAAAD//v8AAAAAAFQAAAA=` |

[Schedule](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v32/schedule.json), [198-identity freeze](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v32/freeze.json) and [baseline/provenance](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v32/baseline.json) precede execution. Current production/test sources match the reused acceptance-v29 frozen controller sources byte-for-byte. Initial read-only compatibility evidence is retained separately, not substituted for editing acceptance.

## Actual Public Transactions

| Operation | Public request / result | Native evidence | Saved result |
|---|---|---|---|
| Scalar | `ExternalPartSession.Edit(ScalarEditRequest)`: B 11 -> 14 mm; PASS, R0 -> R1 | One actual `IDimension.SetSystemValue3`, status 0; immediate and rebuilt readback 14; successful rebuild | One checkpoint, one new Revision, full Oracle and durable consistency PASS |
| Atomic pair | One `ExternalPartSession.Edit(EditSetRequest)`: B 14 -> 13 and C 5 -> 6 mm; PASS, R1 -> R2 | Two actual `SetSystemValue3` calls, both status 0; immediate/rebuilt values 13/6; successful rebuild | One aggregate checkpoint, one new Revision, two native steps; full Oracle and durable consistency PASS |

No consecutive Scalar calls impersonated the batch. Both transactions use existing qualified Handlers, `ExternalEditTransactionBackend`, `RequestMutationTransaction` and `ManagedRevisionStore`; the driver/reference/dependency/geometry guards remain active. Native events, publish boundaries, request/old/new values and Oracle comparisons are flushed immediately to numbered `steps/*.json`.

Evidence: [Scalar](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v32/results/manual-scalar/result.json), [Atomic EditSet](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v32/results/manual-batch/result.json).

## Independent Oracle and Cold Reopen

The existing independent `NativeEditOracle` uses the frozen manual author specification, corroborated by prior independent official-API measurements, with this file's exact references. It does not use Factory Manifest/Reader or production geometry/accessor helpers. Actual native configuration, health, subtype, owners, seed/dependency references, all six cylinder centers/radii/axes, both through boundaries, entire face types, one solid, bounds and volume pass. Unedited A/pattern/host and hole positions remain unchanged. Tolerances are unchanged.

| Revision | Measured / analytic volume (mm3) | Edit controller -> cold controller | Result |
|---|---|---|---|
| R1 | 188950.84552208567 / 188950.8455220857 | 31340 -> 12860 | PASS; B=14, C=5 |
| R2 | 189126.774710687 / 189126.774710687 | 29548 -> 26208 | PASS; B=13, C=6 |

Bounds remain `[-72,-48,0,72,48,14]` mm. Each edit saves through the production durable protocol, closes its owned document, and finishes before a distinct new controller calls `ExternalPartSession.Open`. Cold checks re-resolve references, parse the authoritative observed Companion, compare derived CADState/Revision, Manifest hashes and native bytes, and execute the full independent Oracle. This is controller-independent cold reopen, not a SOLIDWORKS application restart.

Evidence: [R1 cold](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v32/results/manual-scalar-cold/result.json), [R2 cold](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v32/results/manual-batch-cold/result.json), [authoritative pointer](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v32/packages/manual/current.json). Final native SHA256: `15a0f70f0a1e4fa0a13243f6f79b1312d91f7c57b7cceedf12657e487e319833`; observed Companion SHA256: `3c8d8f3fda5aa2181f47cdf8022b11f4f46ede8e5adf2462fad1db32ea2e543a`; Manifest SHA256: `7661607b0ddc5a582006810915d2ae39c62765e39dcb96eae33e560e371dbe4d`.

## Retained Failures and Cleanup

- [v30 pre-native failure](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v30/preflight-refusal.json): preparation used PowerShell's reserved `$input`, emitting empty slot input IDs; schema schedule validation refused before connection/open/Setter. Zero native cost. Corrected metadata in a new namespace, never overwrote the failed freeze.
- [v31 failure](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v31/results/manual-scalar/result.json): CreateCopy successfully qualified and initialized R0, but test lookup used an unnormalized mixed-slash package path and reported `Owned document missing` before any Setter. One open/close, zero edit checkpoints, no edit committed. [Diagnosis](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v31/diagnosis.json) retained; only new schedule paths were normalized. Neither is a native parameter-edit failure or a silently relabeled PASS.
- Successful v32 slots use 2+1+2+1 opens/closes. Including v31, this task adds **7 opens / 7 closes**, taking 124/124 to **131/131**, zero new Parts, zero owned documents. All 531 pre-task ledger events remain an unchanged prefix.
- Every controller ends with the same empty document set and restored active state. Only user-started SOLIDWORKS PID 9080 was attached, never started or terminated. GDI 1848-2269, below existing 7000 guard. No unresolved native recovery marker remains.
- [Final offline audit](D:/CAD-Harness0.2/artifacts/milestone14/final-manual-c4-v1/audit.json): 2,700 frozen identities verified, protected original/Registry/source evidence intact, no ownership/recovery leak. Original and frozen snapshot hashes still match the pre-execution SHA256 above.
- No tracked production/test source changed. M14A/B's unchanged qualification and previous 86/86 pure results are reused; their native matrices were not needlessly repeated.

## Acceptance Boundary

This proves real public Scalar and Atomic EditSet execution with saved independent recovery on the user-authored model. It does not newly qualify all four Scalars on this source, test the full fault matrix, prove untouched independent provenance, or resolve C3's outstanding ambiguity/unrelated-unsupported-history evidence.

The model was authored after the disclosed GUI specification and repeatedly compatibility-checked. It remains **manual external compatibility/development material**, not an untuned formal Held-out or a pre-intake provenance fixture. Original PRD independent/Held-out/different-history requirements and outstanding negative evidence remain pending. Consequently this bounded requested trial passes, but neither formal M14C4 nor overall M14 is declared COMPLETE.
