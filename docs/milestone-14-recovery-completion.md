# M14 Recovery and Completion Report

Reference: `2c651af`, `blocked-code-version`. Assessment date: 2026-10-09 UTC.

**Milestone status: PARTIAL. New native execution: BLOCKED pending a complete budget decision and missing independent inputs.** This is not a production qualification certificate. No M15+ or Fixture Factory extension was implemented.

## Phase 0: Completed

`artifacts/milestone14/recovery-v1/verified-before.json`, `closure-proof.json`, and `result.json` record the recovery action. Controller 25204 was absent; existing SOLIDWORKS PID 23760 was attached, not launched. Exact owned path/title and authoritative disk revision were checked under exclusive ledger/package leases. Only the owned `acceptance-v3/packages/core/working/CADHarnessManagedPart.SLDPRT` was closed, without saving or rebuilding. The engineer document `零件8` (unsaved path, clean) was not adopted, edited or closed.

The immutable closure proof preceded ledger reconciliation. Its SHA256 is `db5220dd8669a133c760419dea749afd710abe68118d5c590a42a1e596782bb4`. The ledger retains the original ownership events and adds `recovery-closed-owned` linked to this proof. No history was erased.

- Opens: **14/27**. Closes: **14**. New Parts: **0**. Live unresolved ownership entries: **0**.
- Authoritative core revision: **4**. Recovery marker: absent. Working native SHA256: `479f85c4b9a1b5ec5692da60ca66cc68572ee45354c349451572d7c3bb8f039e`.
- New native acceptance opens in this recovery: **0**. Recovery document closure is not edit acceptance.
- Grants 12 -> 20 -> 23 -> 27 are present in acceptance-v1/v3/v5 respectively, validated against their frozen identities and the retained human authorization replies. No new ceiling is authorized by this report.

## Changes Since the Reference

Production files:

| File | Actual change |
|---|---|
| `src/CadHarness.SolidWorks/ObservedParameterMutationHandlers.cs` | Observed setters return their actual enum/Boolean status for recording. The session still throws on unsuccessful status; no success is inferred from the new value. |
| `src/CadHarness.SolidWorks/ExternalPartSession.cs` | Optional native evidence sink records old values, persistent references, setter status, immediate/rebuild readbacks, durations and exception HRESULTs. Evidence-write failure propagates through the existing rollback boundary. No geometry tolerance or qualification rule changed. |
| `src/CadHarness.State/V03NativeQualificationCandidates.cs` | Corrects the stale hole candidate description from arc SetRadius to the already implemented unique driving IDimension.SetSystemValue3 accessor. All four `Qualified` flags remain false. |

M14-only test changes: exact ownership recovery audit; disk-only assessment; append-only flushed step files before final-report creation; Oracle actual/expected/difference/tolerance records before numerical assertions; truthful negative-test names; live remaining-budget validation before native connection; grant-lineage validation; attach-only legacy intake. No general test framework was introduced.

Known implementation fixes for actual-table detection (`HasDesignTable`), nonphysical UI inventory, circle driver/cache timing, and saved inventory/parameter association were **already in 2c651af**, not newly fixed in this recovery. They retain their historical failures and still need the missing native evidence. The actual-table positive-driver case has not been native tested. Unknown physical features and unknown dependencies remain read-only; the whole-plate geometry contract was not weakened to admit A1.

## Evidence and History

| Attempt | Opens | Result and evidence boundary |
|---|---:|---|
| source-v2, source-v3 | 1 each | Old engineer A1 refused `UNSUPPORTED_PARAMETER_DRIVER`; no mutation/revision. Design-table service non-nullness was incorrectly treated as actual-table presence. Origin/profile recognition was a separate suspected issue, not the reached refusal. |
| acceptance-v1/core | 1 | Development intake failed at actual-table detection; negative service row/column counts were not proof of a table. |
| acceptance-v2/core | 1 | Nonphysical AnnotationViewFeat/UI inventory was treated as unsupported material history. |
| acceptance-v3/core | 3 | Depth 10 -> 12 succeeded, one checkpoint/revision 1, saved reopen and complete independent Oracle. Diameter failed during execute; rollback to revision 1 reported successful. |
| acceptance-v4/core | 2 | Diameter reached commit, then complete companion association failed; rollback to revision 1 reported successful. |
| acceptance-v5/core | 5 | Interrupted controller; no final result or flushed per-step Oracle. Disk revision 4 exists, but is not a retroactively accepted four-row/batch run. |
| recovery-v1 | 0 | Controlled owned-copy closure and disk/source reconciliation only. |

Historical depth Oracle: volume actual `112108.67282886762`, expected `112108.67282886764` mm3; measured bounds [-60,-40,0,60,40,12] mm; all five cylindrical boundaries and radii checked. Preserve this valid candidate positive evidence; do not replay depth solely to obtain a better report.

The v3/v4 rollback results are **not** the required first-setter two-target/fault matrix. V5's missing reports cannot be regenerated from the current authority. A mocked adapter returning false after an actual successful rebuild is not proof of a native engine rebuild failure.

The new disk-only audit verifies all **2,888** file identities bound by the latest historical freeze, all six frozen input sources, and the four development fixtures' Manifest/Reader/ready/native identities and feature histories. Its output is `artifacts/milestone14/recovery-assessment-v1/offline-audit.json`. Factory proofs remain preparation-only, never edit-result oracles.

Final pure tests: **70/70**, zero build warnings/errors (`artifacts/milestone14/pure/20261009T143402722/result.json`). Final isolated regressions: `artifacts/milestone14/regression/20261009T143311284/result.json`; M11 76/76, M12 48/48, M13 27/27, state 13/13, transaction and construction suites all passed. These do not qualify native editing. Earlier pure/regression attempts remain retained; the final narrow budget-registration test is in the 70/70 run.

## Four-Row Qualification Matrix

| Row | Persistent target / native positive | Saved Oracle / cold read | Corresponding negative / public / external / held-out | Gate |
|---|---|---|---|---|
| ExtrusionDepth | Core host ref `aEIAAAEAAAD//v8AAAAAAFEAAAA=`; 10 -> 12, v3 | Complete v3 Oracle; same-controller saved document close/reopen passed | Required native equation/unsupported driver variant and public engineer/held-out result incomplete; independent fresh-controller proof incomplete | CLOSED |
| HoleDiameter | Core seed ref `aEIAAAEAAAD//v8AAAAAAFoAAAA=`; v3/v4 failed; v5 evidence incomplete | No accepted end-to-end qualification | Actual blind-hole/ambiguity variant, public/external/held-out incomplete | CLOSED |
| PatternCount | Core pattern ref `aEIAAAEAAAD//v8AAAAAAGEAAAA=`; v5 disk value 4 only | No retained accepted full Oracle | Actual unsupported pattern subtype/driver variant, public/external/held-out incomplete | CLOSED |
| PatternSpacing | Same persistent pattern; v5 disk value 20 mm only | No retained accepted full Oracle | Same gaps as count | CLOSED |

Format rejection is not native persistent-reference invalidation. Duplicate target/parameter rejection is not binding ambiguity between two distinct same-size native targets. A changed request configuration is not actual native configuration switching. Old evidence names are retained; new runner names state the exact check performed.

## Remaining Plan and One Budget Proposal

**Current remaining allowance: 13 opens.** The previous v5 frozen schedule alone allocates 16 opens and assumes revision 1; it is not executable against revision 4. The following is a bounded proposal, **not** a newly authorized or executable freeze. Freeze exact current source/binaries, expected values, references, input hashes and order only after required inputs and authorization are available.

| Ordered work | Maximum opens | Coverage / sharing |
|---|---:|---|
| Core revision-4 qualification and recovery sequence | 8 | Initial open (1); one batch for remaining diameter/count/spacing rows (1 commit reopen); independent two-hole batch (1); true persistent deletion/invalidation and exact snapshot restoration (1); first-setter rollback (1); file-publish rollback (1); state-publish rollback (1); interrupted reopen before native OpenDoc6, then independent fresh-controller authoritative recovery (1). Measure the two existing equal-size native targets for genuine binder ambiguity before changing them. |
| Isolated actual native-engine rebuild failure | 2 | Separate owned copy intake and batch-start restoration reopen. Require actual native engine failure evidence; adapter false-result injection cannot satisfy this slot. No frozen source modification. |
| Equation and unknown/blind history refusals | 2 | One intake each. Record actual native driver/cut facts and refusal before checkpoint/mutation. Equation is a depth-driver negative, not automatic proof of a pattern-specific negative. |
| Native configuration and unsupported pattern history | 1 | Read native subtype/reference/configuration facts and rejection. Multiple-configuration driver refusal and two-direction pattern classification must be reported separately, not falsely attributed to one another. Source/spec needed. |
| Actual native design-table driver | 1 | Existing closed Part with a real table; official HasDesignTable fact and production driver refusal. No Excel/table attachment, source repair, or table generation. Source/spec needed. |
| Origin-constrained public two-target edit with rename/reorder | 2 | One intake, one saved verification reopen; prove references stable and one aggregate revision/checkpoint. |
| Current A1 public four-row batch | 2 | One intake, one saved verification reopen; independently frozen A1 geometry, original hashes unchanged. Failure remains failure. |
| Old A1 history | 2 | One intake plus public saved edit/reopen **only if** genuine supported qualification succeeds; otherwise preserve read-only refusal and leave unused allowance. Do not waive old-history coverage by using dev_core. |
| Independent held-out public external edit | 2 | One intake plus commit reopen. Independent custody/spec/history/hash required before formal freeze; no production tuning during its measured attempt. |
| **Necessary planned ceiling** | **22** | Includes internal verification/rollback opens; no Part creation. |
| **Bounded failure contingency** | **2** | New evidence slots only, never overwriting or relabeling failed slots; not permission for unlimited retries. |

Proposal: raise the cumulative cap **27 -> 38** (+11 ceiling, not +11 consumed opens), leaving at most **24** future opens from the current 14. The 22 planned slots exceed today's remaining allowance by 9; two additional contingency opens are explicit. Do not reset or retroactively split the 14 existing opens. No budget transfer from Factory.

Savings already included: reuse v3 accepted depth; combine remaining scalar rows in one transaction; combine rename/reorder with public batch; contract negatives and same-size native measurements share an open session; use the interrupted-recovery fresh controller also for independent cold authority/Oracle readback instead of an extra cold-only open. If independent custody requires a different cold measurement, use a declared contingency slot, not an uncounted open. Every raw OpenDoc6 call is still reserved before execution.

Required missing inputs: an engineer-authored held-out supported Part with independently supplied dimensions/history and custody/hash; existing actual-design-table source; and a suitable unsupported native pattern/configuration source/spec. The previously supplied `压制.SLDPRT` can be evaluated as a multi-config/two-direction negative, never relabeled as a single-config isolated subtype case. No new Factory Parts are requested.

After candidate native rows have complete evidence, a new source version may promote only those rows and test the public production entry. Candidate backend success, public success, saved reopen, external-model compatibility and full PRD completion are separate outcomes. Promotion was not performed in this recovery.

## Originals, External Coverage and Conclusion

Current A1 SHA256: `aae0907a25a0e4068e3d5362f85a7f9529525b4419d1d597b9db7f8449f64bac`. Old retained A1 SHA256: `7182c79520636053c4fed3bcde3c5b947e57f7550c921645db22f638d3af7eef`. Both unchanged in the recovery audit. All four Factory sources also match their frozen hashes. No historical result, failed proof or freeze was overwritten.

A1's compatibility remains unaccepted; its historical failures are not replaced by development fixture results. No held-out fixture has been supplied/frozen, and no held-out external edit has passed. No public external Edit/EditSet native acceptance has passed; all four gates remain closed. Required dual-target atomic success, all five fault/recovery classes, genuine binding/reference negatives, and independent cold proof remain incomplete.

**PARTIAL**, with Phase 0 recovered and the minimal evidence/budget fixes verified by pure tests. Native execution stops at the full-plan budget/input boundary, not at an unresolved ownership problem. COMPLETE is forbidden until the missing real evidence and production qualification/public-entry checks pass.
