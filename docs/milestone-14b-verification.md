# M14B Atomic EditSet and Durable Recovery Qualification

**M14B: COMPLETE. Overall M14: PARTIAL pending M14C.** Verified 2026-10-10 UTC, relative to `458b6e18ae6f77e2aa1147c724144bff672c2be0`. The actual checked-out branch is `v0.3-main-update`; the local `blocked-code-version` ref points to older `a2edad2`. Neither branch was switched or moved to reconcile the prompt's stale branch label. This qualifies bounded atomic transactions over the four M14A native scalar accessor combinations. All native inputs edited here are isolated copies of frozen Factory **development** `dev_core`, not engineer-authored or held-out acceptance models. No new Part, M14A native rerun, M14C acceptance, M15+ work or Factory extension occurred.

## Production Changes

- `ExternalPartSession.cs`: public `Edit(EditSetRequest)` now checks independent atomic qualification and every observed target's exact qualified subtype/parameter/accessor, then dispatches through the existing `ExternalEditTransactionBackend`, shared `RequestMutationTransaction` and `ManagedRevisionStore`. Scalar dispatch is unchanged.
- `V03NativeQualificationCandidates.cs`: adds the separate atomic qualification flag and bounded batch gate. Promotion followed v14 candidate, actual native rebuild failure, publication and independent recovery proofs; v15 subsequently verifies actual public execution. Scalar rows alone cannot grant batch qualification.
- `V03ContractCapabilities.cs`: corrects the stale contract-only refusal message. This context-free entry still cannot authorize native mutation without a verified external session.

The real missing capability was the unconditional public EditSet refusal. No new defect in the existing transaction/pointer protocol was found in this run; its rollback, quarantine and post-commit semantics were verified rather than rewritten. No second EditSet engine, native subtype expansion, geometry-tolerance change or weakened dependency/driver check was introduced.

M14-only test changes add bounded batch schedules, an immediate-evidence scenario helper, public dispatch and native fault scenarios. Existing Oracle, ledger, freeze and lifecycle mechanisms are reused. Pure tests add exact atomic-gate boundaries, a dependency cycle and a final-legal/no-safe-intermediate case. The freeze script copies sources/binaries only, not accumulated evidence directories.

## Successful Transactions

Both candidate and public runs start from a fresh dev_core Revision 0; M14A Core remains Revision 11. Binding uses native persistent references: B `aEIAAAEAAAD//v8AAAAAAGoAAAA=`, C `aEIAAAEAAAD//v8AAAAAAHMAAAA=`, pattern `aEIAAAEAAAD//v8AAAAAAGEAAAA=`. Feature labels in the test specification do not participate in production dispatch.

| Transaction | Native edits | Aggregate result | Complete saved Oracle |
|---|---|---|---|
| Independent holes | B diameter 10 -> 12 mm; C 6 -> 8 mm | Two successful native SetSystemValue3 calls; one checkpoint; R0 -> R1 | PASS; volume 92858.4073464102 / expected 92858.40734641021 mm3 |
| Cross-parameter type | B diameter 12 -> 11 mm; PatternSpacing 25 -> 24 mm | Planner orders spacing before diameter; two native steps; one checkpoint; R1 -> R2 | PASS; volume 93039.0489239916 / expected 93039.04892399162 mm3 |

Each request prepares all targets and the complete final geometry before checkpoint/Setter; result records aggregate ChangeSet/DirtySet and full-model validation. These are two batch requests, not repeated Scalar commits. Native saved bytes, parsed Companion, derived CADState parameters, revision/configuration and hashed Manifest agree. External revisions have no fabricated construction program.

Evidence: [candidate](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v14/results/candidate/result.json), [candidate independent cold](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v14/results/candidate-cold/result.json), [real public EditSet](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v15/results/public/result.json), [public independent cold](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v15/results/public-cold/result.json).

Public cold controller **8508**, distinct from mutation controller **28956**, confirms R2, B diameter 11 at (-30,-20), C diameter 8 at (35,-20), unchanged depth 10 and seed diameter 8, count 3, spacing 24. Pattern centers are (-40,20), (-16,20), (8,20); bounds [-60,-40,0,60,40,10] mm. Full official-API Oracle checks one solid, all planar/cylindrical surfaces, cylinder centers/radii/axes, both through boundaries, native definitions, feature health, references and volume. Factory Reader self-checks establish input identity only, never edit-result correctness. Saved native SHA256 is `64ce37f7faa2cecc7ab9019a352c5d3e7eab41ff13c22d8a595f3fddb0f5b76a`; Companion SHA256 is `0166202c231484f5519b8324826fe9ede541dc8170af3bc9dcd7a7d735fcc4ff`.

## Fault and Recovery Matrix

| Boundary | Real execution / injection | Outcome |
|---|---|---|
| Preparation | Public callback before resolving inputs | Refused; zero native steps/checkpoints; unchanged R0/files |
| First Setter | Actual B becomes 14; C Setter not executed; throw between calls | Candidate restores R0; public restores R2; one checkpoint, one partial step, exact native/pointer hashes and full Oracle |
| Last Setter | Both actual Setters complete; throw before final edit rebuild | Public rolls back exactly to R0; two partial steps, one checkpoint; full Oracle |
| Native rebuild | In unsaved owned copy, official driving-diameter Setter status 0/readback 1 meter makes cut remove host | **Actual ForceRebuild3(false) returns false**, B native error 51; FEATURE_REBUILD_FAILED; exact R0 rollback and full Oracle, not an adapter false |
| Postcondition | Both Setters and native rebuilds succeed; inject at validation boundary | Public rolls back exactly to R0; two partial steps, one checkpoint; full Oracle |
| BeforeNativeSave / AfterNativeSave | Real batch changes, controlled publication exceptions | No committed new Revision; exact R0 native/pointer restoration and full Oracle |
| AfterStateFlush / AfterPackageVerification / BeforePointerPublish | Real saved batch and protocol-boundary exceptions | Prior R0 remains authoritative; exact restoration; full Oracle; no unresolved marker |
| AfterPointerPublish | Exception after real commit point | **Success/StateCommitted R1**, no rollback; marker retained and session quarantined; new controller 20764 verifies R1 and completes recovery |
| Interrupted reopen | Exception before OpenDoc6 during saved verification and rollback reopen | Failed/uncommitted; R0 authority, quarantine and marker preserved; new controller 13944 restores/verifies R0 and completes recovery |
| Rollback blocked | Public first Setter succeeds; injected rollback exception | Explicit failed rollback and quarantine; next public edit refused; new controller 26312 restores/verifies R0 and completes recovery |

Publication failures are controlled exceptions in the real durable protocol, not claims of an actual disk-full or Save3 HRESULT fault. Reopen interruption is a controlled controller-boundary exception, **not an OS process kill**. Recovery nevertheless runs in separate new controller processes against the retained on-disk scene. The user-started SOLIDWORKS process is never terminated. Native rebuild failure is an actual native engine failure and is separately evidenced.

Evidence: [first-edit and all publication boundaries](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v14/results/faults/result.json), [actual native rebuild](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v14/results/native-rebuild/result.json), [postpointer recovery](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v14/results/postpointer-recovery/result.json), [interrupted scene](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v14/results/interrupted/result.json), [interrupted recovery](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v14/results/interrupted-recovery/result.json), [public preparation/last/postcondition/rollback isolation](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v15/results/boundaries/result.json), [public rollback recovery](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v15/results/rollback-recovery/result.json). Scene copies `postpointer-recovery-marker.json`, `interrupted-recovery-marker.json` and `rollback-recovery-marker.json` remain in the respective result directories after active markers are resolved.

## Public Gate and Negatives

Actual public `ExternalPartSession.Edit(EditSetRequest)` passes both successful transactions and First-edit rollback. Atomic qualification is independent of the four scalar rows, and every target still requires healthy known native history and an exact qualified accessor. Observed-only, missing, unsupported, wrong-accessor and duplicate combinations are not opened by the flag. Context-free contract acceptance remains non-executable.

v15 public preflight refuses duplicate target/parameter, nonexistent second target, invalid final geometry, mixed qualified/unqualified parameter, ExpectedOldValue mismatch, configuration, Revision and source-fingerprint mismatch before native Setter/checkpoint. A real seed-to-pattern dependency also refuses **NO_SAFE_EDIT_ORDER**: seed diameter 8 -> 30 and spacing 25 -> 40 has legal final geometry, but required seed-first order overlaps old instances; spacing-first violates the dependency. No safe order is invented. Pure tests separately verify cyclic declared dependencies and strict units/count/bounds. These checks are not presented as M14C true-deletion, same-size ambiguity or native configuration-switch proofs.

## Ledger and Preservation

| Frozen stage / slots | Actual opens / closes |
|---|---|
| M14A and prior history retained | 39 / 39 |
| v14 candidate / candidate-cold / native-rebuild | 3+1+2 / 6 |
| v14 faults / postpointer-recovery / interrupted / interrupted-recovery | 10+1+1+1 / 13 |
| v15 public / public-cold / boundaries / rollback-recovery | 4+1+3+1 / 9 |
| **Cumulative** | **67 / 67; 0 new Parts; 0 unresolved ownership** |

The initial complete schedule allowed 19 candidate opens; the stable public schedule allowed 9 including the remaining PRD consequential boundaries. All 28 new opens were reserved and counted, each slot ran once, and all 11 native slots passed. The [continuous ledger](D:/CAD-Harness0.2/artifacts/milestone14/native-budget.json) retains the exact prior event prefix. Human unlimited-open authorization is frozen in both namespaces; per-slot bounds and native resource guards remain active. All controller processes exited, before/after document sets are empty, original active state was restored, and no active `recovery.json` remains in any native acceptance package. Only user-started SOLIDWORKS PID **1800** was attached.

All four Registry Part hashes and both A1 hashes still match the frozen identities; originals were not edited. Final v15 audit verifies **209/209** frozen file identities, including Registry, Manifest, Reader proofs, inputs and source/binaries. M14A Core remains R11 with native SHA256 `b7e1b5f85cdfdf15ec61b8f048fcf70f9c5d10a6b58bacd20ff4b6f4fc8ab353` and unchanged authoritative Manifest. Existing failures/reports/freezes were neither rewritten nor converted into passing evidence. New numbered `steps/*.json` are flushed immediately with source/binary/schedule identities, values, Setter codes, rebuild and Oracle measurements, transaction outcome, timestamps and current lifecycle counters.

Freeze SHA256: v14 `63f20d30fd9a4034faff1358936ca2d290a29a9c20478a5f61c64d22c939a799`; v15 `0ca7fe9f19ea22b6e112f854ed7c079e95d76aa0929ce2dd4b1f294a719adf90`. Final production sources exactly match the public tested v15 freeze.

## Regression and Remaining Scope

[Final pure tests](D:/CAD-Harness0.2/artifacts/milestone14/pure/20261010T012101328/result.json): **86/86**, including retained M14A scalar boundaries. [Six isolated regressions](D:/CAD-Harness0.2/artifacts/milestone14/regression/20261010T012223582/result.json): M11 76/76, M12 48/48, M13 27/27, state 13/13, transaction and construction suites all pass. Build has zero warnings/errors; `git diff --check` passes. M14A native proofs are reused, not replayed.

**M14C remains pending:** current/old engineer A1 compatibility and historical failures; independent engineer/held-out supported histories; real design-table/configuration overrides; actual deleted/changed references and binding ambiguity between distinct targets; native rename/reorder and complete external-model final acceptance. No developer fixture substitutes for those inputs. M14B COMPLETE does not mark the original M14 PRD COMPLETE or remove any of its requirements.
