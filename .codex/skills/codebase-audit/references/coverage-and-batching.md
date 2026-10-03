# Coverage and Batching

The purpose of batching is to keep concurrent subagent count low **without sacrificing total audit depth**.

## Concurrency vs total work

- Default maximum concurrent audit subagents: **3**.
- Two concurrent subagents are often better when tasks are especially deep or the repository is small.
- Total subagent tasks may be much larger than three. Finish a batch, persist results, close agents, then launch the next batch.
- Do not keep idle/completed agents alive merely to preserve context; their findings must already be written to the worklog/report.

## Focused task sizing

A good subagent task has one coherent reasoning problem, for example:

- state ownership and sources of truth for playback/cache/settings;
- async lifecycle and cancellation for UI navigation/background work;
- historical compatibility/fallback/migration residue;
- abstraction/indirection and duplicated service patterns;
- test coupling and verification debt;
- documentation/rules/configuration drift;
- Git-history churn and repeated patching in identified hotspots;
- subtractive/slimming scan for removable code, concepts, states, paths, abstractions, tests/docs/config/tooling.

Avoid giant assignments that combine many independent concerns. If a task requires scanning several unrelated failure modes, split it into another batch.

## Minimum coverage evidence

Every subagent must report **how it looked**, not only what it found.

Depending on the domain, useful coverage evidence includes:

- inventory counts or lists of relevant files/types/symbols;
- searches used to find all/most relevant constructs;
- modules sampled and why;
- central/high-churn/high-complexity files inspected;
- caller/writer/reader/lifecycle traces performed;
- test/config/doc/history areas checked;
- explicit exclusions and remaining unknowns.

Coverage evidence is not a percentage. It is enough information for the main agent to judge whether "no major issue" is credible.

## Domain status

Track every applicable domain as one of:

- **Covered** — inventory and representative/high-risk inspection are sufficient for the audit goal;
- **Partially Covered** — meaningful work was done but important areas remain;
- **Unverified** — domain matters but could not be investigated adequately;
- **Not Applicable** — domain genuinely does not apply.

Do not mark a domain Covered merely because one or two files looked sound.

## Suggested multi-batch pattern

This is an example, not a fixed decomposition.

### Batch 1 — system foundations

- core/data models and sources of truth;
- module boundaries/dependencies;
- state ownership/lifecycle.

### Batch 2 — runtime complexity

- async/concurrency/cancellation/event flow;
- hidden side effects/control-flow complexity;
- error/recovery/resource ownership.

### Batch 3 — accumulated historical debt

- dead/legacy paths and partial migrations;
- compatibility/fallback/over-defensive logic;
- duplication/fragmentation/over-abstraction.

### Batch 4 — engineering system

- tests/verification debt;
- docs/rules/configuration/feature flags;
- dependencies/build/tooling/security/diagnostics as applicable.

### Batch 5 — subtractive / slimming pass

After enough system context exists, run one or more focused subtraction tasks:

- direct removal and historical residue;
- abstraction/concept collapse and duplicate-path consolidation;
- state/mode/special-path reduction;
- tests/docs/config/dependencies/tooling surface reduction.

Do not force all four into one subagent if the repository is mature. Keep the same concurrency limit and continue sequentially.

### Batch 6+ — hotspot and cross-cutting follow-up

Use earlier results and Git history to target:

- files/modules touched repeatedly by unrelated features;
- repeated bug-fix locations;
- classes with many dependencies/responsibilities;
- clusters of flags/fallbacks/events;
- abstractions with unclear value;
- areas where subagents disagree;
- partially covered domains.

## Git-history coverage

When Git is available, historical-debt scans should normally include some of:

- recent and long-range commit history;
- per-path/file history for suspicious hotspots;
- churn/touch-frequency discovery;
- commits introducing/replacing abstractions;
- repeated fixes in the same area;
- migrations/refactors where old paths may have survived;
- blame only when useful to locate the introducing change, not to assign human responsibility.

Do not label high churn as bad by itself. Use history to decide **why** the code evolved and whether obsolete layers/branches remain.

## Gap-analysis loop

After each batch, the main agent asks:

1. Which scan domains are still Partially Covered or Unverified?
2. Which modules have little direct inspection despite being central?
3. Which candidates need targeted confirmation?
4. Which cross-cutting pattern appears in more than one subagent result?
5. Which Git hotspots have not yet been structurally inspected?
6. Did any subagent rely too heavily on docs or a few representative files?
7. Has a dedicated subtractive pass checked what can disappear/merge/collapse, rather than only how existing code could be reorganized?
8. Are there recommendations that add abstractions without documenting why deletion/simplification is insufficient?

Launch the next batch from these gaps.

## Valid stop conditions

A scan should not stop because:

- a fixed number of subagents ran;
- the first batch found only minor issues;
- architecture documents and code agree;
- build/tests pass;
- the top-level dependency graph looks clean.

A scan may stop when all applicable domains have adequate evidence, hotspot follow-ups are complete, and remaining gaps are explicitly documented.
