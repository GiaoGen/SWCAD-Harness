# Independent CAD Fixture Factory Preparation Report

Date: 2026-10-09. Outcome: **four self-checked development fixtures available; M14 NOT RUN**.

## Delivered Sources

| Development fixture | Native source | Selected independent cold-reopen proof |
|---|---|---|
| `dev_core` | `artifacts/fixture-factory/runs/prep-v3/packages/dev_core/dev_core.SLDPRT` | `prep-v4/packages/dev_core/reader-proof.json` |
| `dev_origin` | `artifacts/fixture-factory/runs/prep-v4/packages/dev_origin/dev_origin.SLDPRT` | `prep-v4/packages/dev_origin/reader-proof.json` |
| `dev_unknown_descendant` | `artifacts/fixture-factory/runs/prep-v4/packages/dev_unknown_descendant/dev_unknown_descendant.SLDPRT` | `prep-v4/packages/dev_unknown_descendant/reader-proof.json` |
| `dev_equation_driver` | `artifacts/fixture-factory/runs/prep-v7/packages/dev_equation_driver/dev_equation_driver.SLDPRT` | `prep-v8/packages/dev_equation_driver/reader-proof.json` |

Proof paths in the last column are relative to `artifacts/fixture-factory/runs`. The authoritative immutable selection registry is `artifacts/fixture-factory/audits/final-v1/fixture-registry.json`, linking SHA256/size identities for each Part, manifest, proof and ready descriptor. A Reader-only recheck links the original source; it does not duplicate, modify or replace the original file/failed proof.

Every selected source passed a separate process's read-only cold reopen, native persistent history/type/definition/dimension/configuration checks, profile solver/world geometry, single solid, complete envelope/volume/cylinder boundary oracle, independent-hole dependency checks, close, active-document restoration and unchanged file hash. The origin fixture includes a real native origin-coincident relation. The blind negative includes a real 2 mm blind cut, not a synthetic metadata flag. The equation negative has an active non-global, non-disabled, non-suppressed equation resolved to the host depth; Builder independently verified its change from an initial 11 mm to 10 mm before saving. No Reader rebuild/save/repair or Harness edit was performed.

## Pure Verification

Factory Builder/Reader/Tests compile in Release with zero warnings/errors. **72/72 pure tests passed**, including strict specifications/JSON, bounds/overlap, no Held-out, immutable files/hash drift, geometry/dependency negatives, equation literal binding contract and driven/driving-state distinction, cumulative authorization/refusal boundaries, project and binary dependency isolation. No Factory project or Reader dependency graph references `CadHarness.*`; Reader does not reference Builder.

## Accounting and Cleanup

- Original authorization: 6 creation attempts / 8 opens, immutable `authorization.json`.
- Additional explicit user grant: +6 creation attempts / +8 opens, immutable `authorizations/prep-v8.json`, new frozen namespace. Cumulative ceilings: **12 creations / 16 opens**. No ledger reset or transfer.
- Actual cumulative use: **6 creation attempts / 9 opens**. All **15 created/opened owned documents closed**; owned-title ledger empty. All **16 controllers** restored the original active document and exited. Maximum recorded GDI: **2659**, below the 7000 abort threshold.
- M14 ledger remains byte-identical, SHA256 `7dec769284ff2bb78916a5b4d66bed4d8925d78107570e0f0937a9328c511db8`, still **2 / 12 opens**. M14 acceptance did not run.
- Final audit verified **4681 baseline source/historical-evidence entries unchanged**, including all prior M14 evidence. A1 current hash is still `aae0907a25a0e4068e3d5362f85a7f9529525b4419d1d597b9db7f8449f64bac`. No engineer original was modified.

## Preserved Failures and Limits

Preparation is not a 100% first-attempt success claim. All logs, frozen versions, failed Parts/manifests/proofs and unexecuted slot statuses remain recorded:

- `prep-v1`: failed before Part creation because the global default template setting did not identify an existing template. Subsequent namespaces explicitly freeze the installed standard template without changing global template preferences.
- `prep-v2`: Reader incorrectly counted physical history using wrapper type spellings; actual cuts exposed `ICE` with underlying `Cut`. The failed proof's restoration flag was captured before disposal; the lifecycle ledger independently records successful close/restoration. Nothing was overwritten.
- `prep-v3`: Reader incorrectly required base extrusion to be an additive Boss. The corrected Reader rechecked the same unchanged Part in `prep-v4`.
- `prep-v4` equation fixture: its earlier weak proof checked equation presence/geometry, not dimension target resolution. It is **superseded and not selected**. The stricter `prep-v5` recheck correctly failed because the equation retained the pre-save document-qualified dimension name.
- `prep-v7`: the corrected Builder generated a genuine equation-driven depth, but Reader incorrectly required ordinary driving state 2 instead of native equation-driven state 1. The failed proof remains. Corrected Reader passed the unchanged source in `prep-v8`, after separately authorized extra budget.
- `prep-v6`: frozen but never executed; no native budget consumed. All other unexecuted scheduled slots are explicitly listed as not executed, not counted as success or failure.

These are post-intake independent-workflow **development** sources, not pre-intake engineer-authored or held-out acceptance evidence. A1 compatibility remains unresolved/unverified by this tool. Factory self-check does not qualify any Harness accessor, prove formal refusal/rollback/edit behavior, or open the production candidate gate. No Held-out files were generated.

Architecture, PRD clarifications, budget/lifecycle separation and operational commands are documented in `docs/prd-v0.3-fixture-factory-addendum.md`. The next action, only upon a later explicit continuation, is to design/freeze the separate M14 run under its existing ledger, not reuse Factory proof as its correctness oracle.
