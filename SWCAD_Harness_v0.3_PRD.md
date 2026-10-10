# SWCAD-Harness v0.3 — Incremental Generalization PRD
## Existing SOLIDWORKS Part Intake, Controlled Editing, and Composable Parametric Modeling

> Version: v0.3-draft-2  
> Project type: **incremental continuation of v0.2**  
> Baseline: v0.2 Milestone 10 formal completion; Playground V0 is a local development interface  
> Target CAD: SOLIDWORKS native `.SLDPRT`  
> Primary language/runtime: C# / .NET 8 / out-of-process SOLIDWORKS controller  
> Input: natural language, typed model context, and an explicitly selected native Part  
> Scope: single-Part creation, recovery, inspection, editing, and same-Part transactions  
> Milestones: **11–20**, continuing the v0.2 numbering

---

# 0. Why v0.3 follows v0.2

v0.2 established a typed CAD Operation IR, runtime capability projection, semantic Binder, explicit design relations, CADState, SOLIDWORKS backend, validation, rollback, and an optional bounded Judge. Its formal Milestone 10 report records finite-scope completion, not arbitrary CAD or engineering-design competence. The older root README still contains a historical `M10 BLOCKED` statement; [the formal M10 verification](docs/milestone-10-formal-verification.md) is the completion baseline for this PRD. Before implementation, freeze the exact source and evidence baseline; do not infer a clean working tree from the historical commit alone.

Current production execution is still centered on one initial XY-plane extrusion, supported hole/pattern/edge operations, a managed live Part, and one parameter edit per planned transaction. The v0.2 state and edit projection assume a managed construction program and a known feature vocabulary. These assumptions must be made explicit and extended, not bypassed, for saved and externally authored Parts.

v0.3 makes two measurable advances:

1. A supported **engineer-authored native Part** can be inspected and edited safely without pretending that every feature originated from Harness IR.
2. Reusable sketch, datum, revolve, additive, and cut capabilities generate editable parts substantially beyond v0.2 plates and disks.

---

# 1. One-sentence definition

Extend the existing Harness so it can reopen its own saved Parts, create a bounded read-only semantic overlay for unfamiliar native Parts, safely edit explicitly supported native parameters, and compile more varied single-Part geometry from reusable typed operations while preserving the v0.2 validation and transaction boundary.

---

# 2. Core hypothesis and success boundary

The v0.3 hypothesis is that **small, composable CAD capabilities plus trustworthy native observation** can increase the range of created and edited Parts without adding one LLM tool or one production preset per part family.

“Accurate user intent” means that every explicit requirement is represented and checked, every inferred/defaulted dimension is disclosed, critical missing constraints are clarified or rejected, and native readback matches the accepted specification. A visually plausible solid is not sufficient.

For bounded part families, a user should not need to enumerate every construction step or cosmetic dimension. The system may use versioned engineering rules and declared defaults for noncritical choices. It must not invent load-bearing dimensions, fit classes, tolerances, material properties, or standards and present them as supplied facts. When necessary information is absent, return a short clarification request or a non-executable concept proposal.

**Core release (P0):** Milestones 11–17 and 20, including four mandatory part-family acceptance cases, supported external-model scalar editing, and a typed same-Part multi-target transaction.  
**Extension (P1):** Milestones 18–19, including natural-language/query-driven target selection and Batch Edit preview, relationship exploration, and Playground controls. Their absence must be reported as deferred scope and must not be disguised as a completed P1 capability. The P0 batch transaction remains available through a typed, explicitly targeted request.

---

# 3. Explicit non-goals

v0.3 does not require:

- SOLIDWORKS assemblies, mates, assembly generation, or cross-Part design propagation;
- STEP/imported BREP-to-feature-history recovery;
- arbitrary external Feature Tree conversion into executable `CadProgram`;
- unrestricted edits of an arbitrary native feature or arbitrary topology repair;
- multi-Part or distributed file transactions;
- automatic production drawings, complete GD&T, or manufacturing qualification;
- autonomous mechanical engineering from an underspecified one-sentence request;
- a commercial SOLIDWORKS plug-in UI or remote multi-user service;
- six production templates matching the example parts.

“Simple fit” in this release means a **single Part's typed interface dimensions or constraints** (for example bore diameter, coaxiality, mounting pattern); it does not mean creating a native assembly or proving a mating Part's full fit.

---

# 4. Architectural boundary and anti-explosion rule

```text
User intent / selected Part
    ↓
Requirement and assumption record (new, bounded)
    ↓
Native observation OR typed construction plan
    ↓
Runtime Capability Catalog + strict IR/intent validation
    ↓
Semantic Binder + relation/dependency checks
    ↓
Existing transaction coordinator and native feature handlers
    ↓
Native readback + affected/full validation + durable state publish
```

Reuse `CadProgramJson`, `OperationRegistry`, `RuntimeCapabilityCatalog`, `CadState`, `SemanticEntityBinder`, `DesignRelationEngine`, `RequestMutationTransaction`, `AtomicStateStore`, and existing SOLIDWORKS ownership/STA boundaries. Version their contracts where necessary; do not create a parallel general-purpose runtime.

The Planner continues to produce bounded structured intent/operations. Do **not** expose individual SOLIDWORKS COM calls, UI clicks, or one tool per part type. New operation kinds require reusable geometric semantics, typed inputs, finite parameters, pure preflight, native postconditions, state capture, and rollback. Part-family examples belong in tests, not production dispatch. Reusable component rules may compile into those operations, but cannot silently bypass capability validation.

Prevent a second form of explosion: a growing prompt/schema/handler list. Project only executable capabilities for the current mode, document, configuration, feature kind, and reference health. Prefer parameterized sketch entities and common placement contracts over separate operations for every slot, pocket, shaft, or flange variant. Keep hard limits on plan size, sketch entities, reference candidates, and native resources; revise v0.2's 12-operation limit only from measured acceptance needs, with matching parser/schema/backend limits.

---

# 5. Priorities and scope decision

| Capability | Priority | v0.3 release treatment |
|---|---|---|
| Saved Harness Part reopen and edit | P0 | Mandatory |
| External native Part inspection with read-only unknowns | P0 | Mandatory |
| Supported external scalar edits | P0 | Mandatory |
| Same-Part multi-target transaction and file protection | P0 | Mandatory typed, explicitly targeted batch; query/preview UI is P1 |
| Generic sketches and reference-based placement | P0 | Mandatory finite vocabulary |
| Revolved boss/base, extruded cut, additive boss | P0 | Mandatory finite vocabulary |
| Partial-dimension intent handling and provenance | P0 | Mandatory for supported cases |
| Query-driven Batch Edit and preview | P1 | Milestone 18, separately accepted |
| Relationship query and local structure view | P1 | Milestone 19, separately accepted |
| Counterbore/countersink, broader edge editing | P1 | Unscheduled extension backlog; individually qualified in a later named milestone |
| Playground expansion | P1 | Local developer UI only |

P0 does not promise every existing SOLIDWORKS feature can be edited. P1 cannot weaken P0 rejection, rollback, or readback guarantees.

Milestone 20 may follow Milestone 17 directly when P1 is deferred; do not mark Milestones 18–19 complete without their own evidence. Counterbore/countersink and broader edge editing have no implementation milestone in this core schedule. They require a separately approved, numbered extension with native qualification and budget. This keeps the two P0 delivery outcomes visible rather than hiding them behind an expanding feature list.

## 5.1 Requirement record and minimum user input

Before planning native geometry, normalize each requested dimension or relation into a record with `semanticKey`, value/unit, provenance (`given`, `derived_by_rule`, `defaulted`, `unresolved`), rule/version when applicable, confidence/ambiguity, and whether the value is critical to function. A rule may fill cosmetic or bounded layout choices; it may not fabricate strength, life, material, tolerance, or fit requirements. The accepted requirement record is immutable for one plan/revision and is included in the plan preview and evidence.

For C1–C4 the minimum input is part intent plus the interfaces and envelope that determine geometry: for example shaft section diameters and axial spans, sleeve mating bore and length, bracket mounting faces and hole interfaces, or plate envelope and slot count/layout. A phrase such as “make a transmission shaft” without those anchors returns `NEEDS_CLARIFICATION`, with the smallest set of missing critical facts. A user should not have to dictate CAD feature order, reference names, every small chamfer, or redundant dependent dimensions. At least two P0 test intents must omit a noncritical detail that the recorded rule/default fills; the generated result must disclose that choice before execution.

This release does **not** claim that a generic LLM can derive safe engineering dimensions from function alone. Rule/default tables are finite, versioned data and independently tested. A proposed plan is not authorized for mutation merely because the LLM confidently states an unsupported assumption.

Illustrative normalized request (field names are finalized with the M11 schema):

```json
{
  "intent": "make a flanged sleeve for a 20 mm shaft, 30 mm long",
  "requirements": [
    {"key": "bore_nominal_diameter", "valueMm": 20, "source": "given", "critical": true},
    {"key": "sleeve_length", "valueMm": 30, "source": "given", "critical": true},
    {"key": "outer_diameter", "valueMm": 32, "source": "defaulted", "ruleId": "sleeve-wall-v1", "critical": false}
  ],
  "unresolved": ["fit_class_if_tolerance_is_required"]
}
```

The nominal solid can proceed only if the unresolved fit class is outside the requested acceptance claim. If the user asks for a guaranteed shaft fit, that unknown becomes critical and execution stops for clarification.

## 5.2 Compatibility and versioning invariants

The v0.2 parser, serializer, schema, state validation, and native path remain a regression contract. v0.3 writes a new program/state schema version only after a strict migration or new creation; old v0.2 files are never silently overwritten. A migrated copy records old/new schema versions, original hashes, migration result, and rollback path. A v0.2 `CadProgram` cannot be reinterpreted as an external-model program. New construction/edit capabilities are derived from the backend's actual registered handlers and current document state, not merely from enum members.

`ParameterNode` in v0.2 requires a strictly positive scalar; v0.3 spatial coordinates and offsets may be zero or negative. Represent them as separately typed signed coordinate/offset values, with their own finite bounds and units, rather than weakening the positivity rule for lengths, diameters, radii, or counts. Any schema size/operation-count change must be made consistently in the parser, generated JSON Schema, provider response schema, validator, runtime projection, and backend preflight.

---

# 6. Durable managed-Part recovery

Persist an explicit association between the native Part, active configuration, committed `CadProgram` (where one exists), CADState, revision, and schema version. Saving, closing, and reopening in a fresh controller process must not rely on in-memory `RelationProgram`, feature handles, or original process IDs.

On reopen, verify document/configuration identity, path and file lineage, native persistent references, feature ownership, parameter bindings, relation/dependency graph, actual scalar readback, and applicable geometry. A changed configuration, Save As, external edit, missing sidecar, stale reference, or ambiguous recovery must produce a typed inspectable status and block mutation until reconciled. Do not silently match features by displayed names or nearest geometry.

Version the state format or add a versioned companion manifest. Existing v0.2 state must remain readable for supported migration; migration takes place on a working copy with an auditable result. Native Part and state are not assumed to commit atomically as two ordinary files: use a working copy/checkpoint plus an explicit durable revision manifest or recovery marker, and reconcile interrupted publish before accepting another edit. This is a **single-Part durability protocol**, not a general multi-file transaction system.

## 6.1 Durable package and reopen state machine

One managed revision consists of a native Part snapshot, its committed state, its construction program if managed, and a small manifest naming the exact file hashes, schema versions, document/configuration identity, and revision. Keep the previous complete revision until the next one is verified. Publish the manifest pointer only after the Part and state are flushed, reopened or independently readable, and cross-checked. If a crash occurs before pointer publication, the prior revision remains authoritative; if it occurs after publication, verify the new package before enabling edits. Preserve a recovery record if neither candidate can be proven valid. Atomic rename/replace may publish the small pointer; do not describe this as an atomic transaction across native CAD and JSON files.

The reopen lifecycle is `Closed → Opening → Inspectable → Editable` or `Quarantined`. `Editable` requires a valid manifest, matching native document/configuration identity, valid state/program hashes, successful persistent-reference resolution, and actual native readback. `Inspectable` allows safe diagnostics when edit preconditions fail. `Quarantined` forbids all mutation after inconsistent publish or failed rollback. Close/reopen and controller restart are separate test events. Save As and configuration switching are explicit identity transitions, not implicit continuation of the old revision; unsupported transitions reject with the previous files preserved.

The application must never claim a durable edit solely because `AtomicStateStore.Commit` succeeded. A file-save error, unsaved dirty native document, or mismatch between saved Part and committed state is a failed transaction. Recovery checks run before another plan or edit is dispatched.

---

# 7. External native Part intake and provenance

First inspection is read-only and must not write custom properties, rename features, rebuild without consent, or save the engineer's original file. Inspect the selected `.SLDPRT` and configuration, enumerate native Feature Tree and supported dependency/parameter evidence, and record bounded geometry observations. A missing native API fact stays unknown; it is not inferred from a display name.

Represent the result as a versioned **observed-model overlay** compatible with the existing state and Binder concepts. Each observed feature carries origin (`harness` or `external`), native kind/subtype, health, evidence, and one of `editable`, `read_only`, or `unsupported`. Unknown features remain visible/read-only and can retain reference/dependency edges without fake `OperationNode` definitions. If an unknown/read-only feature is a possible downstream dependent of an edit and its behavior cannot be checked, P0 rejects that edit; a generic “full rebuild succeeded” is not enough to prove its design intent. No unsupported feature is silently discarded.

External observations need stable semantic IDs local to the selected document/configuration, generated from verified native references and recorded provenance, not from feature-tree order alone. Importing must not claim a complete semantic model of arbitrary mechanical function. Original engineer files are preserved; edits operate on an explicit working copy until validated publication.

## 7.1 Observed-model contract

Keep a typed distinction between **managed** construction state and **external observed** state. Extend `CadState` through versioned types/adapters or a versioned companion record; do not force `FeatureNode.Kind` to invent a v0.2 `OperationKind` for an unrecognized native feature. At minimum an observation records: document/configuration fingerprint, native feature type and subtype, native persistent reference when available, parent/child evidence and its completeness, measured scalar parameters with units and native accessor provenance, relevant body/face/axis geometry, reference health, and edit-support reason. A missing reference or dependency is `unknown`, not an empty proven set.

The read-only import has two stages: inventory all feature-tree nodes within a finite bound, then qualify supported nodes through subtype-specific readers. Unsupported nodes remain in the inventory even when no safe persistent reference can be captured. Any limit overflow returns `OBSERVATION_LIMIT_EXCEEDED` with a partial **noneditable** report. The import must not silently truncate the tree and advertise its earlier nodes as safely editable.

Two observations of the same unchanged file/configuration should yield the same user-facing feature identity or an explicit remapping record. Display names and tree ordinal may help the UI, but cannot be sole binding evidence. The sidecar for an external Part belongs to the working copy; the original remains byte-identical through inspect, plan, failed edit, and successful edit-to-copy workflows.

Illustrative observation, deliberately not a reconstructed creation program:

```json
{
  "schemaVersion": "0.3",
  "origin": "external",
  "configuration": "Default",
  "features": [
    {"semanticId": "observed_boss_1", "nativeSubtype": "qualified_straight_extrude",
     "referenceHealth": "healthy", "dependencyCompleteness": "known",
     "editSupport": ["extrusion_depth"]},
    {"semanticId": "observed_feature_2", "nativeSubtype": "unrecognized",
     "referenceHealth": "unknown", "dependencyCompleteness": "unknown",
     "editSupport": []}
  ]
}
```

The actual schema must also carry verified native references, file fingerprint, measured values and units, and evidence. This shortened example illustrates that read-only inventory and editable qualification coexist without creating fictitious v0.2 operations.

---

# 8. External scalar edit contract

Initially consider these native feature/parameter pairs only:

```text
recognized extrusion → extrusion depth
recognized through-hole implementation → diameter
recognized linear/rectangular pattern → active instance count and spacing
```

“Recognized” means a specifically qualified SOLIDWORKS native subtype with an unambiguous owner, writable parameter accessor, stable persistent reference, understood downstream impact, and independent native readback. A generic cut-extrude circular hole and a Hole Wizard hole are distinct histories; qualification for one does not grant edit rights to the other. Editing must be offered only through the runtime's current capability projection. Unsupported feature histories remain inspectable and read-only.

| Candidate parameter | P0 qualified-history requirement | Readback and preservation oracle |
|---|---|---|
| Extrusion depth | One straight boss/base extrusion with an accessible native depth definition; blind/up-to-surface variants require separate qualification | Native definition depth, resulting axial extent, dependent feature health |
| Through-hole diameter | One explicitly qualified circular-cut or simple native-hole history; Hole Wizard is separate until proven | Native diameter or sketch circle, through-all condition, cylindrical wall geometry |
| Pattern count | One qualified linear pattern with a single active direction; two-direction rectangular history requires separate qualification | Native instance count and actual instance positions, seed unchanged |
| Pattern spacing | Same qualified active-direction history; inactive-direction spacing is not advertised | Native spacing and measured instance centers, dependent clearance |

These are **minimum candidate families**, not a promise that every similarly named SOLIDWORKS feature qualifies. Milestone 11 freezes the exact subtype matrix and API read/write contracts after a small feasibility study; Milestone 14 must either qualify each advertised row or mark the unsupported row and its unmet completion criterion. If the native feature is driven by an equation, external design table, linked dimension, configuration-specific override, or unknown relation that can overwrite the proposed value, its edit capability is withheld until that mechanism is explicitly supported.

Reuse the parameter Handler interface and transaction coordinator, but do not require an external Part to possess a synthetic full creation `CadProgram`. The editor may use a typed observed-feature edit descriptor and native accessor. Validate bounds, active configuration, external drift, impacted geometry/dependencies, rebuild, and final state before publishing. Ambiguous target or changed source file returns a typed refusal. A failure after mutation restores the working copy and the committed state; rollback failure invalidates the session and prevents further edits.

The exact target is selected by observed semantic ID plus document/configuration identity and revision. Before mutation, resolve its persistent reference to the expected native interface and subtype, read the old value, and compare it with the preview/state using the parameter-specific tolerance. After mutation, re-resolve and read the new native value and its affected geometry. A new value that is numerically accepted but drives a different feature or violates a dependent geometry contract is a failure. A changed display name alone is not a reason to retarget or reject when native identity remains verified.

---

# 9. EditSet and same-Part transaction foundation

An `EditSet` is a bounded list of typed target/parameter/new-value requests against **one selected Part and one configuration**, with an expected revision and no duplicate or conflicting target/parameter pair. The P0 typed entry must support prepare-all, capture one batch-start checkpoint, resolve aggregate ChangeSet/DirtySet, execute in dependency-safe order, rebuild/validate, and publish one new revision; any failure restores the batch-start state. Demonstrate this on at least two independent qualified targets. Do not implement this as repeated independent committed edits.

The P0 request envelope has `documentId`, `configurationId`, `expectedRevision`, `edits[]`, and an exact source/working-copy fingerprint. Each edit has a typed target semantic ID, parameter key, expected old value, and requested value. Enforce a finite batch bound (initial target: 2–16 edits), unique target/parameter pairs, legal units, supported native accessors, and no unresolved target. Preflight computes the **final proposed model** before the first native call; interdependent requests may be reordered only by a declared dependency graph. If the final state is legal but no safe intermediate native order exists, reject the batch rather than partially applying it. Native execution may rebuild between edits when required, but it commits state once.

The batch checkpoint includes native file bytes or a verified equivalent native snapshot, committed state/manifest, original feature/parameter values, and selected configuration. On failure, restore the batch-start working copy and state, reopen/read back key parameters and geometry, and leave the source file untouched. Merely reversing parameter calls is insufficient if native feature topology changed. The result reports aggregate `ChangeSet`, `DirtySet`, validation scope, partial native steps, rollback, and one final revision or none.

Illustrative typed request:

```json
{
  "expectedRevision": 4,
  "documentId": "<verified document identity>",
  "configurationId": "<verified configuration identity>",
  "edits": [
    {"target": "observed_boss_1", "parameter": "extrusion_depth", "oldValueMm": 12, "valueMm": 15},
    {"target": "observed_pattern_3", "parameter": "pattern_spacing", "oldValueMm": 25, "valueMm": 30}
  ]
}
```

The final protocol also includes the immutable file fingerprint; the example omits it for readability. All IDs and fields are strict, closed-schema values, not model-supplied native API identifiers.

P1 adds a public target query over observed type, dimensions, host, relation, and editable capability. The query produces a frozen target set and preview including old/new values, affected features, unsupported matches, and estimated validation scope. The user must confirm the exact set/revision before native mutation. Re-evaluate identity and revision at dispatch; if membership or native values drift, invalidate the preview. An empty or ambiguous query never degrades into “edit all.”

The checkpoint must protect original native files from partial edits. After a crash, recovery must identify whether the previous revision or the newly validated revision is authoritative. A test must inject failure at each consequential stage: preparation, first/last native edit, rebuild, postcondition, state publish, file publish, rollback, and reopen.

---

# 10. Generic sketch and spatial reference contract

Extend profile IR beyond centered rectangle/circle with a **bounded closed planar sketch** made from lines, arcs, circles, polylines, and slots. Support declared loops/inner loops and deterministic winding/containment checks. Reject open, self-intersecting, coincident-ambiguous, zero-area, or over-budget sketches before native mutation where decidable. Support essential geometric/dimensional constraints (coincident, horizontal/vertical, parallel/perpendicular, concentric, equal, distance, radius/diameter); classify under-, fully-, and over-constrained sketches. Critical degrees of freedom must be resolved or rejected before execution.

## 10.1 Finite sketch IR and solver boundary

Use named local 2D points/entities, explicit loops, and typed dimensions, rather than an arbitrary list of mouse-like drawing steps. Each entity has a stable local semantic ID. Arcs specify center/radius/endpoints/sweep or an equivalent unambiguous finite parameterization; a “bulge” without direction is invalid. Slots are a compact semantic profile that lowers to two parallel lines and tangent end arcs, with measured width/length/center. Inner loops subtract material only when the consuming feature permits them. Segment order, closure tolerance, loop winding, hole containment, and nonintersection are verified by pure geometry before native sketch creation. A profile that crosses a revolve axis must satisfy a separately stated revolve legality rule.

P0 is **not** an arbitrary symbolic sketch solver. The Planner may output already dimensioned geometry with explicit supported constraints. The deterministic layer verifies declared constraints and computes simple derived positions from them; under-constrained driving geometry, contradictory dimensions, or native solver disagreement is rejected. Do not auto-add hidden dimensions just to make SOLIDWORKS accept a sketch. Initial bounds: at most 64 primitive entities, 8 loops, and 64 constraints per sketch; revise only with an acceptance case and matching native/resource evidence. Coordinates/angles must be finite and unit-checked; lengths/radii positive, local coordinates signed.

A sketch plane can be the supported initial reference plane, a healthy planar face, or a verified datum plane. Placement uses an explicit local frame and typed orientation/offset relative to a bound reference, with units and sign convention. Native plane/face selection and sketch transform stay in the backend. Entity references must survive supported edits through persistent references and geometry/ownership checks; ambiguous topology change blocks dependent operations.

## 10.2 Frame and datum invariants

Define a right-handed local frame with origin, unit X/Y/Z axes, handedness and millimeter origin. The plane normal is local +Z; a signed offset is measured along that normal. A face-based frame needs a deterministic in-plane X direction from a healthy bound edge/axis or an explicit datum; face normal alone does not fix sketch rotation. Bind a plane/face/axis by semantic type, owning feature, native reference, and geometric orientation; reject if more than one candidate remains. Recompute the frame after a supported edit and confirm that dependent sketches retain orientation and intended offset. Mirrored or flipped frames are a failure, not an acceptable geometrically similar result.

Support world/origin principal planes, qualified datum planes, and planar faces of a managed solid. A curved face is not a sketch plane. Creating a new offset/reference plane may be implemented as one reusable operation or a typed placement field, but must have the same identity, preflight, native readback, and rollback guarantees as other operations. No camera/screen coordinates belong in the IR.

These are geometry primitives, not arbitrary expression or script execution. The Planner does not emit SOLIDWORKS API names, selection marks, raw COM identifiers, or free-form equations.

---

# 11. New native feature vocabulary

P0 adds reusable contracts and handlers for:

1. **Revolved boss/base:** a valid sketch profile, typed rotation axis, angle/depth mode, native solid result, and measured radial/axial postconditions.
2. **Extruded cut:** a bound planar sketch, direction and finite/through-all end condition, host body, and measured material-removal postconditions.
3. **Additive boss:** an additional extrusion on a supported plane/face with verified body merge or explicitly rejected multiple-body result.

| Operation contract | Typed inputs and required parameters | Required native postconditions |
|---|---|---|
| Revolved boss/base | Closed profile; bound axis in sketch plane; finite angle or full revolution; target body rule | Feature exists; one intended solid body; correct revolution angle, radial envelope, axial spans and volume |
| Extruded cut | Closed sketch; bound host body and plane/frame; through-all or positive finite depth; side/direction | Feature exists; one intended body remains; material removal, depth/end condition and affected faces/slots match |
| Additive boss | Closed sketch; bound host body and plane/frame; positive depth and merge intent | Feature exists; expected merge, one intended solid body, depth and affected geometry match |

For a cut or additive boss on an existing body, preflight proves its referenced body/host exists and that the operation is not silently targeting an untracked external body. A “rebuild succeeded” result without the expected body count and material-change evidence is insufficient. Native failure after creating a sketch but before the feature must roll back the sketch and any datum feature as part of the same construction transaction.

The exact IR spelling and shape should be finalized in Milestone 11 after a contract spike; semantics above are fixed. Step, annular groove, pocket, slot, and keyway are compositions of sketches and these operations, not production operations named for those part categories. Native preflight must reject unsupported thin features, multibody outcomes, invalid axis/plane references, and features with uncheckable topology.

Capture the driving dimensions of each new feature with a verified native read accessor. At least one meaningful driving dimension in each mandatory acceptance Part must support a subsequent qualified edit through the existing parameter Handler/transaction pattern; a feature tree that can only be recreated from scratch does not satisfy “editable model.”

For P0, qualify edits to one scalar from each relevant new operation family: a revolve-driven radial or axial dimension, an additive boss depth, and an extruded cut depth or through-all-compatible dimension. Multi-segment profile edits that change topology are not automatically supported. Every advertised edit pair needs old/new native readback, dependency impact, and rollback evidence. The runtime capability projection lists only actually qualified pairs for the current Part.

P1 may add counterbore/countersink and more fillet/chamfer target/parameter support. Each hole subtype must have its own finite native qualification; “change hole type” is not implied by diameter editing. No operation-kind addition may be accepted solely because one benchmark part needs it.

---

# 12. Relations, CADState, and semantic querying

Extend existing relation/dependency storage to record native parent-child, sketch-to-feature, host face/plane, axis, seed/pattern, and editable parameter links with provenance. Separate **observed native dependencies** from **declared design relations**: the former describe what SolidWorks currently reports; the latter are constraints Harness promises to maintain. Do not assert a design relation merely because two features happen to be geometrically aligned.

P1 exposes a bounded local structure graph: query a feature and return its immediate parents, children, host, pattern seed, associated parameters, reference health, and edit support. Query output is data for inspection and target selection, not proof of arbitrary mechanical function. Existing Binder rules remain type-, owner-, geometry-, and dependency-aware. Any new re-resolution strategy must return unique, unresolved, or ambiguous with auditable evidence.

---

# 13. Planner and intent contract

The primary Harness path still prefers **one bounded complete plan** before construction. It may use a bounded requirement-normalization step that separates `given`, `derived_by_rule`, `defaulted`, and `unresolved` values. Rules/defaults are versioned and visible; they cannot create a hidden engineering specification. Critical missing dimensions or fit assumptions trigger clarification/unsupported rather than execution.

The planning response has one of three outcomes: `planned` with strict typed program and requirement record; `needs_clarification` with a bounded list of critical questions and no executable program; or `unsupported` with a reason. A fourth provider failure/rejection remains an error, not a disguised clarification. If a user answers a clarification, generate a new plan/revision and rerun pure preflight; never splice new values into an old approved plan. A pure preflight can reject contradictory dimensions, unsupported feature/host kinds, ambiguous references, impossible closed profiles, and known overlap. Native feasibility remains subject to transactional execution and readback.

The Planner receives only the current executable capability subset, typed model/observation context, and necessary spatial/parameter constraints. Creation, single edit, external edit, and EditSet are distinct modes; no mode can smuggle an unqualified native target into another. Strict JSON/schema, operation and entity limits, safe identifiers, no executable code, pure preflight, and one-use execute approval remain. Avoid semantic repair or provider retry that changes the plan without explicit recording.

The ability to construct a new feature on an existing Part is allowed only when the underlying host/body and rollback path are supported. It is not a blanket promise to modify arbitrary external geometry.

**Anti-tool-expansion check:** count model-visible callable tools and schema alternatives before/after every milestone. The main construction path still exposes one structured plan response and one bounded execution approval, not a tool for each new feature or candidate part. Keep the provider prompt grounded in the executable `RuntimeCapabilityCatalog`; do not include read-only or unqualified native features as valid actions. A new feature handler is acceptable when it composes across at least two acceptance/held-out shapes or has a separately justified general geometric role. Reject a change that only improves a named fixture through hidden template knowledge.

---

# 14. Acceptance part families

These are **test tasks, not production presets**. All dimensions, layouts, histories, and exact held-out choices are frozen before benchmark execution. Images supplied with the project brief are illustrative of part categories, not geometric ground truth.

| Case | Priority | Minimum demonstrated capability |
|---|---|---|
| C1 — multistep transmission shaft | P0 | Revolve, multiple axial diameters/lengths, annular groove, editable dimensions |
| C2 — flanged sleeve with stepped bore | P0 | Coaxial inner/outer profiles, revolve or valid cut composition, measured bore/shoulder |
| C3 — L-shaped mounting bracket | P0 | Sketch/feature on another plane or face, additive boss, holes, fillet |
| C4 — multi-slot mounting plate | P0 | Slot/combined sketch, multiple independent cuts, positional edits |
| C5 — keyed flange hub | P1 | Revolve, keyway cut, circular pattern, dependent relation verification |
| C6 — grooved bearing support | P1 | Multiple sketch datums, local cuts, groove geometry and editable references |

For C1–C4, freeze at least one **primary design variable**, one **dependent relation**, and one **post-creation edit** before implementation of Milestone 20. Illustrative acceptance shape, without prescribing a production template:

| Case | Input anchors and independent oracle | Required follow-up edit |
|---|---|---|
| C1 | At least three axial diameter regions and their lengths, one groove; native axial/radial measurements and total length | Change one qualified diameter or axial span; unaffected regions and groove relation remain valid |
| C2 | Flange OD/thickness, sleeve OD/length, stepped bore diameters/depths; common-axis and wall-thickness checks | Change a qualified bore or flange dimension without losing coaxiality |
| C3 | Two perpendicular mounting faces, thickness/leg spans, hole size/placement and one edge treatment; native face normals and hole centers | Change one qualified boss/leg or hole dimension; perpendicularity and host relation survive |
| C4 | Plate envelope and at least three separately created slots/cuts; slot width/length/centers and body-volume checks | Change one slot-driving dimension or cut depth; other cuts stay unchanged |

The final numeric ranges and reference fixtures are set in Milestone 11 and frozen before any formal measurement. They must stay inside the advertised runtime capability; no after-the-fact simplification of a failed case. A case may use multiple valid feature histories; correctness is judged by geometry, editability, and declared relations, not by matching one oracle feature-tree spelling. A separate test proves the absence of named C1–C6 branches in production source.

Maintain a development-visible example set and a separate held-out set with independently recorded hashes. Held-out values and compositions are not embedded in production prompts, capability descriptions, or handlers. The formal runner reveals them only as user tasks at measurement time. If a held-out task exposes a genuine missing primitive, record the failure and implement the primitive in a later source version and a **new** formal run; do not retroactively relabel the original attempt.

P0 acceptance requires C1–C4. C5–C6 are separately reported P1 candidates; a failed C5/C6 does not silently disappear from the report, but does not block P0 completion. At least one P0 case and one external-model edit case must use held-out parameter/layout or modeling-history combinations not used for production tuning. Production source must not branch on C1–C6 names, images, fixture IDs, or known dimensions.

The generated model must be a native, editable Part with a sensible feature tree. Acceptance checks geometry, native dimensions, dependencies, reference health, subsequent edit behavior, and failure rollback—not a screenshot alone.

---

# 15. External-model acceptance matrix

Use native `.SLDPRT` fixtures with recorded SOLIDWORKS version, configuration, feature history, expected editable parameters, and checksums. For M14, user-manually-authored external Parts and existing Fixture Factory development fixtures are accepted with their actual provenance recorded; third-party authorship is not a completion gate. Factory self-checks do not substitute for Harness edit acceptance. At minimum, qualify depth, hole diameter, and pattern count/spacing on supported native subtypes. Include geometrically equivalent Parts built through different histories: where both histories are qualified, both must edit correctly; where only one is qualified, the other must remain inspectable and explicitly read-only. Geometry equivalence alone is not proof of feature-edit equivalence.

Fixture provenance must identify manual, Harness, Factory, third-party, or unknown creation workflow and prior diagnostic/tuning use. For M14, creation before the v0.3 intake implementation is not a completion gate. At least one fixture includes an unrelated unsupported feature, and one includes a potentially affected unsupported descendant. The former may remain editable only if dependency evidence proves it is unaffected; the latter is a mandatory refusal. Include one source with renamed/reordered features but preserved persistent identity, and one with a changed reference or configuration. Keep the source checksum before/after every trial.

**M14 source amendment (2026-10-10, explicit user authorization):** stop third-party model searches and accept the user model and existing fixtures under the provenance rules above. Sections 14 and 21.7 still require an untuned held-out external task combination, frozen before execution with no production changes during measurement. A fresh parameter combination on a disclosed, previously inspected model may satisfy that task-level condition; it does not make the model itself fresh or independently sourced. Historical failures/PARTIAL reports retain their original scope. This amendment does not waive equivalent-history, dependency, ambiguity, native geometry, transaction, recovery, cold-reopen or original-byte protections, and does not change M15-M20 requirements.

Required robustness cases include renamed features and reordered tree **when native identity remains valid**, and same-size features with distinct exact targets; these must not cause arbitrary retargeting or unnecessary refusal. Required negative cases include suppressed target, changed configuration, external file drift, deleted or changed persistent reference, unknown downstream feature, invalid new value, native rebuild failure, state publish failure, file publish failure, and interrupted reopen. Original engineer-authored files must remain byte-identical unless an explicit export/publish action selected them; normal editing targets a working copy.

**M14 ambiguity allocation amendment (2026-10-10, explicit user authorization: "明确批准修订验收分配"):** P0 selects exact semantic/native identities, not features by equal dimensions. M14 must prove independent exact binding of same-size targets through editing, rename/reorder and cold reopen, and retain pre-mutation rejection of unresolved/changed identities and unsafe native ownership, drivers and dependencies. Query/under-specified selection ambiguity and exact preview confirmation are allocated to M18; an intentionally confirmed multi-target set is distinct from ambiguous single-target intent. Actual nonunique native inventory, profile ownership or parameter accessor remains unsafe and must reject: no guard, qualification gate or geometry tolerance is relaxed. Without a reproducible applicable native ambiguity case, its native branch coverage remains **UNVERIFIED**, but constructing that case is no longer a separate M14 completion gate. This explicitly revises the necessity/allocation of the former Sections 15/16 ambiguous-binding trial, not its historical result: earlier PARTIAL reports and failures remain unchanged. The four-row unsupported/ambiguous negative alternative remains. M15's ambiguous spatial-reference refusal is unchanged.

---

# 16. Benchmark and correctness oracle

Separate **planning success**, **native construction/edit success**, **editable-model success**, **durable reopen success**, and **full task success**. A planner response accepted by schema is not a successful CAD task. Record all attempts, including unsupported/ambiguous/refused cases, and separate failures from successful-task timing statistics.

For each accepted case, use an independent oracle: native feature type and parameter readback, measured body geometry, volume/bounding dimensions when useful, hole/slot locations, relationship/dependency checks, and required edit/reopen behavior. External edit oracles compare source and working copy before/after, the exact target set, untouched features, state revision, and rollback restoration. A render/visual inspection may supplement but not replace numeric/native checks.

Planning correctness is measured with the **actual configured model**, not only deterministic fixtures. Freeze prompts, provider settings, capability snapshot, task statements, scoring oracle, and run order before the first formal request. Each C1–C4 case receives at least three independent plan attempts with the same task and no hidden repair; use different frozen tasks/parameters for held-out coverage. A rejected or `needs_clarification` plan is recorded as such, not as native success. Required native construction attempts follow the frozen schedule and preserve failures; do not rerun a failed slot to manufacture a higher rate. Three attempts per family give descriptive evidence only, not statistical proof of general reliability.

Release gate: deterministic typed fixture execution must pass C1–C4 and their required follow-up edits **4/4**. In the real-model formal schedule, each family must reach at least **2/3 complete-task successes**, for at least **8/12** total, with every failed attempt contained and its Part closed. A complete task includes correct plan, native build, independent geometry oracle, editable state, specified follow-up edit, and durable reopen where the case requires it. These thresholds are local release gates, not an estimated population success rate. Freeze the exact schedule and provider before any measured run; if the provider is unavailable, report the real-model gate unverified rather than replacing it with mock results.

At least two supported partial-dimension intents must be tested end to end: the accepted plan records the default/rule and the built geometry matches it. At least two missing-critical-input intents must stop without native mutation. Existing external Parts are scored separately from new construction; include at least one supported edit, one read-only refusal, and one same-Part batch. For M14, apply the exact-target robustness and native safety evidence allocation in Section 15; M18 separately requires an ambiguous query/selection refusal. An external read-only refusal is correct behavior, not an editing failure, when its unsupported status was declared before the test.

Record provider/model alias and settings, LLM calls and tokens, planning/runtime/validation/rebuild/recovery/rollback wall time, native rebuild count, resource use, Parts created/opened/closed, and cleanup. Use a frozen task set and production baseline before formal attempts. Do not compare performance with v0.2 or a stepwise agent unless the modes share equivalent task, operation vocabulary, backend, and oracle. v0.3 completion is a correctness/generalization claim, not an automatic efficiency claim.

---

# 17. Native safety, lifecycle, and test budget

Pure parsing, constraints, planning, Binder, and transaction tests run without activating SOLIDWORKS. Native tests use one owned document at a time, register ownership before mutation, restore the original active document, close/discard test documents, and never force-close an engineer's document. External fixtures are opened read-only for inspection; edits use disposable working copies. The v0.2 responsive-process and GDI resource guard remains, including abort at GDI ≥ 7000.

Each milestone has a finite budget of **new test Parts** and **native document open cycles**, counted separately. Reopening an existing Part does not create a new Part, but does consume an open-cycle slot. Record both. Do not delete a budget ledger or repeat a failed run to improve statistics. A budget increase requires a separate, explicit authorization and new evidence namespace. These are test execution rules, not a product limitation on normal user documents.

---

# 18. Implementation milestones

Each milestone lists goal, implementation boundary, verification, native budget, and completion signal. Start at Milestone 11; never silently implement a later milestone while claiming an earlier one.

## Milestone 11 — v0.3 contract and baseline freeze

**Goal:** fix the executable state/IR/observed-model extension contracts and preserve v0.2 behavior.

**Allowed:** schema/versioning design, provenance and capability descriptors, requirement/assumption record, migration reader, pure contract tests, baseline manifest, and a small COM feasibility probe if a native API assumption cannot be established otherwise. Choose the final names for revolve/cut/additive operations and external observed-feature descriptors here.

**Forbidden:** part-family presets, production external mutation, broad native regression, and claiming migration before a reopen test.

**Verification:** strict schema/unknown-feature/legacy-state/mode/capability rejection tests; source manifest. **Budget:** 0 new Parts, at most 1 native open cycle only if a documented contract probe is necessary. **Complete when:** versioned contracts and compatibility strategy have pure test evidence and no existing v0.2 executable capability is accidentally widened.

**Required outputs:** a versioned field/enum/limit table for program, observed state, requirement record, and EditSet; old-to-new state migration matrix; exact native subtype qualification candidates; native publish state diagram; frozen C1–C4 task definitions and independent oracles. State which v0.2 APIs remain wire-compatible. A contract-only operation is not yet advertised as executable in the runtime catalog.

## Milestone 12 — durable Harness Part reopen

**Goal:** save, close, reopen in a fresh controller, and continue a supported edit.

**Allowed:** durable program/state association, identity and persistent-reference revalidation, revision manifest/checkpoint, supported v0.2 state migration on a copy, and explicit stale/refusal states.

**Forbidden:** external-model import and Save As identity guessing.

**Verification:** G2-like managed Part cold reopen, relation-preserving edit, restart-independent state reconstruction, and injected interrupted-publish recovery. **Budget:** at most 2 new Parts and 5 native open cycles. **Complete when:** successful reopen/edit/readback plus negative identity/configuration/drift cases pass; no open test Part remains.

**Required sequence:** create and save an owned Part → close it → terminate the controller while keeping SOLIDWORKS lifecycle safe → start a fresh controller → select the saved Part and manifest → rebind references → edit one parameter → save and close → reopen again and read back the edited native value and relation. Include a failure at the Part-save/state-publish boundary and prove which revision remains authoritative. Reopening within the original `SolidWorksExecutionContext` does not satisfy this milestone.

## Milestone 13 — external Part read-only intake

**Goal:** inspect independently authored native Parts without changing them.

**Allowed:** bounded Feature Tree/parameter/dependency capture, observed-model overlay, native subtype provenance, read-only unknown features, and safe semantic IDs.

**Forbidden:** mutation, synthetic full `CadProgram` reconstruction, and writing Harness properties to the original.

**Verification:** at least two distinct native modeling histories, unknown feature, suppressed feature, configuration change, and checksum of originals. **Budget:** 0 new Parts, at most 5 external fixture open cycles. **Complete when:** supported observations are correct, unknowns remain read-only, and original bytes are unchanged.

**Required outputs:** inventory count versus native Feature Tree count; per-feature native subtype, reference health, editable/read-only reason, measured scalar provenance and dependency completeness; stable identity across unchanged reopen. A read-only fixture containing an unsupported descendant must remain visible without gaining edit permission. A fixture with more nodes than the bound must fail as partial/noneditable, not appear complete.

## Milestone 14 — external scalar edit and same-Part batch transaction

**Goal:** edit qualified parameters in an engineer-authored Part and execute an explicitly targeted two-or-more-edit batch as one native/state transaction.

**Allowed:** qualified depth/hole/pattern accessors, observed-feature edit descriptors, frozen source working copy, typed EditSet with explicit semantic IDs, aggregate checkpoint/ChangeSet, strict native readback, rollback and interrupted-publish recovery.

**Forbidden:** query-driven target discovery or UI preview, unqualified Hole Wizard equivalence, in-place original-file mutation, and multiple independent commits masquerading as one batch.

**Verification:** each qualified native subtype, an actual two-target native batch, independent full oracle, failure injection after the first batch edit and during file/state publication, and pure EditSet contract tests. **Budget:** 0 new Parts, at most 12 working-copy open cycles. **Complete when:** accepted single and batch edits persist across reopen as one revision; failed edits restore the batch-start source/working revision or invalidate safely; unsupported histories reject before mutation.

**Required matrix:** each of the four §8 parameter rows has an observed target, advertised capability, preflight bounds, native mutation, native parameter/geometry readback, cold reopen readback, and one negative unsupported/ambiguous variant. For the two-target batch, record both old/new values, one aggregate revision, one checkpoint, and an injected failure after the first native edit that restores **both** targets. Compare original source hashes before and after success and failure. If any row is not qualified, do not claim P0 completion.

Apply the explicitly approved M14 source and ambiguity allocation amendments in Section 15. A demonstrated unsupported variant satisfies the row-level negative alternative; same-size exact-target robustness is not reported as an ambiguous refusal. Report unverified native ambiguity branch coverage separately, without granting unsafe bindings edit permission or claiming all v0.3 milestones complete.

## Milestone 15 — generic sketch and spatial binding

**Goal:** create bounded composite planar sketches on qualified planes/faces.

**Allowed:** lines, arcs, circles, polylines, slots, loops, essential constraints, local frames, planar-face/datum selection, pure sketch legality checks, semantic/persistent reference capture.

**Forbidden:** arbitrary spline/surface design, free-form equations, unbounded sketch entities, and automatic topology guessing.

**Verification:** pure constraint and loop corpus; native sketches on initial and noninitial planes; one ambiguous reference refusal. **Budget:** at most 3 new Parts and 4 native open cycles. **Complete when:** sketch geometry/dimensions read back and remain bound through one supported edit.

**Required corpus:** valid outer+inner loop, arc/slot closure, signed local placement, rotated face frame, offset plane, reversed-normal rejection, open/self-intersecting/zero-area profile, inconsistent dimension, and unresolved driving degree of freedom. Record sketch solver status and transformed world geometry before claiming success. A native sketch with a correct silhouette but flipped frame or unconstrained driving position fails.

## Milestone 16 — revolve, additive boss, and extruded cut

**Goal:** execute the three generic feature contracts transactionally.

**Allowed:** handlers, native preflight, state capture, persistent references, material-add/remove checks, rollback checkpoints, and finite backend capability projection.

**Forbidden:** C1–C6 special cases and unverified multibody behavior.

**Verification:** independent minimal native examples for each operation and at least one multi-feature composition; postcondition failure rollback. **Budget:** at most 4 new Parts and 5 native open cycles. **Complete when:** native geometry and state agree, the combined program remains editable, and failure restores the pretransaction Part.

**Required native checks:** revolve axis/angle plus measured radial and axial spans; additive boss body merge and positive volume change; cut body retention and intended negative volume change; sketch/datum/feature persistent references after rebuild. Perform a qualified edit on one new driving dimension and verify dependent geometry. Force one late feature failure after a prior successful feature so rollback must remove the complete addition, including absorbed sketches/datums.

## Milestone 17 — Planner composition and partial-dimension intent

**Goal:** plan supported new parts and supported continuation of an existing Part without requiring every construction detail in user text.

**Allowed:** dynamic capability projection, bounded requirement/assumption normalization, versioned finite rules/defaults, strict new IR schema, pure preflight, and clarification/unsupported outcomes.

**Forbidden:** hidden critical design assumptions, one-off prompts keyed to C1–C6, unbounded agent loops, and auto execution after plan generation.

**Verification:** pure model/fixture cases with explicit dimensions, omitted noncritical dimensions, critical missing dimensions, and held-out parameter combinations; small native smoke. **Budget:** at most 2 new Parts and 3 native open cycles. **Complete when:** the Planner produces executable/validated plans for supported cases and refuses underspecified or unsupported requests before mutation.

**Required provider checks:** strict response envelope for `planned`, `needs_clarification`, and `unsupported`; actual current capability projection; no unavailable external feature in an executable target; one complete plan rather than a hidden feature-by-feature CAD loop. Test at least two given/derived/defaulted traces and two critical-unknown refusals. Record the exact model alias, token usage when returned, and whether the test used a live provider or a fixture; fixture success does not count as real-model planning accuracy.

## Milestone 18 — query-driven EditSet (P1 extension)

**Goal:** select the P0 EditSet through a bounded query and expose a frozen preview before its existing controlled execution.

**Allowed:** query filters over qualified observed features, exact target/old/new preview, one-use approval, and dispatch through the M14 transaction boundary.

**Forbidden:** ambiguous “all matching” fallback, cross-Part batches, and widening native edit support merely because a query selects a feature.

**Verification:** two independent qualified targets, one dependent target set, membership drift, mid-batch failure, rollback, and reopen. **Budget:** 0 new Parts and at most 5 working-copy open cycles. **Complete when:** all-or-nothing native/state behavior and preview-to-dispatch identity are proven.

**Required preview:** query expression, exact ordered target IDs, old/new parameter values and units, affected dependencies, unsupported/ambiguous matches, source fingerprint, configuration and revision. A changed target set, current value, or file fingerprint invalidates approval before native mutation. P1 adds selection and preview; it must call the already qualified M14 typed batch backend rather than implementing another mutation coordinator.

**Required ambiguity case:** a genuinely under-specified or ambiguous query/selection must refuse before mutation or require exact preview clarification/confirmation. Distinguish ambiguous single-target intent from an intentional exact multi-target set; multiple equal-size matches alone do not prohibit a confirmed batch. This is the query-selection acceptance allocated from Sections 15/16, not permission to weaken M14 native identity/ownership/accessor guards or M15 spatial binding.

## Milestone 19 — local relation queries and Playground extension (P1 extension)

**Goal:** show inspectable model structure and the qualified plan/preflight/edit workflow in the existing local UI.

**Allowed:** local relation graph DTOs, external Part inspection, editable-target display, Batch preview, new feature visualization, and existing confirmation/STA/session guards.

**Forbidden:** remote service, commercial plug-in UI, second Binder/solver, browser-supplied executable IR, and automatic native retry.

**Verification:** pure/mock API and UI tests, secret/session guards, no native activation in automated UI qualification. **Budget:** 0 new Parts and 0 native open cycles; optional native smoke belongs to a separately bounded acceptance run. **Complete when:** UI reflects backend truth and cannot execute stale or unsupported targets.

**Required view:** distinguish managed versus external Part, editable versus read-only versus ambiguous feature, committed versus provisional state, given versus defaulted dimension, plan versus preflight versus execute, and original file versus working copy. Preserve the existing loopback-only, credential-redaction, one-use approval, owned-document, and STA guarantees. UI labels do not upgrade a backend capability.

## Milestone 20 — frozen v0.3 core acceptance

**Goal:** decide whether the two P0 delivery outcomes have actually been achieved.

**Allowed:** frozen C1–C4 tasks, held-out compositions, external histories/edit fixtures, cold reopen, negative and recovery cases, independent oracles, and measured telemetry. Evaluate C5/C6 and P1 status separately if implemented; do not alter core pass/fail definitions after observing results.

**Forbidden:** benchmark-time handler/prompt tuning, replacing a failed attempt without recording it, screenshots as the sole oracle, and an efficiency/generalization claim beyond the tested scope.

**Verification:** at least 3 independently sampled planning/construction attempts per C1–C4 case, including frozen held-out inputs; at least 2 distinct native histories per advertised external edit family, with qualified and read-only outcomes distinguished. Include one targeted v0.2 G2 construction/edit native regression, cold reopen, and bounded negative/fault cases. Report attempt-level successes/failures and successful-only timing separately. **Budget:** at most 16 new Parts and 32 total native open cycles, including external working copies. **Complete when:** every P0 criterion in §21 passes with original fixtures preserved, no leaked test documents, and all failures classified. Otherwise report PARTIAL with exact unmet criteria.

**Required freeze and report:** hash production source/binaries, schema/capability snapshot, provider prompt/settings, fixture/task definitions, test schedule, and correctness oracles before first measured slot. Run every scheduled slot once, including failures. Report per C1–C4 planning, native, editable, follow-up-edit and reopen success and the §16 release gate; report each external subtype and batch separately. For every Part/working copy record create/open/close, active-document restoration, process/GDI guard, original checksum, state/native revision, rollback and cleanup. A post-freeze production fix requires a new named acceptance run; it cannot silently overwrite a failed run.

---

# 19. Failure behavior

Retain v0.2 stable failure families and add typed outcomes for unsupported external native subtype, observed-only target, ambiguous target set, source-file drift, configuration mismatch, migration failure, incomplete durable publish, invalid sketch, under/over-constrained sketch, unsupported body topology, and preview expiration. Stable codes are finalized in Milestone 11; do not expose raw COM exceptions as the only diagnostic.

Every mutation or rejected preview reports: source/working-copy identity, expected/current revision, target IDs, whether native mutation started, rebuild result, recovery/rollback attempted and succeeded, state/native publication status, and whether the session remains editable. Refusal before mutation must preserve original and committed state. Failure after mutation must either prove restoration or invalidate the session with a recoverable checkpoint; never claim success from a merely plausible shape.

---

# 20. Evidence policy and execution contract

Milestones 11–19 keep concise verification documents and machine-readable results only where they clarify an acceptance fact. Milestone 20 retains complete attempt-level raw outcomes, task/source hashes, provider configuration without secrets, native measurements, timing, lifecycle, and final criteria. Preserve historical v0.2 evidence; do not rerun its full native suite without a concrete regression risk or a required gate.

Each milestone instruction to an implementation agent should identify the PRD version, exact milestone, allowed files and behavior, native Part/open-cycle budget, and completion checks. At the end report COMPLETE/PARTIAL/BLOCKED, files changed, tests run, Parts created/opened/closed, original fixture checksums, limitations, and later milestones not implemented. A pure milestone must not launch SOLIDWORKS merely to produce activity.

---

# 21. Completion criteria

SWCAD-Harness v0.3 **core** is complete only when all of the following are evidenced:

1. A Harness-created saved `.SLDPRT` reopens in a fresh controller and accepts a relation-preserving edit with correct native readback.
2. At least two independently authored external modeling histories are inspected without modifying originals; unsupported/unknown features remain visible and read-only.
3. Extrusion depth, one qualified through-hole diameter history, and active linear-pattern count and spacing each have a qualified native subtype, positive edit/reopen case, and ambiguity/unsupported negative case.
4. External edits protect the original, validate the working copy and CADState, and restore or invalidate safely after injected failures; an explicitly targeted two-or-more-edit batch commits one revision or none.
5. The new sketch/datum/revolve/additive/cut contracts build composable native Parts without production code keyed to an acceptance part.
6. C1–C4 each pass deterministic native geometry, parameter readback, editable feature-tree/state, and required follow-up edit checks; the frozen real-model schedule meets the §16 success thresholds.
7. At least one held-out new-part composition and one held-out external-model history/parameter combination pass without benchmark-time production changes.
8. Supported partial-dimension intents disclose given/derived/defaulted values; a critical missing constraint produces clarification/unsupported before native mutation.
9. Planner capability/schema/pure preflight, Binder, dependency validation, transaction, rollback, and resource/lifecycle safeguards remain effective for both managed and external paths.
10. Formal attempt-level evidence separates planning, native, editable, reopen, and complete-task success, and makes no unsupported efficiency or universality claim.

P1 status is reported separately: `EditSet query/preview`, `relationship query`, `Playground extension`, `C5`, `C6`, and optional hole/edge expansions. A P1 capability is never labeled complete without its own acceptance evidence.

Final status is either:

```text
SWCAD-HARNESS v0.3 CORE COMPLETE
```

or:

```text
SWCAD-HARNESS v0.3 PARTIAL — <exact unmet criteria>
```

---

# 22. Expected final result

```text
Supported natural-language intent OR selected native Part
↓
explicit requirements / assumptions OR read-only native observation
↓
bounded executable capabilities and semantic references
↓
strict typed CAD construction or qualified edit request
↓
existing transaction + reusable SOLIDWORKS backend
↓
native readback / affected validation / rollback if necessary
↓
durable, reopenable Part and consistent CADState
```

This release advances **single-Part** generality. It does not claim arbitrary CAD comprehension, autonomous engineering design, or assembly automation.
