# M14 P0 Ambiguity Clarification

Read-only contract/source/evidence review at HEAD `cd5e8e5`, 2026-10-10. No production, PRD, model or ledger changes; no SOLIDWORKS attachment or native tests. This is a proposed acceptance interpretation, not approval to change a requirement or upgrade M14 to COMPLETE.

## Original Contract

The original PRD at `aea97a3` and the current PRD agree on these relevant clauses:

| Clause | Actual requirement | Consequence |
|---|---|---|
| Section 8 | Target is selected by observed semantic ID, document/configuration identity and revision; native reference must resolve to expected type/owner | P0 does not discover a target by matching diameter/name/location |
| Section 9, P0 | Each edit supplies an exact semantic target; unresolved targets reject | No executable "edit whichever 10 mm hole" request exists |
| Section 9, P1 / M18 | Query over type/dimensions/relations returns a frozen target set and preview | Under-specified query candidate ambiguity belongs here; multiple matches are not necessarily ambiguous if the query intentionally and explicitly selects a set |
| M14 required matrix / Section 21.3 | Each row needs one negative **unsupported/ambiguous** variant | A demonstrated unsupported case can satisfy the row; four separate ambiguous native variants are NOT required |
| Sections 15 and 16 | Separately require ambiguous same-size features and at least one ambiguous binding refusal | This obligation is explicit, but its applicable P0 native condition/test procedure is not defined; it cannot silently be erased by the row-level alternative |

References: [PRD exact target](D:/CAD-Harness0.2/SWCAD_Harness_v0.3_PRD.md:222), [P0 envelope](D:/CAD-Harness0.2/SWCAD_Harness_v0.3_PRD.md:230), [P1 query](D:/CAD-Harness0.2/SWCAD_Harness_v0.3_PRD.md:250), [negative matrix](D:/CAD-Harness0.2/SWCAD_Harness_v0.3_PRD.md:366), [benchmark outcomes](D:/CAD-Harness0.2/SWCAD_Harness_v0.3_PRD.md:380), [M14 row](D:/CAD-Harness0.2/SWCAD_Harness_v0.3_PRD.md:444).

## Actual P0 Execution

1. Native inventory creates document/configuration-local IDs from verified native references. Duplicate capture/persistent identities reject `AMBIGUOUS_NATIVE_INVENTORY`; normal repeated traversal of the same COM feature is deduplicated.
2. Public qualification requires an exact semantic ID and qualified parameter/accessor. Planning uses a dictionary keyed by semantic ID, not a dimension-based candidate list.
3. Before mutation, native `GetObjectByPersistReference3` returns a single object or resolution failure. Reference bytes, expected type, health and round-trip must match. No fallback by name, diameter or nearest position exists.
4. An exact feature can still have an unsafe internal binding: profile ownership must have exactly one candidate; a hole needs exactly one radius/diameter candidate; seed and native cylinder correspondence must be unique. These are genuine P0 safety guards, distinct from selecting between two equal-size features.

Code: [inventory uniqueness](D:/CAD-Harness0.2/src/CadHarness.State/ExternalObservation.cs:67), [native deduplication](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/ExternalPartInspection.cs:127), [public gate](D:/CAD-Harness0.2/src/CadHarness.State/V03NativeQualificationCandidates.cs:46), [explicit planning](D:/CAD-Harness0.2/src/CadHarness.State/ExternalEditPlanning.cs:135), [native resolution/ownership/accessor](D:/CAD-Harness0.2/src/CadHarness.SolidWorks/ExternalNativeQualification.cs:16).

`AMBIGUOUS_NATIVE_DIMENSION` also covers **zero** candidates, not just multiple candidates. That error code alone cannot prove actual ambiguity. Multiple parent sketches need not be multiple consumed owners. A duplicate request, fabricated identity or extra annotation does not automatically demonstrate the original same-size binding case. A first refusal at an unrelated driver gate is not proof that the ambiguity guard was reached.

## Evidence and Applicability

| Case | Current evidence | Correct interpretation |
|---|---|---|
| Two equal-size holes with distinct exact references | v20 `identity` + independent `identity-cold`: real diameters 7, then 8 after rename/reorder; public EditSet and complete Oracle PASS | Robust exact binding PASS, not ambiguous refusal |
| Stale/deleted native target, changed configuration/source | v25/v28/v20 public negative results | Actual identity-drift safety PASS, not multi-candidate ambiguity |
| Unsupported driver/subtype for all four Scalar rows | v12 driven dimension/GeometryPattern; v22 equation | Row-level negative requirement satisfied without an ambiguous variant |
| Under-specified diameter/name target query | No P0 query entry | Not an executable M14 case; must not add M18 merely to create it |
| Actual nonunique native inventory, owner or accessor | Static guards and limited pure checks; no sufficient public native ambiguity trial | Native branch remains UNVERIFIED; not proven impossible and not a demonstrated production bug |
| Wrist with several profile parents | v29 diagnostic; v21 qualification refuses earlier unsupported driver | Does not close the ambiguity evidence gap |

The previous close-out's "sole missing native ambiguity evidence" was a conservative reading of the global Sections 15/16, not a defect established in M14 or a missing source-provenance condition. It should not be interpreted as requiring four extra ambiguous parameter fixtures or a new ordinary equal-size-hole Part.

## Decision Needed

**Recommended:** explicitly allocate the acceptance definitions to the implemented interfaces, without changing production safety:

- M14: prove same-size exact targets remain separate through editing/rename/reorder/cold reopen; preserve pre-mutation rejection of unresolved/changed identities and unsupported/unsafe owners, drivers and dependencies. Keep the actual native ambiguity branch labelled UNVERIFIED unless a real applicable case exists.
- M18: verify under-specified query/selection ambiguity and exact preview confirmation. An intentionally selected multi-target set must remain distinguishable from ambiguous single-target intent.
- Do not make deliberately manufacturing a rare native identity/owner/accessor failure a compulsory M14 closure fixture when no reproducible applicable case is available. State this exclusion explicitly if approved; retain the native guard and its unverified coverage, not a fabricated PASS.

**This recommendation changes the allocation/necessity of the Sections 15/16 acceptance case. It is not merely a wording cleanup.** It requires explicit approval and a small, versioned PRD clarification before the unresolved case can stop blocking M14. Until then M14 remains PARTIAL under the unchanged technical clauses. Historical reports must not be rewritten as passes. The existing source-condition amendment does not authorize this separate change.

Alternative: retain the literal mandatory native-refusal case and M14 PARTIAL until a naturally applicable P0 case is found. First freeze what exact native ambiguity and public refusal are expected; do not ask the user to randomly build models or impose third-party authorship. There is currently no verified GUI recipe that guarantees this case without unrelated earlier disqualification, so no additional modeling request is justified.

## User Material and Safety

The user does **not** need to supply another ordinary Part, another author, or two same-size holes. Existing material suffices for the demonstrated M14 four-Scalar/atomic/recovery and same-size exact-identity requirements. It does not supply an actual applicable native ambiguity trial. If the strict alternative is selected, the missing material must be specified from a verified native ambiguity mechanism first, rather than invented by the user.

Review made zero native opens/new Parts. Ledger remains **145 opens / 145 closes / 0 new Parts / OwnedTitles=[]**. No production gate/tolerance changed, no source search resumed, no old report or failure overwritten. No regression rerun is necessary for this documentation-only clarification.

## Approved Disposition

On 2026-10-10 the user explicitly approved: "明确批准修订验收分配，然后再检查是否可用通过M14进入M15". The recommendation above is now recorded in PRD Sections 15/16 and the M14/M18 clauses. The preceding proposal and historical PARTIAL conclusions are preserved, not rewritten as native passes.

M14 requires equal-size exact-target robustness and unchanged identity/ownership/accessor/dependency safety. Query-selection ambiguity belongs to M18. The genuine P0 native ambiguity branch remains **UNVERIFIED** and its guard remains mandatory; constructing a currently unavailable native case is no longer an independent M14 closure gate. M15's own spatial-reference ambiguity test remains mandatory. See the separate [amended M14 acceptance and M15 entry decision](D:/CAD-Harness0.2/docs/milestone-14-final-acceptance.md) for evidence and final status under the approved revision.
