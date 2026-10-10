# M14C1 Acceptance Boundary and Evidence Audit

**M14C1: COMPLETE (audit only). Overall M14: PARTIAL.** Audited 2026-10-10 at `18470cbcf3172f4024226a25e4f57c2361d74035`, actual branch `v0.3-main-update`. No production/PRD/model/ledger changes, SOLIDWORKS attachment, native opens or test reruns. This report interprets existing evidence; it does not upgrade any historical result.

## 1. Original Acceptance Boundary

Authority: [original PRD](D:/CAD-Harness0.2/SWCAD_Harness_v0.3_PRD.md:199), not the residual-work lists in earlier reports. A/B/C stage names are execution subdivisions, not new product requirements.

| Original requirement | Clause | Audit conclusion |
|---|---|---|
| Four qualified native subtype/parameter/accessor rows; exact owner/reference, understood drivers/dependencies; bounds, before/after values, geometry, saved cold read | Section 8; M14 required matrix | Required. Similar appearance does not qualify Hole Wizard or two-direction patterns. |
| Engineer-authored supported single edit and explicitly targeted atomic batch, at least two independent qualified targets | M14 goal; Sections 9, 15 | Required. Factory development success does not fulfill independent engineer acceptance. |
| Prepare all, final geometry and safe intermediate order; one checkpoint/revision; restore batch start or quarantine | Section 9; M14 | Required. Already implemented and natively evidenced. |
| Consequential preparation/edit/rebuild/postcondition/file/state/rollback/reopen fault boundaries | Section 9; Section 15; M14 verification | Required. Controlled faults in the actual protocol are valid injection; real rebuild failure is separately evidenced. |
| Independently authored native files; version/configuration/history/parameters/checksums; identify manual/independent pre-intake provenance | Section 15 | Required. Newly made independent-API development fixtures cannot retroactively satisfy the provenance cutoff. |
| Geometrically equivalent Parts with different histories; qualified history edits, unqualified counterpart remains inspectable/read-only | Section 15 | Required. Does NOT require qualifying every counterpart or two editable histories per row. |
| Unrelated unsupported feature and potentially affected unsupported descendant | Section 15 | Required fixtures. Editing beside the unrelated feature is conditional on proof, not an unconditional obligation. Affected unknown descendant must refuse. |
| Rename/reorder with valid native identity; suppress/configuration/source/reference drift; same-size ambiguity; invalid values | Sections 8, 15-16 | Required. Distinct equal-size targets with exact references are NOT ambiguous. |
| At least one held-out external history/parameter/layout combination unused for production tuning | Section 14 (lines 350-352); Section 21.7 | Required for closing external generalization here. Original wording does not demand a newly named author for every test. Current user also prohibits recycling screened third-party files as fresh Held-out. |
| Independent native Oracle, untouched targets, revision/restoration/source checks; distinct edit/read-only/ambiguity/batch outcomes | Section 16 | Required. Factory Reader is preparation evidence only. |
| One owned document; original-byte protection; safe cleanup/resources; continuous authorized accounting | Section 17 | Required. Existing no-limit grants do not authorize reset or deletion of past accounting. |

**Not additional M14 hard gates:** making A1 editable; isolated single-configuration Design Table plus dynamic table-driven modification; an OS-killed controller or actual disk-full fault rather than protocol injection; supporting unrelated unknown material history; arbitrary frames/multiple hosts; every subtype variant. These can strengthen testing but are not stated M14 requirements. Milestone **20** requires two distinct histories per advertised family; Section 16's C1-C4 real-provider 8/12 construction gate and broader v0.3 completion are not newly imposed M14C1 gates. They remain obligations of their original stages. No PRD clause is deleted.

## 2. Reusable Evidence

Paths below are relative to `D:/CAD-Harness0.2/artifacts/milestone14/`; the [M14A](D:/CAD-Harness0.2/docs/milestone-14a-verification.md), [M14B](D:/CAD-Harness0.2/docs/milestone-14b-verification.md) and [M14C](D:/CAD-Harness0.2/docs/milestone-14c-verification.md) reports provide step-level links. PASS applies only to the indicated provenance and behavior.

| Requirement | Existing result paths | Coverage and limit |
|---|---|---|
| Depth | `acceptance-v3/results/core/result.json` depth steps; v6/core-cold; v13/scalar-public + public-cold | Candidate 10 -> 12 mm reused; public 12 -> 11 mm, R11. The later v3 failure and interrupted v5 remain failures. |
| Diameter, count, spacing | v6/core + core-cold; v13/scalar-public + public-cold | Setter, rebuilt dimensions, complete body Oracle, saved and separate-controller cold PASS; development provenance only. |
| Local-origin circle | v11/origin + origin-cold; v10/probe | Exact local-origin coincidence and public edit PASS; not arbitrary external-to-sketch relations. |
| Batch and cross-type batch | v14/candidate + candidate-cold; v15/public + public-cold | B/C diameters, then diameter + spacing; each one checkpoint/revision, complete native/state/companion/manifest and cold PASS. |
| Failure/recovery matrix | v14/faults, native-rebuild, postpointer-recovery, interrupted, interrupted-recovery; v15/boundaries, rollback-recovery | Real first/last Setters, native ForceRebuild3=false/error 51, protocol file/state failures, post-pointer commit authority, reopen interruption, rollback quarantine/new-controller recovery PASS. Not disk-full or OS-kill claims. |
| Illegal batches/values | v13/scalar-public; v15/public; retained 86-test pure result | Duplicate/missing target, wrong accessor, invalid final geometry, no safe dependency order, old-value/config/revision/fingerprint mismatch refuse before Setter/checkpoint. |
| Rename/reorder; two equal-size exact targets | v20/identity + identity-cold | Distinct references stay exact through successful native rename/reorder/public batch and cold. Genuine ambiguity refusal NOT proven. |
| Actual deleted reference; suppression | v25/deleted; v25/suppressed | Native deletion resolves null/status 1; actual suppression; public preflight drift refusal, zero Setter/checkpoint, exact restoration/Oracle PASS. v20 weak quarantine-only results cannot substitute. |
| Actual configuration/source change | v28/configuration; v20/source-drift | Active native configuration change rejects stale request; actual disposable source-byte change yields SOURCE_FILE_DRIFT. v23/v26 test failures retained. |
| Equation and affected unknown descendant | v22/dev_equation_driver-qualification; v22/dev_unknown_descendant-diagnostic and qualification | Actual equation refusal and native Host -> blind-cut graph; unaffected B/C separately observed. Whole-package refusal is proven on development data, not independent source coverage. |
| Actual Design Table | v24/vendor-cavity-diagnostic; v27/vendor-cavity-qualification | HasDesignTable=true; actual depth dimension IsDesignTableDimension=true/value 0.086 m; production refuses combined drivers. Sufficient for conservative non-advertisement, not isolated per-driver behavior. |

Design Table dynamic trial `v27/vendor-cavity-diagnostic` really FAILED after configuration switch/rebuild; cached data cannot prove a changed driven value. Preserve it, but do not make dynamic editing of an already rejected table a new completion condition. Static native evidence and combined refusal do not certify table editing.

## 3. Model Provenance and Held-out Matrix

Exact paths, checksums, versions/configurations/history and source claims: [v24 catalog](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v24/catalog.json), [v17 catalog](D:/CAD-Harness0.2/artifacts/milestone14/acceptance-v17/catalog.json), [Factory Registry](D:/CAD-Harness0.2/artifacts/fixture-factory/audits/final-v1/fixture-registry.json). Derived inspection/working/revision copies inherit source lineage; they are not extra independent models. Pure-test files bearing a `.SLDPRT` extension are not native authoring evidence.

| Source family / selected native files | Classification | Future use / Held-out eligibility |
|---|---|---|
| M12 `artifacts/milestone12/legacy/CADHarnessM12Original.SLDPRT` and managed revisions; earlier Harness G2/construction files | Harness-generated | Managed regression only; neither independent external nor Held-out for M14. |
| `dev_core`, `dev_origin`, `dev_equation_driver`, `dev_unknown_descendant` | Fixture Factory-generated, post-intake development | Qualification/regression/safety only; no Held-out or engineer provenance. |
| `D:/document/A1.SLDPRT`; retained `source-v3/package/working/CADHarnessManagedPart.SLDPRT` | External engineer assertion; same lineage; already debugged | Compatibility/read-only research, NOT main positive target or Held-out. Authoring cutoff not independently established by native version history. |
| `D:/Desktop/solidworks/三爪夹具/2号连接件.SLDPRT`, `腕关节.SLDPRT` | External engineer assertion, already M13/M14 screened; detailed authorship unknown | Existing refusal research; not fresh Held-out. |
| `D:/Download/压制.SLDPRT`, both recorded configurations | External engineer assertion, already screened | Suppression/configuration/two-direction negative input; not fresh Held-out. |
| Mecanumbot Body Front/Top/Side Plate | Independently verifiable third-party pre-intake candidates, now screened | Josh Villbrandt, 2017 fixed commit `c50a655aef61ac1a6340b551ec4e837e54e871c4`, Apache-2.0, verified blobs. Unsupported histories; reuse read-only evidence, not fresh Held-out. |
| Coaxial UAV `plate.SLDPRT`, `columnPlate_short.SLDPRT` | Independently verifiable third-party pre-intake candidates, now screened | Aubrey/AubreyC, 2016 fixed commit `75b8fbb7f0f55fe5f08d517074a916808586b818`, verified blobs; license not observed. End condition 6 unqualified; not fresh Held-out. |
| Installed Base Plate, Beam Splice, HASCO cavity plate | Third-party/vendor custody, precise author/workflow unknown | First two multi-config; cavity table/equation/external-link negative. No supported positive or Held-out claim. |
| Uncatalogued/unknown-origin files | Source unknown | Not admitted to independent acceptance without provenance; location/extension is insufficient. |
| Fresh eligible Held-out | **None frozen** | Material gap, not proof that suitable models cannot exist. |

Catalog `previouslyUsedForTuning=false` means no recorded production tuning, NOT an everlasting Held-out designation. All five downloaded candidates have been examined; current instruction rules out relabeling them. Upstream provenance records and original failed screens remain untouched.

**Future eligibility, fixed before selection/intake:** verifiable independent native author/workflow and pre-intake date; actual feature history, not imported geometry masquerading as qualified accessors; lawful test custody; untouched source hashes; no prior debugging/tuning of selected task combination; supported candidates assessed against the published bounded contract below. No required author name, dimension, fixture ID or exact three-hole layout. Record every selected source before screening, freeze unsupported expectations and the independent Oracle before formal execution; preserve rejected selection and failed attempts. New source, binaries, task values and order require an immutable new run, not changes to a failed slot.

## 4. Actual Production Support

Source review, not an assertion of universal CAD support:

| Boundary | Implemented restriction / reason | Status relative to M14 |
|---|---|---|
| Host | One four-line rectangular blind boss/base, world-XY aligned, no reverse/offset/thin/draft/thickness link; one solid | Measured plate abstraction/Oracle coverage restriction, **not** a general PRD external-model axiom. See [qualification](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/ExternalNativeQualification.cs:149). |
| Hole | One consumed ProfileFeature with one nonconstruction circle; exactly one writable driving diameter/radius; through-all, no unsupported start/flip/driver | Qualified native subtype and ownership safety. Zero/multiple candidate dimensions refuse AMBIGUOUS_NATIVE_DIMENSION. Hole Wizard/blind/multicircle cuts remain read-only. |
| Pattern | Single active direction, one qualified hole seed, straight in-plane edge direction; no skipped/varying/body/face/geometry pattern or shared seed | Specific subtype qualification, not general rectangular-pattern support. Production permits arbitrary measured in-plane direction; existing test Oracle assumes +X. |
| Quantity/layout | Up to 16 hole features, 8 patterns, 256 total instances; finite clearances and complete volume/bounds/cylinder surfaces | No fixed three-hole/one-pattern/120x80 predicate. Production geometry is variable within these limits. [Planning](D:/CAD-Harness0.2/src/CadHarness.State/ExternalEditPlanning.cs:56). |
| Drivers/configuration | Whole document rejects multiple configs, any equation/table/linked equation/external file reference; sketch external relations rejected except exact local-origin circle-center coincidence | Fail-closed implementation is broader than withholding only the affected parameter. Single-config and no-cross-sketch-link assumptions are current coverage limitations, not universal source eligibility rules in PRD. [Drivers](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/ExternalNativeQualification.cs:57). |
| Unknown history | Any non-neutral unqualified physical node rejects editable intake, including an unrelated branch | Conservative whole-package qualification. PRD permits, but does not require, proven unaffected-branch editing. Do not relax merely because B/C have no children. |
| Identity/dependencies | Semantic IDs derived from document/config/native reference; native round-trip exact interface/type/health; complete parent/child graph; unchanged inventory/dependency sets | Required safety. Names/tree ordinals not identity; unbound diagnostic IDs never grant editing. [Observation](D:/CAD-Harness0.2/src/CadHarness.State/ExternalObservation.cs:71), [inventory identity](D:/CAD-Harness0.2/src/CadHarness.State/ExternalInventoryIdentity.cs:7). UI-only fallback names are not mutation binding keys. |
| Scalar/Batch gate | Four exact qualified rows plus separate atomic qualification; live intake/driver/geometry checks still required | Both real public entries open only for qualified sessions, not Registry membership or provenance. [Gate](D:/CAD-Harness0.2/src/CadHarness.State/V03NativeQualificationCandidates.cs:46), [session](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/ExternalPartSession.cs:117). |
| Complete geometry | Plate-minus-disjoint-cylinders analytic volume; XY bounds; full cylinder center/radius/axis/through-boundary matching | Implicit plate geometry assumption. Cannot verify arbitrary solids, extra bosses/fillets or intersecting holes by this Oracle. |

The [independent test Oracle](D:/CAD-Harness0.2/tests/CadHarness.ExternalEditing.Tests/NativeEditOracle.cs:65) additionally assumes centered bounds, bottom Z=0, one +X pattern and fixture labels. These are **test specifications**, not production selection criteria. It cannot silently score translated/rotated/multiple-pattern Held-out inputs correctly; use a separately frozen spec-derived native Oracle for their actual declared geometry, without production helper reuse or changing tolerances.

A1's hole profiles have actual DISTANCE relations to host edges; the host also has local-origin coincidence outside the single-circle exception (v19/probe). `swExternal` here means external to the sketch, not necessarily another file. No evidence proves these inputs safe under the current writable-driver contract. Recognized scalar readers in [inspection](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/ExternalPartInspection.cs:199) do not themselves qualify editing.

Reusable future work could resolve exact same-document relation owners and propagation, generalize a measured plate-local frame, or qualify unaffected branches with a complete material Oracle. These are real **optional capability gaps**, not demonstrated defects or mandatory fixes for A1. General solids, new subtypes, Hole Wizard, two-direction arrays and query-driven target discovery exceed current M14 qualification and must not enter C2 merely to harvest a positive sample.

## 5. Remaining Gap Classification

A = evidence gap; B = production capability gap; C = unsupported by the current qualified scope; D = material/Oracle/infrastructure gap. Multiple labels identify separate problems, not an inferred bug.

| Item / PRD | Existing proof / actual gap | Class | Blocks M14? / owner / minimum action |
|---|---|---|---|
| Independent positive scalar + batch + cold; M14 goal, Section 15 | A/B fully pass on development data; no supported pre-intake independent positive | A+D | **Yes / C4.** Find eligible native source covering four rows and two independent targets; freeze and use public entries/independent complete Oracle. Can combine in one source/run. No new engine needed. |
| Held-out external edit; Sections 14, 21.7 | No fresh eligible supported combination | D then A | **Yes / C4.** Same independent positive may satisfy Held-out if selected/frozen under predeclared custody criteria, never tuned on first. |
| Geometry-equivalent different histories; Section 15 | Rename/reorder maintains geometry but is not distinct authoring history; no measured independent equivalent pair | D+A | **Yes / C4.** Acquire and independently measure an equivalent pair. One qualified edit and one declared read-only history suffice; do not demand two editable variants. |
| Independent unsupported unrelated/affected history; Section 15 | v22 graph/refusal strong but Factory-origin; third-party refusals exist but no mapped equivalent/affected/unrelated matrix | D+A | **Yes, coverage gap / C3 mapping then C4 material.** Reuse independent graph evidence if sufficient; otherwise one eligible negative source can cover both relations, with zero mutation. Whole-package refusal allowed. |
| Genuine ambiguous same-size binding refusal; Sections 8, 15-16 | v20 proves exact-reference success only; unique-dimension/owner guards exist, no actual native ambiguous-binding trial | A+D | **Yes / C3.** Freeze two real same-size candidates and an actually underdetermined native identity/ownership condition; exercise the existing admission/binding refusal before Setter/checkpoint. Explicitly show why exact target cannot be established. |
| Same-size ambiguity versus P0 exact IDs | No size/name-query API exists; exact IDs resolve distinct targets by design | C for query extension | Do not add M18 to make a negative test. Duplicate request/absent ID/schema refusal or an unrelated multi-dimension refusal is NOT sufficient proof of same-size ambiguity. If no applicable P0 ambiguity can be exercised, keep this item UNVERIFIED and resolve interpretation against the original contract before declaring COMPLETE. |
| Single table-only isolation/dynamic table edit | Actual table dimension and combined refusal; dynamic trial failed | A+D, supplemental | **No additional gate / optional C3.** Reuse static refusal; no Excel/tool expansion solely to pass an unstated condition. No table editing permission. |
| A1 edge/origin relations, rotated/nonrectangular/multihost solids, multiple configs, unaffected branch editing | Current code intentionally cannot qualify these; no representative in-contract defect established | B (optional reusable extension), C (unqualified histories) | **Not mandatory / conditional C2 or later.** Only exact relation ownership/frame evidence with a representative justified use case warrants a bounded extension. Never fixture-specific exceptions. |
| Delete/suppress/config/source/rename/fault/cold | Strong v14/v15/v20/v25/v28 proofs above | No open gap for demonstrated behavior | Reuse. No full native rerun or extra engineer fault matrix just to change provenance. C4 still independently tests accepted positive geometry/cold. |

No mandatory production defect is established by this audit. Material scarcity does not prove an implementation bug. Conversely bounded plate support must be disclosed rather than advertised as general external-Part support.

## 6. C2 / C3 / C4 Boundaries and Shortest Path

**C2 Compatibility Extension: skip by default.** Entry requires a representative failure inside an agreed M14-qualified intent, exact root-cause evidence and a reusable minimal correction. Completion requires targeted pure/native/public/cold proof, unchanged safety and no fixture predicates. If new relation/frame capability is deliberately admitted, it needs separate native qualification and a fresh formal source version; never relabel its discovery model Held-out. Do not widen native subtype scope or rebuild transactions.

**C3 Negative Evidence Completion: recommended next execution stage.** First map/reuse existing strong proofs. Execute only genuinely missing original-PRD negative coverage, especially same-size ambiguous binding and independent graph mapping. Freeze the actual unsafe native condition and expected refusal first; prove no Setter/checkpoint, source preservation and cleanup. Completion requires an honest evidence row per original required negative; unresolved P0 ambiguity interpretation remains explicit, not a fabricated PASS. Do not repeat table dynamics, A1 positive attempts or M14A/B.

**C4 Independent Held-out Final Acceptance:** establish provenance/contract criteria now, obtain fresh eligible material before formal execution, then freeze production, task combinations, hashes, source history, public calls and independent Oracle. Minimum coverage can share one independent supported positive for four scalars, exact two-target batch and separate-controller cold, plus an equivalent-history read-only counterpart and required unsupported-branch material (combine roles only when facts prove them). Do not prescribe a fixed Part count or every row twice. All source/working/untouched/native/state/revision/recovery checks apply. Preserve first-attempt failures; production repair means a new version/run and fresh eligible combination, never changed expectations.

**M14 COMPLETE requires** the reused four-row/atomic/recovery matrix plus the missing independent positive, held-out, equivalent-history/source coverage and genuine ambiguous refusal above; no unresolved recovery/ownership and protected originals. C4 must score support/refusal separately. M14 completion does not certify M20's full construction/provider/history matrix.

Stop investing in forced A1 support, already-screened candidate recycling, general Hole Wizard/configuration/solid modeling, duplicate A/B schedules, and Design Table dynamic-edit tooling for a rejected input. Start C3 while arranging C4 material under the predeclared criteria; trigger C2 only on a proven reusable need, not automatically.

## 7. Offline Preservation Checks

- Clean starting worktree/HEAD verified. All **91 nongenerated production `.cs` files** match the tested v15 freeze. No production functionality changed in this audit.
- Original PRD SHA256 still `17412193a33228e376fe4cecd24576e3d0b6a5a4a5b43f62d4dd568235026275`, matching its frozen copy.
- Rehashed all **2,315 identities** listed by [final M14C audit](D:/CAD-Harness0.2/artifacts/milestone14/final-m14c-v1/audit.json): zero mismatches. Rechecked **17 unique catalog/Registry native sources**: all match. This is identity preservation, not a rerun of their native tests.
- Ledger unchanged: **119 opens / 119 closes / 0 new Parts / OwnedTitles=[]**; SHA256 `e7d11da2edac4cb1dcb6df8cb3f9e2705831d53d1326b5a226872a4e5efcd554`. No new native calls. Historical authorization/events/failures remain intact.
- Core authority still **R11**; current pointer resolves the verified manifest SHA256 `7fb4edd53c234b2f9587ff675ca93ddee491304a345543dce23c3fcf30fdcf95`; revision native and working SHA256 both `b7e1b5f85cdfdf15ec61b8f048fcf70f9c5d10a6b58bacd20ff4b6f4fc8ab353`.
- No `recovery.json` outside retained pure/regression evidence under M14. Historical audit's 141 pure/regression markers are intentional fixtures, not native leaks. No live SOLIDWORKS-state claim is made without attachment; this task neither owns nor manipulates documents.
- Reuse latest pure **86/86** (`pure/20261010T022346282/result.json`) and six suites exit 0 (`regression/20261010T020606270/result.json`). No tests rerun for a documentation-only audit. Final `git diff --check` is the delivery check.

Earlier reports, including their broader residual lists and failed attempts, remain unchanged. This audit corrects their interpretation, not their history.
