# Generalized CAD Harness v0.2 — Clean Implementation PRD
## Finite Generalized Parametric CAD Runtime for SOLIDWORKS

> Version: v0.2-clean-draft-1  
> Project type: **new folder / new project / clean implementation**  
> Target CAD: SOLIDWORKS  
> Primary language: C#  
> Runtime style: out-of-process SOLIDWORKS controller  
> Input: text first  
> Reference implementation: public GitHub repository `GiaoGen/CAD-Harness`  
> Critical rule: the v0.1 repository is **reference material only**, not a dependency and not a regression target.

---

# 0. Why this is a new project

v0.1 proved useful architectural ideas, but its implementation is heavily specialized around one canonical model and contains a large historical test/evidence system.

v0.2 changes the architecture substantially:

```text
v0.1
hard-coded canonical intent
→ canonical runtime
→ canonical CADState
→ canonical validator

v0.2
typed CAD Operation IR
→ semantic entity binding
→ design relations
→ generic CADState
→ SOLIDWORKS backend
→ incremental validation
```

Therefore v0.2 MUST be built as a clean project.

The existing GitHub repository may be inspected for proven implementation techniques, but the new project must not inherit the old repository's runtime/test lifecycle by default.

The local modified v0.1 workspace is irrelevant to v0.2.

Do not copy the local workspace.

If a v0.1 implementation detail is needed, consult the clean GitHub repository.

---

# 1. One-sentence definition

Build a finite generalized CAD Harness that converts natural-language mechanical intent into a typed CAD operation program, binds operation inputs to semantic entities in the current CAD model, compiles those operations into native SOLIDWORKS API calls, preserves explicit design relations across edits, validates only affected state when safe, and uses AI only where deterministic logic cannot resolve bounded ambiguity.

---

# 2. Core hypothesis

The v0.2 hypothesis is:

> A finite generalized CAD runtime can support multiple previously unpreset parametric designs using reusable CAD operations, semantic entity binding and explicit design relations, while requiring fewer frontier-LLM decisions, less repeated CAD-state reconstruction and fewer tokens than a conventional step-by-step LLM tool-calling CAD agent.

This hypothesis must be measured.

Do not claim it is already proven.

---

# 3. Non-goals

v0.2 is NOT:

- an arbitrary natural-language-to-CAD system;
- a general mechanical designer;
- a GUI-driving SOLIDWORKS agent;
- a wrapper that exposes every SOLIDWORKS API as an LLM tool;
- a system that lets an LLM generate and execute arbitrary C# or VBA;
- a migration of the v0.1 codebase;
- a compatibility project for historical Phase 1–8 tests;
- a reproduction of v0.1 benchmark infrastructure.

---

# 4. Relationship to v0.1

## 4.1 v0.1 is reference material

The clean GitHub repository may be consulted for:

```text
SOLIDWORKS COM connection
direct native API usage
mm → meter conversion
persistent-reference API usage
document identity strategy
atomic file replacement pattern
rollback transaction ideas
batch update ideas
native feature creation examples
known SOLIDWORKS API quirks
```

## 4.2 v0.1 code is NOT automatically reusable

Before copying any class or function, Codex must answer:

```text
1. Is this behavior still required by v0.2?
2. Is the code generic, or canonical-specific?
3. Does copying it introduce old test/evidence dependencies?
4. Can a smaller clean implementation provide the same behavior?
```

If the answer is unclear, reimplement the minimal required behavior.

## 4.3 Explicitly forbidden inheritance

Do NOT copy into the new project:

```text
CanonicalFeatureState
CadIntent.Canonical()
CanonicalExecutor
CanonicalEntityResolver
canonical-only StateCapture
canonical-only CadValidator
Phase 6 live test harness
Phase 7 performance comparison suite
Phase 8 benchmark runner
historical artifact/evidence directories
historical test-document lifecycle behavior
fixed 24-reference manifest assumptions
fixed four-parameter planner schema
```

Do not import the old test folder wholesale.

---

# 5. Architectural boundary

The main architecture is:

```text
User
↓
Intent Planner
↓
Typed CAD Program / Operation IR
↓
Operation Registry / Type Check
↓
Semantic Entity Binder
↓
Design Relation Engine
↓
SOLIDWORKS Backend
↓
Native SOLIDWORKS API
↓
ChangeSet
↓
Incremental Validator
↓
Recovery / Rollback
↓
Atomic CADState Commit
```

The LLM decides:

> What CAD operations represent the user's intent?

The Harness decides:

> What objects those operations refer to, whether the operation is legal, how it is executed, whether it succeeded, and how state should be updated.

SOLIDWORKS decides:

> Native CAD execution and native geometry/feature behavior.

---

# 6. Critical architectural rule

## Do not mirror SOLIDWORKS APIs as LLM tools

Bad:

```text
FeatureExtrusion3
FeatureCut4
Select4
FeatureLinearPattern5
FeatureFillet3
```

exposed directly as planner tools.

Good:

```text
CreateExtrude
CreateThroughHole
CreateLinearPattern
CreateRectangularPattern
ApplyFillet
ApplyChamfer
EditParameter
```

The high-level operation represents CAD meaning.

The SOLIDWORKS backend translates that operation into the correct native API sequence.

---

# 7. CAD Operation IR

## 7.1 Core types

Implement a typed program representation such as:

```text
CadProgram
OperationNode
OperationKind
OperationContract
OperationInput
OperationParameter
DesignRelation
```

Example:

```json
{
  "programVersion": "0.2",
  "operations": [
    {
      "id": "base",
      "kind": "create_extrude",
      "profile": {
        "kind": "centered_rectangle",
        "widthMm": 100,
        "heightMm": 60
      },
      "depthMm": 8,
      "semanticId": "base_plate"
    },
    {
      "id": "hole",
      "kind": "create_through_hole",
      "host": "base_plate.top_face",
      "diameterMm": 6,
      "semanticId": "mounting_hole_seed"
    },
    {
      "id": "pattern",
      "kind": "create_rectangular_pattern",
      "seed": "mounting_hole_seed",
      "countX": 2,
      "countY": 2,
      "semanticId": "mounting_holes"
    }
  ],
  "relations": [
    {
      "kind": "centered_about",
      "subject": "mounting_holes",
      "reference": "base_plate.local_frame"
    }
  ]
}
```

## 7.2 Planner output must not contain

```text
COM interfaces
SOLIDWORKS API method names
selection marks
feature display names
screen coordinates
C# code
VBA code
arbitrary executable expressions
```

---

# 8. Initial supported operation vocabulary

P0:

```text
centered_rectangle_profile
circle_profile

create_extrude
create_through_hole
create_blind_hole

create_linear_pattern
create_rectangular_pattern
create_circular_pattern

apply_fillet
apply_chamfer

edit_parameter
```

Assemblies/mates are NOT required for P0.

However, the type system must not prevent a future operation such as:

```text
create_concentric_relation
```

with typed semantic inputs such as:

```text
RotationalReference A
RotationalReference B
```

---

# 9. Operation Contract

Every operation must define:

```text
inputs
parameters
preconditions
effects
postconditions
```

Example:

```text
CreateThroughHole

input:
    host: PlanarHostSurface

parameters:
    diameter: Length > 0
    placement: Point2D | PositionRelation
    depthMode: ThroughAll | Blind

effects:
    creates ThroughHoleFeature
    modifies Body
    creates cylindrical wall semantic entities

postconditions:
    native feature exists
    rebuild succeeds
    measured diameter matches
    end condition matches
```

This is a Harness type system.

It must NOT be a copied version of SOLIDWORKS API documentation.

---

# 10. Semantic Type System

Initial semantic entity types:

```text
FeatureRef
BodyRef
SketchProfile
PlanarFace
CylindricalFace
LinearEdge
CircularEdge
ReferenceAxis
ReferencePlane
PointRef
LocalFrame
```

Initial semantic roles:

```text
HostSurface
PatternSeed
PatternDirection
FilletEdgeSet
ChamferEdgeSet
RotationalReference
PlacementReference
```

Native SOLIDWORKS objects may satisfy semantic roles.

Example:

```text
IFace2 + cylindrical surface
→ CylindricalFace
→ can satisfy RotationalReference
```

Native legality remains the backend's responsibility.

---

# 11. Semantic Entity Binder

Binding pipeline:

```text
Operation input slot
↓
CADState candidate query
↓
semantic-type filter
↓
geometry / ownership / dependency filter
↓
0 candidates → BINDING_UNRESOLVED
1 candidate  → bind directly
N candidates → bounded ranking
```

Do not call an LLM when deterministic filtering produces one candidate.

Do not silently choose the first candidate when multiple plausible candidates remain.

Return:

```text
BINDING_AMBIGUOUS
```

when semantic intent cannot be justified.

---

# 12. Bounded Judge / Jev

The system should define:

```text
IBoundedJudge
```

The project must work without any bounded judge.

Appropriate uses:

```text
rank a small legal candidate set
choose among a finite recovery policy set
classify a bounded ambiguity
select an allowed validation profile above the deterministic minimum
```

Inappropriate uses:

```text
read hole diameter
check rebuild error
count bodies
resolve whether a persistent reference exists
```

Those are deterministic/native facts.

Jev may be added later as one adapter.

---

# 13. CADState v0.2

Use a new clean schema.

Conceptual structure:

```json
{
  "schemaVersion": "0.2",
  "document": {},
  "features": [],
  "entities": [],
  "parameters": [],
  "bindings": [],
  "relations": [],
  "dependencies": [],
  "revision": 0
}
```

Mandatory concepts:

```text
document identity
configuration identity
semantic IDs
feature ownership
native persistent references
parameter bindings
design relations
dependency edges
edit revision
reference health
```

Do not implement v0.1 state migration in the clean project.

v0.1 state compatibility is out of scope.

---

# 14. Design Relations

Initial relation vocabulary:

```text
hosted_on
pattern_seed
centered_about
symmetric_about_axis
equal_spacing
through_all
aligned_with
depends_on
```

Example:

```text
2-hole centered linear pattern:

seed offset =
-(count - 1) × spacing / 2
```

This relation must be represented through a typed relation implementation.

Do not encode:

```csharp
if (HoleSpacing)
    seedX = -spacing / 2;
```

in the generic transaction path.

---

# 15. SOLIDWORKS Backend

The backend owns:

```text
COM interfaces
selection mechanics
selection marks
feature-data objects
SOLIDWORKS enums
native API sequencing
dimension access
rebuild boundaries
temporary preference management
native cleanup
```

Conceptual handler:

```csharp
IOperationBackendHandler
{
    OperationKind Kind { get; }

    PreflightResult Preflight(...);

    OperationExecutionResult Execute(...);
}
```

The planner must not know these details.

---

# 16. Transaction model

Generic mutation flow:

```text
load current state
↓
resolve operation inputs
↓
preflight
↓
capture rollback data
↓
execute
↓
rebuild
↓
validate required postconditions
↓
recover if allowed
↓
validate final state
↓
atomic state commit
```

On failure:

```text
rollback
↓
rebuild
↓
validate restored state
↓
do not commit new state
```

---

# 17. ChangeSet / DirtySet

Every mutation returns a `ChangeSet`.

Example:

```json
{
  "changedFeatures": ["base_plate"],
  "changedParameters": ["base_plate.thickness"],
  "possiblyInvalidatedEntities": [
    "base_plate.top_face",
    "mounting_holes.*.wall_face"
  ]
}
```

The dependency graph expands this into a `DirtySet`.

Only dirty semantics need targeted validation unless the operation requires full validation.

---

# 18. Incremental validation

Validation levels:

## Level 0 — always

```text
native API result
feature error status
rebuild status
transaction integrity
```

## Level 1 — changed parameters

Read only directly changed parameter values.

## Level 2 — dirty semantic state

Read affected semantic entities and relations.

## Level 3 — full managed-model validation

Only for:

```text
new model finalization
high-risk topology edits
recovery
reference re-resolution
state drift suspicion
explicit full validate
benchmark fault injection
```

The point is to avoid repeatedly scanning the full CAD model after trivial edits.

---

# 19. Generic semantic re-resolution

Use resolver strategies such as:

```text
PlanarFaceResolver
CylindricalFaceResolver
LinearEdgeResolver
CircularEdgeResolver
```

Candidates may use:

```text
semantic type
owner
local frame
expected geometry
adjacency
native feature provenance
relation context
```

Resolver behavior:

```text
0 → unresolved
1 → recovered
N → ambiguous
```

No universal topology reasoning system is required.

---

# 20. Planner

The planner outputs one bounded `CadProgram`.

It must not iteratively call the CAD runtime in the main Harness path.

Planner output must be:

```text
strictly typed
schema validated
enum validated
numeric validated
bounded operation count
free of API names
free of executable code
```

Suggested limit:

```text
<= 12 operations per plan
```

Unsupported intent must be rejected before mutation.

---

# 21. Supported model families

P0 should support finite composition sufficient for:

```text
rectangular plates
circular plates / disks
through holes
blind holes
linear hole patterns
rectangular hole patterns
circular hole patterns
fillets
chamfers
same-model parameter edits
```

Editable parameters should include:

```text
profile width
profile height
profile diameter
extrusion depth
hole diameter
blind-hole depth
pattern spacing
pattern count
fillet radius
chamfer distance
```

---

# 22. Generalization test families

These are tests, NOT presets.

Production code must not contain dedicated classes/functions for them.

## G1 — two-hole plate

```text
80 × 50 × 10
2 × Ø8 through holes
40 mm centered spacing
R3 outer fillets
```

## G2 — four-hole plate

```text
100 × 60 × 8
4 × Ø6 through holes
2 × 2 centered rectangular layout
```

Then edit:

```text
thickness → 10
hole diameter → 8
```

## G3 — unseen rectangular composition

```text
120 × 80 × 10
6 × Ø8 through holes
2 × 3 centered rectangular pattern
R5
```

## G4 — circular family

```text
Ø100 × 12 disk
Ø20 center bore
6 × Ø8 bolt holes
Ø70 bolt circle
```

## G5 — chamfer family

```text
90 × 70 × 6 plate
4 centered mounting holes
2 mm edge chamfer
```

## G6 — negative cases

```text
impossible fillet
invalid pattern
unsupported placement
```

Expected:

```text
structured failure
rollback
state unchanged
```

## G7 — unsupported

```text
optimized turbine blade with internal cooling passages
```

Expected:

```text
unsupported
no runtime mutation
```

---

# 23. Held-out generalization test

After generic handlers are complete, define at least one new composition that:

```text
uses only existing operation kinds
uses unseen parameter values / composition
has no dedicated preset
requires zero production-code changes
```

The Harness must:

```text
plan
bind
execute
validate
persist
edit later
```

---

# 24. Step-by-step LLM baseline

A fair baseline is required only near the end of v0.2.

Harness mode:

```text
User
→ one high-level CadProgram
→ deterministic execution
→ bounded escalation when needed
```

Baseline:

```text
User
→ LLM selects one CAD operation
→ execute
→ observe compact state
→ LLM selects next operation
→ ...
```

Both must share:

```text
same model
same CAD operation vocabulary
same SOLIDWORKS backend handlers
same final correctness validator
same user task
```

This compares orchestration architectures, not backend quality.

---

# 25. Expected efficiency hypothesis

For a task with N CAD operations:

```text
step-by-step agent:
O(N + re-observation + recovery) model decisions

Harness:
O(1 + unresolved semantic ambiguity) frontier-model decisions
```

Expected advantage:

```text
fewer frontier-model calls
lower token usage
less context reconstruction
fewer repeated CAD-state interpretations
more stable semantic continuity
```

Engineering targets, NOT guaranteed results:

```text
median frontier-model calls
<= 50% of stepwise baseline

median token usage
>= 40% lower

median frontier-LLM wall
>= 40% lower

single-parameter validation wall
>= 50% lower than full-state validation
```

End-to-end speedup must be measured separately because SOLIDWORKS COM operations may dominate runtime.

---

# 26. Clean-project testing rules

This section is mandatory and overrides any generic instruction such as “run all regressions”.

## 26.1 General rule

Every milestone uses the smallest possible verification scope.

Codex MUST NOT expand verification beyond the current milestone without explicit user instruction.

## 26.2 No historical regression

The new project has no obligation to run:

```text
v0.1 Phase 6 live tests
v0.1 Phase 7 benchmark
v0.1 Phase 8 benchmark
old artifact generation
old performance comparison
```

## 26.3 SOLIDWORKS document lifecycle

Every native test-created Part must:

```text
be registered as test-owned
be closed after the test
discard unsaved changes unless preservation is explicitly requested
restore the original active document
```

Evidence belongs on disk, not in dozens of open SOLIDWORKS documents.

## 26.4 Resource guard

Before creating a native test Part:

```text
check open test-owned document count
check SOLIDWORKS process responsiveness
check GDI count if available
```

Default safety behavior:

```text
if GDI >= 7000:
    abort native test
    return TEST_RESOURCE_LIMIT
```

Do not create more documents after the threshold.

## 26.5 Native-test budget

A milestone may not exceed its listed native Part budget automatically.

If additional live validation seems useful, STOP and report:

```text
ADDITIONAL_NATIVE_VALIDATION_RECOMMENDED
```

Do not run it without explicit user approval.

## 26.6 No open-ended native loops

No milestone may run:

```text
unbounded repetitions
large benchmark iterations
historical benchmark sweeps
performance comparisons
```

unless that milestone explicitly requires them.

---

# 27. Implementation milestones

The following phases are deliberately small.

Each phase has:

```text
goal
allowed implementation
forbidden scope
test budget
completion signal
```

## Milestone 0 — Project bootstrap

### Goal

Create the new clean project.

Implement only:

```text
solution/project structure
build script
basic test runner
configuration
SOLIDWORKS interop references
README
```

Reference the old GitHub repository only for environment/build knowledge.

### SOLIDWORKS execution

```text
0 native Parts
```

Optional:

```text
one attach/inspect command
```

but no geometry creation.

### Forbidden

```text
CAD Operation IR implementation
state system
planner
benchmark
geometry creation
```

### Completion

```text
MILESTONE 0 COMPLETE
```

when clean build + pure unit runner work.

## Milestone 1 — CAD Operation IR and Type System

### Goal

Implement only:

```text
CadProgram
OperationNode
OperationKind
OperationContract
OperationRegistry
semantic input types
strict JSON/schema parser
validation of typed programs
```

### Required unit tests

Test:

```text
valid program parses
unknown operation rejects
missing input rejects
wrong semantic type rejects
invalid numeric value rejects
API/COM names cannot appear in planner-facing fields
round-trip serialization
registry lookup
```

### SOLIDWORKS execution

```text
0 native Parts
```

This milestone MUST NOT open or create SOLIDWORKS Part documents.

### Forbidden

```text
SOLIDWORKS backend
CADState
PersistentReferences
Jev
planner HTTP
benchmark
canonical-model reproduction
```

### Completion

```text
MILESTONE 1 COMPLETE
```

when all pure tests pass.

## Milestone 2 — Minimal SOLIDWORKS Backend Vertical Slice

### Goal

Compile one IR operation into one native feature path.

Implement:

```text
SOLIDWORKS connection
CreateExtrude backend handler
centered rectangle profile backend
minimal execution context
```

Test task:

```text
80 × 50 rectangle
extrude 10 mm
```

No holes.
No patterns.
No fillets.

### Native Part budget

```text
maximum 2 Parts
```

Expected normal use:

```text
1 Part
```

### Required verification

Check only:

```text
1 solid body
width
height
depth
rebuild success
```

Close test Part after verification.

### Forbidden

```text
full CADState
generic binder
persistent references
full validator
holes
patterns
Jev
LLM
benchmarks
```

## Milestone 3 — Generic CADState + Persistent Identity

### Goal

Persist and restore the single extrude created in M2.

Implement:

```text
DocumentIdentity
FeatureNode
SemanticEntityNode
ParameterBinding
PersistentReference adapter
atomic StateStore
```

No v0.1 state migration.

### Native Part budget

```text
maximum 2 Parts
```

### Required verification

```text
create one plate
capture state
controller process restart allowed
restore managed feature
read parameter
close test Part
```

### Forbidden

```text
holes/patterns
relations
Jev
full benchmark
old v0.1 regression
```

## Milestone 4 — Composable Feature Backend

### Goal

Add reusable feature handlers:

```text
ThroughHole
BlindHole
LinearPattern
RectangularPattern
Fillet
Chamfer
CircularPattern if feasible in this milestone
```

Do not build a named part preset.

### Native Part budget

```text
maximum 4 Parts
```

### Required tasks

Use only minimal compositions needed to prove handlers.

No performance benchmark.

### Completion

At least:

```text
G1 create
G2 create
```

must be possible through generic operations.

## Milestone 5 — Design Relations + Binder

### Goal

Implement:

```text
Semantic Entity Binder
centered_about
symmetric_about_axis
pattern_seed
hosted_on
equal_spacing
dependency graph
```

Move coupled parameter behavior into relation handlers.

### Native Part budget

```text
maximum 3 Parts
```

### Required verification

```text
2-hole centered edit
2×2 centered edit
2×3 centered edit
```

Each test closes its Part.

### Forbidden

```text
Jev
large benchmark
stepwise baseline
```

## Milestone 6 — Generic Transaction + Incremental Validation

### Goal

Implement:

```text
ChangeSet
DirtySet
generic mutation transaction
targeted validation
full-validation escalation rules
rollback
```

### Native Part budget

```text
maximum 3 Parts
```

### Required verification

```text
successful parameter edit
failed/impossible edit
rollback
dirty-only validation
```

No historical benchmark.

## Milestone 7 — Planner

### Goal

Implement:

```text
natural language
→ strict CadProgram
```

Start with deterministic fixtures and/or mocked structured model responses.

Then add one configurable frontier LLM planner.

### Native Part budget

Default:

```text
0 Parts
```

Optional final smoke:

```text
maximum 1 Part
```

### Forbidden

```text
iterative tool agent
Jev
large live benchmark
```

## Milestone 8 — Bounded Judge / Jev

### Goal

Implement:

```text
IBoundedJudge
```

and optionally a Jev adapter.

Test controlled ambiguous candidate selection.

### Native Part budget

Default:

```text
0 Parts
```

Optional smoke:

```text
maximum 1 Part
```

The system must still work without Jev.

## Milestone 9 — Generalization Evaluation

### Goal

Run:

```text
G1
G2
G3
G4
G5
G6
G7
held-out composition
```

This is the FIRST milestone allowed to run a broader native suite.

### Native lifecycle

Each case:

```text
create isolated Part
execute
write evidence
close/discard Part
restore original document
```

Maximum concurrently open test-owned Parts:

```text
1
```

No case may depend on a previous case's open document.

## Milestone 10 — Stepwise Agent Baseline + Comparative Benchmark

### Goal

Implement the fair iterative baseline and compare it with Harness mode.

This is the FIRST milestone allowed to run repeated performance measurements.

### Required comparison

Measure:

```text
success
editable-model success
LLM calls
input tokens
output tokens
LLM wall
runtime wall
validation wall
rebuilds
recovery
total wall
```

### Statistical minimum

```text
1 warm-up
5 measured runs per task/mode
```

Each run must close its Part.

GDI/resource guard is mandatory.

---

# 28. Codex execution contract

Every instruction to Codex should include:

```text
Read this PRD.

Execute ONLY Milestone X.

Do not implement later milestones.
Do not run tests from the old CAD-Harness repository.
Do not expand the native-test budget.
Do not run benchmarks unless this milestone explicitly requires one.

If the milestone can be completed with pure tests, do not launch SOLIDWORKS.

If additional work outside scope appears necessary:
STOP and report BLOCKED with the reason.
Do not autonomously broaden scope.

At the end return:
- COMPLETE or BLOCKED
- files changed
- tests run
- native Parts created
- native Parts closed
- known limitations
- next milestone NOT implemented
```

---

# 29. Failure behavior

Stable failure families should include:

```text
PROGRAM_SCHEMA_INVALID
OPERATION_UNSUPPORTED
OPERATION_PRECONDITION_FAILED
BINDING_UNRESOLVED
BINDING_AMBIGUOUS
RELATION_VIOLATED
PARAMETER_NOT_APPLIED
FEATURE_REBUILD_FAILED
GEOMETRY_INVALID
GEOMETRY_IMPOSSIBLE
STALE_REFERENCE
ROLLBACK_FAILED
STATE_COMMIT_FAILED
TEST_RESOURCE_LIMIT
```

A mutation failure must record:

```text
mutation started?
rollback attempted?
rollback succeeded?
state committed?
```

---

# 30. Evidence policy

Keep evidence minimal during development.

For Milestones 0–8:

```text
one concise verification markdown
small JSON results only when useful
no giant historical evidence archive
```

Do not save hundreds of duplicate run files.

Do not preserve open SOLIDWORKS Parts as evidence.

Full raw evidence is required only for:

```text
Milestone 9 generalization evaluation
Milestone 10 comparative benchmark
```

---

# 31. Completion criteria

v0.2 is complete when:

1. one Operation IR supports multiple unpreset compositions;
2. G1–G5 execute without dedicated part presets;
3. a held-out composition executes without production-code modification;
4. same-model edits preserve explicit relations;
5. semantic binding works on supported entity types;
6. persistent identity survives supported edits;
7. incremental validation correctly detects injected failures;
8. failed mutations rollback safely;
9. bounded judge is optional;
10. a fair stepwise baseline exists;
11. comparative metrics are recorded;
12. no efficiency claim is made unless benchmark evidence supports it.

Final status:

```text
GENERALIZED CAD HARNESS v0.2 COMPLETE
```

or:

```text
GENERALIZED CAD HARNESS v0.2 PARTIAL
```

with exact unmet criteria.

---

# 32. Expected final result

If successful:

```text
Natural language
↓
one high-level typed CAD program
↓
deterministic semantic binding
↓
reusable CAD operation handlers
↓
SOLIDWORKS native execution
↓
incremental validation
↓
persistent semantic state
```

New supported designs should emerge from:

```text
recombining operations + relations
```

rather than:

```text
adding a new preset / hard-coded workflow
```

The expected efficiency advantage over an ordinary step-by-step LLM CAD agent is:

```text
fewer frontier-model calls
lower token usage
less repeated state observation
less repeated context reconstruction
more stable design-intent continuity
```

The project must measure the real advantage in Milestone 10.

Do not assume the target speedup before the benchmark exists.
