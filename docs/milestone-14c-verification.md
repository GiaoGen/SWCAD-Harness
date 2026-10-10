# M14C External Model Qualification

**M14C: PARTIAL. Overall M14: PARTIAL.** Verified 2026-10-10 UTC against `58e5dec` on the existing `v0.3-main-update` branch. M14A/B qualification is retained; it is not substituted for independent external-model acceptance. No M15+, Factory expansion, engineer-source modification, tolerance relaxation or production-gate promotion occurred.

## Source and Compatibility Matrix

Frozen paths, SHA256, native references, configuration, version history and observations are in [v17 catalog](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v17/catalog.json), [extended catalog](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v24/catalog.json) and their per-source `results/*/steps/*.json`. Runtime revision is `32.0.1`; native `VersionHistory` strings are retained verbatim, not inferred authoring versions.

| Source | Provenance / independence | Native conclusion |
|---|---|---|
| Current A1 | User engineer model; previously diagnosed, created after intake; NOT held-out | INSPECTABLE_READ_ONLY; public-session intake refuses unsupported sketch drivers |
| Old A1 | Preserved source-v3 bytes; same engineering lineage; NOT held-out | Same refusal; old failures retained, not reclassified as successful edits |
| Connector / wrist | User-provided non-Harness models; precise author/workflow unverified; already used in M13 | Production rejects external sketch relations; wrist cuts additionally lack supported subtype evidence |
| Suppressed Part | Existing engineer source; two user-named configurations | Both configurations refuse driver qualification; actual alternate configuration has a two-direction pattern |
| Mecanumbot three plates | Josh Villbrandt, fixed 2017 Git commit, Apache-2.0; independent pre-intake candidates, no tuning | Read-only: driver refusal plus Hole Wizard / mirror / nonqualified cuts; Top Plate also has fillet history |
| Coaxial UAV two plates | Aubrey, fixed 2016 Git commit; independent pre-intake candidates, no tuning; no license file observed | Read-only; native extrusion end condition 6, not the qualified single-direction Blind subtype |
| Installed Base Plate / Beam Splice | Existing SOLIDWORKS sample paths; named author/workflow unverified | Multiple configurations, no actual Design Table; inspected only, not promoted as engineer acceptance |
| Installed HASCO cavity plate | Existing vendor sample; NOT a supported held-out positive | Actual Design Table, 1,423 configurations, two equations and two external references; mandatory production driver refusal |
| Four Factory files | Frozen independent preparation Registry / Reader; development only | M14A/B evidence reused; dev_core supports boundary trials, equation/unknown fixtures remain refused |

Third-party provenance: [Mecanumbot](D:/CAD-Harness0.2/artifacts/milestone14/thirdparty-candidates-v1/provenance.json), [Coaxial UAV](D:/CAD-Harness0.2/artifacts/milestone14/thirdparty-candidates-v2/provenance.json). All five downloaded Part blobs match their fixed Git blob IDs. First-repository full commit API retrieval was rate-limited; second-repository lazy blob checkout failed and fixed-commit raw downloads were used instead. Neither failure is hidden. No downloaded code or macro was executed. Upstream sources: [Mecanumbot-CAD](https://github.com/joshvillbrandt/Mecanumbot-CAD/tree/c50a655aef61ac1a6340b551ec4e837e54e871c4), [coaxial-uav](https://github.com/AubreyC/coaxial-uav/tree/75b8fbb7f0f55fe5f08d517074a916808586b818).

No candidate qualified for supported independent editing. This is a bounded search result, not a claim that no qualifying model exists anywhere. C1/C2/C3 independent positive acceptance remains **BLOCKED_MISSING_INDEPENDENT_HELDOUT**. No Factory or installed library source is relabeled as a successful engineer/held-out model.

## A1 Diagnosis

Both A1 files expose 32 inventory nodes: one extrusion, three `ICE` wrappers recognized as `SingleCircleThroughAllCut`, one single-direction pattern and four consumed profiles. Current A1 SHA256 is `aae0907a25a0e4068e3d5362f85a7f9529525b4419d1d597b9db7f8449f64bac`; old A1 is `7182c79520636053c4fed3bcde3c5b947e57f7550c921645db22f638d3af7eef`. Both retain `17000[2023/282]` native version history.

The [actual positive-intake attempts](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v18/results/engineer_current/result.json) and [old-version attempt](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v18/results/engineer_old/result.json) **FAIL** before any Setter/checkpoint/revision: `UNSUPPORTED_PARAMETER_DRIVER`, starting at host Sketch1. The [read-only relation probe](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v19/results/probe/result.json) resolves a local-origin coincidence in the rectangular host, but also finds external-to-sketch DISTANCE relations from all three hole profiles to actual host edges. Sketch3 has an additional coincidence. Entity and definition references, filter counts and owners are recorded. This is not merely a coincidence-versus-dimension choice, nor evidence of an external-file link. The currently qualified exception is exact local-origin coincidence at a single-circle center, not arbitrary rectangular-host or edge-driven sketch relations. Changing that safety boundary was not justified by this run.

Consequently C1 Scalar, C2 engineer EditSet, their cold slots, and A1 rename/reorder were not executed after failed intake. They are **UNVERIFIED**, not successful through dev_core. Earlier ICE/profile-ownership and design-table-service mistakes remain separate historical issues already fixed before this baseline. The preserved [A1 diagnosis](D:/CAD-Harness0.2/docs/milestone-14-a1-diagnosis.md) is not overwritten.

## Native Boundary Results

| Case | Actual evidence / outcome | Status |
|---|---|---|
| N1 same-size identity | Two different B/C references both become diameter 7; exact targets remain unique; then both become 8 after native rename/reorder | PASS exact-reference behavior on development data; genuinely ambiguous-binding refusal UNVERIFIED |
| N2 deleted reference | Native `DeleteSelection2`, old reference resolves null/status 1; public request refuses in **preflight**, zero Setter/checkpoint; exact disk/pointer and complete Oracle restored | PASS, v25/deleted |
| N3 rename / reorder | Native feature names changed; `ReorderFeature` returns true; same references bind correct targets; public EditSet, saved package and separate-controller cold pass | PASS development history only, v20/identity + identity-cold |
| N4 suppression | Actual `SetSuppression2`/`IsSuppressed`; public preflight `STATE_DRIFT_DETECTED`, zero Setter/checkpoint; exact recovery and full Oracle | PASS, v25/suppressed |
| N5 configuration | Existing suppressed model actually switches configuration; both original configurations reject intake. Separate qualified dev_core session actually activates a new configuration; stale request refuses `FEATURE_REBUILD_FAILED` in preflight; original one-configuration state restored | PASS, v22 diagnostic/qualifications + v28/configuration |
| N6 Design Table | Existing cavity native document reports `HasDesignTable=true`; native host depth dimension `IsDesignTableDimension=true`, value/definition 0.086 m. Production explicitly rejects table/configuration/equation/external driver facts before mutation | Static native driver and combined refusal proven; dynamic table change UNVERIFIED |
| N7 unknown dependency | Actual native GetParents/GetChildren confirms Host -> blind cut; B/C have no outgoing children. Production refuses the unqualified blind history as a whole, including edits unrelated to that branch | PASS conservative refusal; independent external-history coverage missing |
| N8 source drift | One byte appended to a disposable source, actual hash differs; public entry throws `SOURCE_FILE_DRIFT`, zero native events/checkpoint/revision; source restored exactly | PASS, v20/source-drift |
| Equation driver | Actual equation-bearing frozen development source refuses `UNSUPPORTED_PARAMETER_DRIVER` | PASS, v22 qualification |

Strong refusal evidence: [deleted](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v25/results/deleted/result.json), [suppressed](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v25/results/suppressed/result.json), [configuration](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v28/results/configuration/result.json), [source drift](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v20/results/source-drift/result.json), [unknown native graph](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v22/results/dev_unknown_descendant-diagnostic/result.json), [table dimensions](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v24/results/vendor-cavity-diagnostic/result.json), [production table refusal](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v27/results/vendor-cavity-qualification/result.json).

The table's [cross-configuration trial](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v27/results/vendor-cavity-diagnostic/result.json) **FAILS**: configuration switch succeeds, real `ForceRebuild3` returns false, and the retained feature handle/reference and 0.086 m cached readback cannot prove a new driven value. No PASS is inferred from those stale observations, no source is saved and no same-path blind retry follows. Excel COM is unregistered; [official design-table guidance](https://help.solidworks.com/2024/English/SolidWorks/sldworks/c_Design_Table_Configurations.htm) requires Excel for table editing. An actual table was therefore investigated rather than fabricated. Table-only, single-configuration controlled-dimension isolation is still unverified; the library source has several independently disqualifying drivers.

## Successful Public Transactions and Cold Oracle

[Identity trial](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v20/results/identity/result.json) uses the real public production EditSet twice, not direct test Setters. Each request has two successful `SetSystemValue3` calls, **one checkpoint and one new revision**, R0 -> R1 -> R2. Names and tree order are not production binding keys. B reference `aEIAAAEAAAD//v8AAAAAAGoAAAA=` and C `aEIAAAEAAAD//v8AAAAAAHMAAAA=` are unchanged.

[Independent cold](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v20/results/identity-cold/result.json), controller **15604** versus editing controller **25248**, confirms R2, renamed/reordered references, both diameters 8, centers (-30,-20)/(35,-20), unchanged host/seed/pattern, Native/State/Companion/Manifest consistency and no recovery marker. Official-API complete Oracle volume is **93486.72587712816 mm3**, equal to independent expected volume; envelope [-60,-40,0,60,40,10] mm; all five cylinders and both through boundaries pass. The geometry-equivalent pre/post reorder is genuinely verified, but remains development provenance, not independent history acceptance.

## Test Corrections and Preserved Failures

- v20 deleted/suppressed initially created recovery markers too early. Their raw PASS results only prove pending-recovery quarantine, **not** native-reference refusal. They are superseded for that requirement by v25 preflight proofs; neither record is edited or deleted.
- v23/v26 configuration trials FAIL because configuration creation already activates it and a redundant ShowConfiguration2 returns false. v26 immediate facts prove creation=true, active new configuration, count=2. The test now checks actual activity, not that redundant return; v28 passes. Production configuration behavior was not changed.
- Compile/preparation defects were confined to tests (namespace/accessibility, local names and ledger JSON property). Offline audit initially also mistook deliberately forged pure/regression recovery fixtures for active native recovery. Both audit failures are preserved as `audit-preparation-failure-v*.json`; those fixtures are retained and fingerprinted, not removed.
- A1 positive-intake failures and actual table-rebuild failure remain failures. No missing successor slot is converted to PASS and no aggregate rate hides skipped independent positive tests.

## Production Scope and Regression

**Production files changed relative to `58e5dec`: none.** The investigation found unsupported/uncertified native drivers, not a proven defect permitting a safe minimal production widening. Four scalar subtype/accessor gates and independently qualified Atomic EditSet remain unchanged. Public entry is genuinely exercised in the identity transactions and native-state refusals. No fixture ID, feature name, similar size, larger tolerance or direct COM test edit grants production eligibility.

Only `ExternalCatalog.cs`, `ExternalModelQualification.cs`, test `Program.cs`/`NativeAcceptance.cs` and the source-only freeze script change. These are bounded M14C scenarios reusing existing session, transaction, persistence, ledger, journal and Oracle, not a second mutation engine or general testing framework.

Final [pure tests](D:/CAD-Harness0.2/artifacts/milestone14/pure/20261010T022346282/result.json): **86/86**; build zero warnings/errors; `git diff --check` passes. [Six isolated regressions](D:/CAD-Harness0.2/artifacts/milestone14/regression/20261010T020606270/result.json): M11, M12, M13, State, Transactions and ConstructionTransactions all exit 0 on unchanged production. M14A/B original native qualifications and fault/recovery proofs are reused, not rerun solely to improve presentation. Shared M14A Core remains **R11**; native SHA256 `b7e1b5f85cdfdf15ec61b8f048fcf70f9c5d10a6b58bacd20ff4b6f4fc8ab353` and authoritative manifest SHA256 `7fb4edd53c234b2f9587ff675ca93ddee491304a345543dce23c3fcf30fdcf95` still match v15 baseline and current pointer.

## Ledger and Final Gate

| Stage | Additional opens/closes |
|---|---|
| v17 source catalog | 9 / 9 |
| v18 A1 failures + v19 diagnosis | 3 / 3 |
| v20 boundary trials including weak superseded refusals | 9 / 9 |
| v21 existing-source qualification + v22 screening/diagnostics/qualification | 16 / 16 |
| v23 failed configuration + v24 vendor diagnostics | 5 / 5 |
| v25 strong refusals + v26 failed configuration | 6 / 6 |
| v27 actual table failed dynamic test / production refusal + v28 configuration | 4 / 4 |
| **M14C added / cumulative** | **52 / 52; cumulative 119 / 119; 0 new Parts, 0 unresolved ownership** |

43 native controller slots ran; five raw FAIL results and two weak superseded refusal records are retained. User unlimited M14C authorization is distinct and frozen; each scenario retains its own finite ceiling, original protection, responsive-process/GDI guard and immediate journal. Only user-started SOLIDWORKS PID **29348** was attached, never started/terminated by this task; final lifecycle GDI 3043, no controller left running.

[Final offline preservation audit](D:/CAD-Harness0.2/artifacts/milestone14/final-m14c-v1/audit.json) verifies **2,315 frozen identities**, all four Factory source/Manifest/Reader/Ready identities, all selected engineer/download/vendor hashes, and the complete **309-event historical prefix** through the starting 76-open snapshot. Zero active native recovery markers remain. Its 141 deliberately retained pure/regression marker files are evidence fixtures, not unresolved native ownership. The continuous [ledger](D:/CAD-Harness0.2/artifacts/milestone14/native-budget.json) and all historical namespaces remain intact.

**Remaining M14 conditions:** supported pre-intake independent engineer Scalar + atomic EditSet + independent cold reopen; a supported held-out positive frozen with its own Oracle; independent geometrically equivalent/different-history acceptance; genuinely ambiguous native binding refusal; complete Design Table dynamic/isolated-driver qualification and independent external unknown-dependency coverage. These are not removed from PRD §§15-16. No P0/v0.3-wide completion or M15 readiness is asserted.
