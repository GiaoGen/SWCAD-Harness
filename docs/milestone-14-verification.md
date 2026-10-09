# Milestone 14 Verification

PRD: `SWCAD_Harness_v0.3_PRD.md`, v0.3-draft-2, sections 8, 9 and Milestone 14.

Status: **PARTIAL**. Native verification paused at the user's explicit request while diagnosing A1. No M15+ implementation.

## Implemented Foundation

- Typed scalar/EditSet dispatch reuses `RequestMutationTransaction`; strict source/copy fingerprints, document/configuration/revision and expected old values.
- Prepare-all projection validates the final analytic model and dependency-safe intermediate order before native setters; aggregate ChangeSet/DirtySet, one checkpoint and one revision.
- Complete external observed companion persists without a construction `CadProgram`. Only recognized qualified nodes adapt to legacy transaction state; unknown inventory remains external/read-only.
- Existing `ManagedRevisionStore` protocol extended by observed payload mode: flushed native snapshot and external state, immutable manifest, prior-revision authority, small atomic pointer and exact journaled crash recovery. Managed v0.2 payload behavior retained.
- Existing extrusion/hole/pattern parameter-handler classes implement observed-feature access, without synthetic operation definitions. Persistent re-resolution, configuration/driver checks and independent complete plate geometry verification.
- Owned working-copy session, source checksum preservation, saved cold readback and byte-snapshot rollback; failed recovery quarantines mutation.
- Finite candidate geometry: one world-XY rectangular prism, at most 16 single-circle through-hole targets, 8 one-seed single-direction feature patterns, 256 total hole instances. Unknown material-removal history refuses; configuration overrides, external/equation/table drivers, multibody, second-direction/skipped/varying/body patterns and Hole Wizard refuse.

## Evidence

Baseline freeze: 16,440 source/historical evidence entries, recorded before production edits. Native source/binary snapshots are retained per attempt; earlier M11-M13 artifacts are not overwritten.

Native attempt namespaces:

- `source-v1`: snapshot preparation failed on the historical Windows reserved name `aux.json`; no native open. Retained partial snapshot.
- `source-v2`: A1 intake refusal; 1 open/close, no mutation.
- `source-v3`: detailed A1 driver refusal; cumulative 2 opens/closes, no mutation. Frozen source/binaries and original hashes retained.

Native budget: **0 new Parts / 2 of 12 opens / 2 closes**; no owned document remains. User explicitly prohibited further native opens during diagnosis. Current A1 changed after user editing and is a different fingerprint; previous native evidence cannot be applied to it.

M14 pure: **53/53** (`artifacts/milestone14/pure/20261009T045011267/result.json`). Earlier pure run caught a touching-hole test proposal; that proposal was fixed to retain positive clearance, not by relaxing the oracle.

Isolated regression runs `artifacts/milestone14/regression/20261009T044249739/result.json` and `artifacts/milestone14/regression/20261009T044950064/result.json`: M11 76/76; M12 48/48; M13 27/27; state 13/13; transaction and construction suites passed. The second run includes the profile-candidate fix. Full solution built with zero warnings/errors; the subsequent production qualification-gate check was compiled and covered by the final M14 pure run.

## Required Four-Row Matrix

| Row | Observed old A1 target | Mutation / parameter + geometry oracle / cold reopen | Negative evidence | Acceptance |
|---|---|---|---|---|
| Extrusion depth | Host depth 10 mm | Not executed | Package rejected before mutation; unknown cut is recorded downstream | NOT QUALIFIED |
| Through-hole diameter | Seed 10 mm; Hole3 5 mm; Hole2 unrecognized with 7.5 mm cylindrical radius | Not executed | Hole2 classification/driver uncertainty; no target has edit acceptance | NOT QUALIFIED |
| Linear-pattern count | One direction; count 2 | Not executed | Driver/refusal boundary only, no qualified editable row | NOT QUALIFIED |
| Linear-pattern spacing | D1 spacing 40 mm | Not executed | Driver/refusal boundary only, no qualified editable row | NOT QUALIFIED |

No native executable capability is advertised by the existing Planner/M11 candidate gate. The public external session edit entry also explicitly invokes that closed qualification gate before checkpoint/mutation. Handler/transaction foundations are experimental until this matrix is independently qualified; no P0 completeness claim. The current native runner implements intake/diagnostics only; the planned scalar/batch native acceptance schedule is not yet implemented.

## Remaining Acceptance

Confirm the recognition and table-driver diagnosis with separately authorized read-only native evidence; freeze the updated source fingerprint and binary schedule. Then complete all four positive/negative scalar rows, independent two-target aggregate native batch, native rollback of both targets after first-edit failure and file/state-publication failures, and fresh-controller cold readback. Pure mocks do not substitute for those events.

Details: `docs/milestone-14-a1-diagnosis.md`.
