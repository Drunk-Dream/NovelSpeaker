# Prioritization and Synthesis

The audit is useful only if it both identifies high-leverage cleanup themes **and preserves the wider meaningful technical-debt inventory** for later cleanup sessions. Executive synthesis should be concise; evidence and lower-priority debt do not need to be.

## Priority is about cleanup value, not aesthetics

### P0 — Correctness / integrity / trust blocker

Use when the evidence indicates a meaningful risk such as:

- data corruption or lost user state;
- security/trust-boundary weakness;
- serious race/lifecycle behavior that can produce incorrect state;
- failure handling that can silently continue in an invalid state;
- repository/build behavior that makes produced artifacts materially unreliable.

P0 is not "code is ugly." It is a risk that should be understood before normal structural cleanup.

### P1 — Major structural debt

Use when a problem:

- spans important modules or central workflows;
- repeatedly complicates feature work;
- creates multiple sources of truth or unclear ownership;
- forces many special paths/flags/fallbacks;
- causes broad regression risk;
- makes important behavior hard to reason about;
- represents a root cause behind several local symptoms.

A codebase should normally have only a limited number of P1 themes.

### P2 — Localized debt

Use when the issue is real but:

- the blast radius is limited;
- the area changes infrequently;
- a workaround is contained and understood;
- cleanup is useful but not prerequisite to broader simplification.

P2 often becomes "clean when touching this area" rather than a dedicated project.

### P3 — Optional improvement

Use for low-return cleanup that is not meaningfully harming current development. P3 findings may remain in the wider technical-debt inventory when they are meaningful to a comprehensive cleanup. Omit only trivial style/aesthetic noise.

## Confidence is independent of priority

A potentially severe issue may still have Medium confidence. Do not inflate certainty because the impact would be large.

- **High:** observed directly and sufficiently traced.
- **Medium:** evidence supports the interpretation but an assumption remains.
- **Low:** plausible lead requiring more investigation.

Low-confidence items normally belong under Investigation Candidates / follow-up rather than the main remediation sequence. Do not silently discard a credible lead merely because it is not yet confirmed.

## Factors for prioritization

Use qualitative judgment across:

- **Correctness/trust risk** — can behavior become wrong, unsafe, or unrecoverable?
- **Blast radius** — how much of the system depends on it?
- **Change frequency** — how often must maintainers touch this area?
- **Cognitive load** — how many states, paths, concepts, or historical rules must be kept in mind?
- **Coupling** — how many unrelated components/tests change together?
- **Symptom generation** — is this a root cause producing several other problems?
- **Removal leverage** — can deleting or simplifying it eliminate substantial downstream complexity?
- **Cleanup dependency** — must this be resolved before other cleanup can be done safely?

Do not produce a numeric score unless the user explicitly asks for one. Numeric scoring creates false precision and often rewards easy-to-count smells over structural problems.

## Consolidating findings

Merge final structural findings when they share a root cause, but preserve the underlying candidate/debt IDs or examples so later cleanup sessions can recover the concrete evidence.

Bad synthesis:

- F01: too many loading flags
- F02: stale refresh results
- F03: cancellation missing in refresh
- F04: refresh service has semaphore
- F05: navigation sometimes starts another refresh

Potentially better synthesis:

- F01: refresh operation has no single owner/lifecycle, causing overlapping writers, ad-hoc reentrancy control, and incomplete cancellation.

Keep symptoms as evidence under the root finding.

Do not over-merge unrelated problems merely because they occur in the same module.

## Subtractive leverage

Treat removal/simplification value as independent from defect severity. A P2/P3 item can still be a high-value slimming opportunity if it safely eliminates substantial code, states, paths, dependencies, or maintenance surface.

For every proposed cleanup direction, ask in this order:

1. Can it be deleted?
2. Can the special/legacy path be removed?
3. Can an existing implementation become the only path?
4. Can concepts/state be merged or made derived?
5. Can the current implementation be simplified?
6. Only then consider structural refactoring or new abstraction.

Do not let a redesign survive synthesis merely because it sounds architecturally cleaner if a smaller subtractive change solves the current problem.

## Building cleanup order

Prefer this dependency order when supported by findings:

1. **P0 correctness/trust risks** that make later work unsafe.
2. **Core model and state ownership** problems.
3. **Module boundaries and lifecycle/async ownership** problems.
4. **Delete obsolete compatibility, fallback, duplicate, and workaround paths** made unnecessary by the clarified model.
5. **Consolidate inconsistent implementations and right-size abstractions.**
6. **Align tests with durable behavior.**
7. **Run/complete cross-cutting slimming:** collapse redundant abstractions/state/modes and remove engineering surface made unnecessary by earlier phases.
8. **Simplify docs, rules, configuration, build scripts, and dependencies.**
9. **Optional localized cleanup.**

This is not mandatory. The actual repository dependency graph governs the report.

## Cleanup-direction language

The audit should say things like:

- "establish one authoritative owner for X and make Y derived";
- "remove the obsolete compatibility path after verifying supported upgrade boundaries";
- "consolidate the three equivalent implementations behind the existing boundary";
- "reduce internal invalid states by validating at the input boundary";
- "make the operation lifecycle explicit and propagate cancellation through it".

Avoid premature implementation prescriptions such as exact class names, framework migrations, or a large new hierarchy unless the user asked for a design plan.

## Signs the report is overfitted

Reconsider the audit if:

- most findings are P1;
- findings mostly correspond one-to-one with files;
- every inconsistency is treated as a defect;
- the recommended cleanup adds more concepts than it removes without explicit need;
- the report contains no concrete opportunities to delete/merge/collapse anything in a mature, historically layered repository despite evidence of long-running additive development;
- the report contains dozens of tiny test/doc/style tasks;
- there are no areas judged healthy;
- recommendations require a rewrite without demonstrating why incremental simplification is infeasible.
