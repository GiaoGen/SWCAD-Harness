# SWCAD-Harness v0.3: Independent CAD Fixture Factory Addendum

## Scope and Status

This supplement defines a support tool, not a production milestone or a new M14 qualification. The historical PRD, engineer-model evidence and milestone ledgers remain unchanged. The user authorized implementation and a separate preparation budget; the latest direction explicitly forbids starting M14 after preparation.

The original section 15 provenance cutoff cannot be satisfied by newly generated files. Such files are **post-intake, independent-workflow development fixtures**, never retrospectively described as pre-intake engineer-authored fixtures. They cannot alone meet M14's engineer-authored acceptance goal. A1 remains an independent/negative compatibility case, with its old failures preserved. New fixtures do not prove that A1 is fixed.

## Architecture and Independence

- `tools/CadFixtureFactory.Contracts`: bounded independent specifications, metadata, file identity and preparation accounting contracts. It contains no Harness contracts.
- `tools/CadFixtureFactory.Native`: official COM attachment, STA, exclusive controller lease, ownership, budget and resource protection only. No modeling or geometry oracle.
- `tools/CadFixtureFactory.Builder`: official SOLIDWORKS API construction from an independent specification; one new owned document, native save and close, immutable manifest. No production IR, Planner, Binder, Transaction, CADState, Handler or geometry utility references.
- `tools/CadFixtureFactory.Reader`: separate executable/process, read-only cold reopen. Its own spec-derived analytic oracle, native feature/driver/configuration/dependency and complete-body checks. It does not reference Builder, rebuild, save, repair, or score a Harness edit.
- `tools/CadFixtureFactory.Tests`: pure specification, oracle and dependency-isolation tests. No COM activation.

Builder and Reader source/binaries, specifications, schedule, authorization and baseline hashes are frozen before preparation. Each controller verifies this freeze before native access. Changed source requires a new namespace; failed packages, controller logs and proofs are retained. A successful Reader publishes an immutable `ready.json`, explicitly marked **PREPARATION_SELF_CHECKED_NOT_M14_ACCEPTANCE**.

Source snapshots are retained, not just hashes of mutable working-tree paths. Native history records both `GetTypeName2` (including an `ICE` wrapper) and `GetTypeName` (the underlying native type). The Reader checks the actual definition interface and exact geometry, not a localized tree spelling. Base `Extrusion` is distinguished from an additive Boss; a boolean `IsBossFeature` is not a test for base extrusion.

A corrected Reader may recheck an existing failed preparation source only under a new frozen namespace, with the original manifest/spec/Part and failed proof frozen, one newly charged open, and a separate proof destination. It cannot edit the source, replace the original failed proof, or improve the original run's reported success rate. The new `ready.json` links both original Builder provenance and the new Reader freeze.

## Proposed PRD Clarifications

| Clause | Clarification |
|---|---|
| Section 14, development/held-out isolation | Factory IDs, values and histories are test data only, never production predicates. Development results cannot be counted as held-out results. Held-out specs and hashes require independent custody and a separately authorized schedule; none are generated in this run. |
| Section 15, independent provenance | Permit explicitly labeled post-intake independent-API fixtures for development and supplementary regression. Keep pre-intake/engineer-authored provenance and coverage obligations separate. Never replace a failed engineer source or claim historical failure resolution by changing fixtures. |
| Section 16, independent oracle and freeze | Factory readback is preparation-quality evidence only. Formal Harness edit correctness requires a separately frozen runner and oracle; neither Factory executable may repair or judge edited copies. Freeze the exact source/spec/binaries and schedule before preparation, preserve all failures. |
| Section 17, native lifecycle/budget | Add a separately authorized fixture-preparation ledger. Reserve every creation/open attempt before the native call, including failed attempts. Preparation opens are not formal opens. Any later Harness inspection/edit/reopen is charged to that milestone's existing ledger. No reset, transfer or retroactive reclassification. |
| M14, zero new Parts and engineer-authored goal | Zero new Parts applies to its formal run; all independently generated sources must already be closed, hashed and self-checked before intake. This does not waive engineer-authored or held-out coverage, nor change its 12-open cap. A claim of M14 completion remains subject to its original matrix. |

## Budget and Resources

Authorized preparation ceiling: **6 creation attempts / 8 native open attempts**, for four development fixtures, no Held-out. Each successful fixture normally costs one creation and one independent cold reopen. Spare slots are not automatic retries: a source fix requires a new frozen evidence namespace and unchanged cumulative accounting.

On 2026-10-09, after the original preparation ledger reached 6 creations / 8 opens, the user explicitly authorized **an additional 6 creations / 8 opens**. The original authorization remains byte-identical. A separate immutable `authorizations/prep-v8.json` binds its hash and records cumulative ceilings **12 creations / 16 opens**, scoped only to the new frozen namespace and preparation. Counters are not reset; no budget is transferred to M14. The next required operation is one read-only recheck, not another creation. Later namespaces require a separate explicit grant binding, not automatic inheritance of this file.

M14 stays at **2 / 12 cumulative opens**. This tool never invokes M14 or updates its ledger. A pre-Factory baseline includes production source and all historical M14 artifacts; the final audit must verify every entry.

Attach only an existing SOLIDWORKS application. Never launch or terminate it. One owned document at a time, ownership recorded before modeling, read-only Reader opens, no adoption/closure of engineer documents, restore original active document without rebuild, restore temporary global dimension preference, close/discard owned documents. Abort at unresponsive process or GDI >= 7000. Controller timeout stops only its own process and leaves a conservative unresolved ownership record; no further native run until inspected. Keep all creation/open/close/restoration/PID/GDI lifecycle records.

## Initial Development Fixtures

All are single-configuration native `.SLDPRT` files. Host: centered XY plate, 120 x 80 x 10 mm. Seed: diameter 8 at (-40,20), +X linear pattern count 3/spacing 25. Independent holes: diameter 10 at (-30,-20), diameter 6 at (35,-20), each from a separate single-circle sketch and through cut, not Hole Wizard. The first case is the minimum scalar and two-independent-target development source.

| Fixture | Independent specification | Preparation checks |
|---|---|---|
| `dev_core` | Fixed circle centers, driving diameter dimensions, one-direction pattern, two independent through holes | Clean rectangle/through-cut/pattern definitions, drivers, dependency independence, all 5 through cylinders |
| `dev_origin` | Same except diameter-10 hole at (0,0) with native origin coincidence | Fully constrained profile, origin relation/parent, transformed geometry; origin is not an extra physical profile |
| `dev_unknown_descendant` | Core plus separately profiled blind cut diameter 6 at (45,25), depth 2 | Genuine unsupported-for-M14 downstream cut, exact blind boundaries and extra volume removal; no assertion of formal refusal behavior |
| `dev_equation_driver` | Core with native equation driving host depth = 10 mm | Exactly one active, non-global, non-suppressed equation; resolve its target to the native host depth and read the saved equation value. Builder uses an initial 11 mm depth and verifies the equation drives it to 10 mm before save; Reader never solves/repairs the model. No assertion of formal edit qualification |

Manifests include generation method, SOLIDWORKS/template version/hash, frozen Builder/spec/source identities, native feature persistent references/history/configuration/drivers/definitions, expected geometry, file SHA256/size and ledger. Reader proof includes independent full-body measurements, checks, reader binary identity, process/time, configuration and active restoration. Files are development fixtures, not production examples or formal acceptance results.

## Operation

1. Run `scripts/test-fixture-factory.ps1`; this builds only Factory projects and performs no native activation.
2. Freeze with `scripts/prepare-cad-fixtures.ps1 -RunId <new-name> -FreezeOnly -PartTemplate <existing.prtdot>`. Freeze the explicitly selected existing template; do not change global SOLIDWORKS template preferences.
3. Execute one scheduled fixture with the same script and `-FixtureId <development-id>`. Builder and Reader run as separate hidden processes from retained frozen binaries; failures stop that slot. For a corrected-Reader-only recheck, freeze a new run with `-FixtureId <id> -RecheckPackage <original-failed-package>` and execute that explicitly scheduled recheck; it costs one open and zero creations.
4. Inspect manifests/proofs/accounting and audit baseline. Stop at preparation. M14 requires a later explicit continuation, its existing budget and a new formal freeze.
