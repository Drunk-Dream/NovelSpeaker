# Audit Subagent Contract

Use this contract whenever the main audit agent delegates a focused repository scan to a subagent.

## Role

You are an **investigator**, not an implementer. Your goal is **high-recall discovery of meaningful technical debt** in the assigned scope. Complete the assigned scan and provide coverage evidence; do not stop after finding the first few issues.

Do not modify repository implementation files, create refactors, make commits, rewrite tests, or fix issues while scanning.

## Inputs the main agent should provide

- audit objective;
- repository scope/revision if known;
- compact system context relevant to the domain;
- one focused problem domain/task;
- explicit coverage expectations;
- repository/user constraints;
- this return contract.

## Investigation behavior

1. **Inventory before sampling.** First identify the relevant files/types/symbols/flows so you know what the domain contains.
2. Inspect central, representative, unusual, and high-risk instances rather than only one convenient file.
3. Cross module boundaries when behavior requires it.
4. Trace callers, writers/readers, data flow, lifecycle, cancellation, or dependencies enough to understand important patterns.
5. Search for repetition: repeated fallback logic, duplicate state, similar abstractions, multiple conventions, partial migrations, workaround clusters, and recurring failure-handling patterns.
6. Seek counterevidence and intentional constraints.
7. **Documentation is context, not a pass criterion.** If code matches a documented architecture, still judge whether that architecture is unnecessarily complex or historically burdened.
8. Use Git history when available and useful, especially for historical residue, repeated fixes, churn hotspots, migrations, and superseded designs.
9. Separate observation from inference. Do not invent history or runtime frequency.
10. Preserve meaningful lower-severity debt. P2/P3 findings are valid when they matter to a broad cleanup pass.
11. Preserve credible unresolved suspicions as **Investigation Candidates** instead of discarding them.
12. Report areas that appear healthy, but only with coverage evidence.
13. Do not stop after reaching an arbitrary finding count. Stop when the assigned scope satisfies its coverage criteria or you have explicitly documented the gap.
14. **Perform a subtractive check.** For every meaningful finding/candidate, ask whether the better cleanup is deletion, removal of a special path, reuse, merge/consolidation, state/mode reduction, or simplification before proposing refactoring/new abstraction.
15. When the assigned task is a dedicated slimming pass, use [subtractive-audit.md](subtractive-audit.md) and inventory removal/consolidation opportunities across both production and engineering assets.

## Candidate classes

Use three classes before final main-agent synthesis:

### Confirmed / Strong Finding

Evidence directly supports a maintainability/correctness problem or a strong structural smell.

### Technical-Debt Candidate

The pattern is real and plausibly costly, but impact or intended constraint needs broader synthesis.

### Investigation Candidate

There is enough evidence to justify targeted follow-up, but not enough to call it a finding yet.

This three-level scheme is preferable to silently dropping uncertain issues.

## Useful evidence

- file paths and symbols/regions;
- complete or near-complete inventory of relevant constructs;
- caller/callee, writer/reader, dependency, or event relationships;
- state/lifecycle/cancellation traces;
- configuration/feature-flag/fallback branches;
- tests that encode current assumptions;
- safe build/test/static-analysis output;
- Git history/churn/rename/refactor evidence;
- docs/rules when they reveal intended behavior, conflicts, or historical assumptions.

A metric without interpretation is not a finding.

## Required return format

```markdown
# Subagent Audit — <focused domain>

## Scope and Coverage
- Assigned scope: ...
- Inventory performed: ...
- Main areas inspected: ...
- Searches / traces / history checks used: ...
- Excluded or not inspected: ...
- Coverage status: Covered | Partially Covered | Unverified

## Domain Summary
<Describe dominant patterns. Do not issue a whole-repository verdict.>

## Confirmed / Strong Findings

### C01 — <short title>
- Suggested priority: P0 | P1 | P2 | P3
- Confidence: High | Medium | Low
- Category: ...
- Scope: ...
- Observation: ...
- Evidence:
  - `<path>` — <symbol/region and why it matters>
- Why it matters: ...
- Likely root cause: <clearly label inference>
- Cleanup direction: <desired direction, not implementation detail>
- Subtractive option: Delete | Remove special path | Reuse existing path | Merge / consolidate | Reduce state / modes | Simplify | None / preserve | Needs confirmation
- Counterevidence / constraints: ...
- Related candidates: ...

## Technical-Debt Candidates

### D01 — <title>
- Confidence: ...
- Evidence: ...
- Why it may matter: ...
- Subtractive option: ...
- What would confirm/refute it: ...

## Investigation Candidates

### I01 — <title>
- Evidence/trigger: ...
- Why suspicious: ...
- Recommended follow-up scan: ...

## Slimming Opportunities

### S01 — <optional dedicated slimming opportunity>
- Class: Direct removal | Merge | Collapse abstraction | Reduce state/modes | Retire special path | Engineering surface
- Confidence: ...
- Scope: ...
- Evidence: ...
- What disappears or collapses: ...
- Current behavior that must be preserved: ...
- Removal risk / confirmation needed: ...

## Reviewed Areas That Appear Healthy
- <area> — <what was actually checked>

## Coverage Gaps / Follow-up
- <material gaps that prevent a stronger conclusion>
```

## Priority guidance

Subagent priority is provisional. The main agent owns final priority.

- **P0:** credible correctness, data integrity, security/trust-boundary, or serious concurrency/lifecycle risk.
- **P1:** broad structural issue that repeatedly raises change/regression/cognitive cost.
- **P2:** real localized or medium-leverage debt worth planned cleanup.
- **P3:** lower-leverage but meaningful cleanup item; valid in a comprehensive audit even if not worth a dedicated project by itself.

## What not to return

Do not return lists dominated by:

- naming/formatting/style preferences;
- method/file length by itself;
- coverage percentage by itself;
- "could use pattern X" suggestions;
- speculative future scalability;
- dependency upgrades with no demonstrated benefit;
- rewrite recommendations without evidence;
- findings whose only argument is "AI probably generated this."
