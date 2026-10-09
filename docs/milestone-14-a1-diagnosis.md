# M14 A1 Read-Only Diagnosis

Status: PARTIAL; native verification explicitly paused by the user. No new native opens after that request.

## Evidence Boundary

- Retained observation: `artifacts/milestone14/source-v3/intake-result.json`.
- Observed A1 SHA256: `7182c79520636053c4fed3bcde3c5b947e57f7550c921645db22f638d3af7eef`; 95,407 bytes.
- Current A1 SHA256 after the user's reported sketch change: `aae0907a25a0e4068e3d5362f85a7f9529525b4419d1d597b9db7f8449f64bac`.
- The retained observation describes the old file, not the updated file. No inference that the new file has passed.
- Cumulative native budget: 0 new Parts, 2/12 opens, 2 closes, no owned document remaining. The two attempts made no parameter changes and never published a revision.

## Cut-Extrude2 Facts

The feature displayed as `切除-拉伸2` has native type `ICE`, a healthy persistent feature reference, and an unrecognized observed subtype. It has one observed cylindrical face with radius 7.5 mm and approximate bounds X [-7.5, 7.5], Y [-12.5, 2.5], Z [0, 10]. This is evidence of cylindrical geometry, not proof of a single-circle profile or a through-all native end condition.

Recorded immediate parents: origin, host extrusion, and Sketch3. No outgoing edge from this cut was recorded. Its dependency completeness was UNKNOWN, so the absence of outgoing records is not proof that there are no children.

The old observation did not retain the native thin/both-directions/draft flags, the numerical end condition, the exact count of consuming sketch interfaces, the active circle count, or sketch constraints. These facts cannot be reconstructed from a cylindrical face or a displayed feature name. No native call was made to obtain them after the user's pause.

## Recognition Logic

The former reader counted every parent returning `ISketch`. An `OriginProfileFeature` can be a constraint parent and expose a sketch interface without being the consuming profile. The recorded origin dependency therefore makes an origin-versus-profile misclassification a plausible Harness defect, but the old evidence does not prove the runtime interface returned by that parent.

The reader and editor now restrict consuming profiles to native `ProfileFeature` before checking `ISketch`. They still require exactly one genuine profile, exactly one active circular segment, and the original through-all/nonthin/one-direction/nondraft contract. Extra genuine profiles, extra active circles, and unknown/3D profile types remain unsupported. Display names are not binding evidence.

Coincident constraints are not rejected by the subtype reader. Replacing a coincident-to-origin constraint with dimensions may remove the origin dependency and mask the former counting problem; it is not required merely to satisfy subtype recognition. M14 driving-value qualification remains separate and checks native dimensions, equations, tables, external links, geometry and dependencies.

Future observations now record the exact failed subtype flags and consuming-profile/active-circle counts in the read-only reason. This instrumentation is not yet native-verified.

## Editing Impact

- The host extrusion has a recorded path to Cut-Extrude2. Under the PRD unknown-downstream rule, its depth cannot be advertised until that cut is qualified or otherwise safely verified.
- Recorded seed-hole -> linear-pattern path does not run through Cut-Extrude2.
- Recorded Hole3 path does not run through Cut-Extrude2.
- Hole2 itself is unqualified. Unknown dependency completeness prevents a positive claim that the other targets are independent solely from these records.
- The current finite whole-plate oracle requires every material-removal feature to be recognized. This additional conservative qualification boundary currently rejects the package as a whole, even for targets with no recorded path through Hole2. This is distinct from a demonstrated downstream dependency.

## Earlier Driver Refusal

The first actual refusal, before mutation/checkpoint, was `UNSUPPORTED_PARAMETER_DRIVER`. Retained facts: configuration count 1, equation count 0, linked-equation-file false, external-reference count 0; the old check recorded design-table object non-nullness as `designTable=True`.

Object non-nullness alone may describe a SOLIDWORKS table service, not an actual design table. The revised check records table interface, native row/column counts and native table-feature inventory; it rejects actual or unknown table drivers. No new native verification has been performed, so this report does not assert that A1 contains a design table or that the revised check has passed.

## Verification

M14 pure tests: 53/53, including exclusion of an origin sketch, rejection of two genuine profiles, rejection of unknown/3D profile types, and the still-closed production qualification gate. Compile: zero warnings/errors. These are pure/compiled checks, not native edit qualification.

The four native scalar rows, independent two-target native batch, native first-edit/file/state-publication rollback and cold reopen acceptance remain uncompleted. M14 and P0 are not COMPLETE.
