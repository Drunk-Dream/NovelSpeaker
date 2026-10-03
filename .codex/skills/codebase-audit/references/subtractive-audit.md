# Subtractive Audit / Codebase Slimming

Use this reference to audit whether the repository has accumulated code, concepts, states, paths, abstractions, tests, configuration, dependencies, or documentation faster than obsolete material is removed.

The objective is **not minimum LOC**. The objective is to preserve required product behavior, correctness, security, resilience, and diagnosability while reducing the amount of system structure that future maintainers and agents must understand.

A successful slimming opportunity removes or collapses something that no longer earns its maintenance cost.

## Core question

For every subsystem and every meaningful finding, ask:

> If this repository were designed today for its current supported behavior, which of these concepts, paths, states, compatibility rules, abstractions, or engineering assets would not need to exist?

## Mandatory decision order

Before proposing a refactor or new abstraction, consider the following in order:

1. **Delete** — can the code/path/config/test/doc/dependency/concept disappear entirely?
2. **Remove the special case** — can the product/runtime invariant be narrowed so this branch is unnecessary?
3. **Reuse** — can an existing implementation become the single path instead of adding or preserving another one?
4. **Merge / consolidate** — can two concepts, states, services, pipelines, or assets become one?
5. **Reduce state / modes** — can derived or redundant state disappear? Can invalid combinations be made unrepresentable?
6. **Simplify** — can the current implementation keep the same responsibility with fewer paths or layers?
7. **Refactor** — restructure only after the subtractive options above are insufficient.
8. **Add abstraction** — last resort, justified by current real variation rather than hypothetical future use.

A recommendation that adds more durable concepts than it removes requires explicit justification.

## Slimming opportunity classes

Use `Sxxx` IDs for dedicated slimming opportunities when useful.

### S-Delete — direct removal

Look for:

- dead/unreachable code after verifying framework/DI/reflection/XAML/plugin usage;
- replaced implementations still present beside the current implementation;
- obsolete migration and compatibility paths beyond the supported upgrade boundary;
- stale feature flags, settings, schema fields, commands, routes, providers, adapters, resource files, or scripts;
- workaround code for states that can no longer occur;
- dependencies/packages/tools with no current purpose;
- tests whose only purpose is protecting already-removed behavior or implementation shape;
- duplicate/obsolete docs and agent rules.

### S-Merge — consolidate equivalent concepts

Look for:

- multiple services/managers/coordinators with overlapping responsibility;
- duplicate state holders where one can become authoritative or derived;
- parallel code paths that implement the same behavior with small historical differences;
- several helpers/parsers/mappers/validators that can use an existing single implementation;
- separate engineering assets that carry the same rule or workflow.

Do not create a new generic framework merely to merge three small implementations if using one existing implementation is simpler.

### S-Abstraction — remove or collapse indirection

Look for:

- interface/base/factory/registry/resolver/coordinator stacks whose real runtime variation is small or nonexistent;
- wrappers that forward calls without policy, ownership, translation, security, lifecycle, or test-seam value;
- extension systems used by no realistic extension;
- abstractions kept because they might be useful in the future;
- public/internal APIs exposing options no current caller needs.

Ask what concrete maintenance problem each abstraction solves **today**.

### S-State — reduce state and modes

Look for:

- derived mutable state that can be computed from an authority;
- duplicated snapshots/caches without clear need;
- clusters of booleans controlling reentrancy, initialization, suppression, navigation, refresh, or recovery;
- persisted state that only mirrors another store;
- configuration/feature-flag combinations creating unnecessary modes;
- invalid state combinations normalized repeatedly instead of prevented at a boundary.

Reducing one state variable can be more valuable than deleting many ordinary lines if it collapses several behavioral combinations.

### S-Path — remove special/fallback/compatibility paths

Look for:

- legacy fallback after the system now has one supported representation;
- retry/repair/rebuild at several layers for the same condition;
- old and new runtime paths both retained after migration;
- branch matrices driven by historical versions or temporary rollout state;
- silent recovery for internal invariant violations that should now fail explicitly;
- provider-specific special cases that could use the common contract.

Do not remove real resilience for expected transient failures, platform constraints, offline behavior, or supported compatibility merely to reduce code.

### S-Engineering — reduce non-production maintenance surface

Look for:

- implementation-detail tests and redundant architecture guards;
- obsolete fixtures, snapshots, scripts, temporary debugging utilities, generators, or benchmark scaffolding;
- duplicated or contradictory long-term docs/rules;
- build/CI jobs or custom tooling whose value no longer justifies upkeep;
- package wrappers or local reimplementations of stable platform/library capabilities;
- archived migration/reference files still treated as active project context.

## Dedicated slimming pass

The comprehensive audit should include at least one focused **Subtractive Audit** task after enough system context is available. In a mature repository this may require several focused tasks rather than one giant pass.

The dedicated pass should:

1. inventory likely removable/collapsible assets across source, tests, config, dependencies, docs, scripts, and tooling;
2. use current call/reference/dependency evidence before declaring direct removal;
3. use Git history, when available, to find superseded implementations, completed migrations, temporary workarounds, replacement commits, feature pivots, and abstraction accumulation;
4. inspect high-churn and historically layered areas for old paths surviving beside replacements;
5. search for abstraction families (`Manager`, `Coordinator`, `Service`, `Provider`, `Factory`, `Registry`, `Resolver`, `Adapter`, `Helper`, etc.) and assess semantic value rather than naming alone;
6. inspect state/flag clusters and branch matrices for opportunities to collapse modes;
7. inspect tests/docs/configuration/tooling for assets that survive only because the repository historically kept adding without pruning;
8. preserve uncertain deletion candidates as investigation items rather than recommending unsafe removal.

## Cross-cutting subtractive check

Every domain subagent must also ask, for each meaningful finding or debt candidate:

- Can this be solved by deleting something rather than redesigning it?
- Can one existing path become the only supported path?
- Can two state owners/concepts be merged?
- Can a state become derived or disappear?
- Can a compatibility/fallback branch be retired based on current support requirements?
- Can a wrapper/abstraction be collapsed without losing a real boundary?
- Would the suggested cleanup add more concepts than it removes?

Record the best answer as the **Subtractive option**.

Allowed values:

- `Delete`
- `Remove special path`
- `Reuse existing path`
- `Merge / consolidate`
- `Reduce state / modes`
- `Simplify`
- `None / preserve`
- `Needs confirmation`

## Evidence threshold

### Direct removal

A direct-delete recommendation should normally have enough evidence to show both:

1. no required current behavior depends on it; and
2. there is no hidden invocation mechanism or supported compatibility requirement that makes simple reference search misleading.

For uncertain removals, use `Needs confirmation` and state exactly what must be checked.

### Concept removal

For removing an abstraction/subsystem/concept, explain:

- what responsibility it currently claims;
- where that responsibility could go or why it no longer exists;
- which consumers would become simpler;
- what boundary/value would be lost, if any;
- why current behavior does not justify retaining the concept.

## What slimming is not

Do **not** treat these as wins by themselves:

- fewer files;
- fewer interfaces;
- fewer lines of code;
- fewer tests;
- fewer validation checks;
- removing diagnostics, cancellation, security checks, resource cleanup, or explicit error handling that protect real requirements;
- converting readable explicit code into dense clever code;
- collapsing intentionally separate domain concepts just because their implementations resemble each other.

Prefer fewer **necessary concepts and paths**, not merely denser source text.

## Reporting

The final audit report should contain a dedicated `Codebase Slimming Opportunities` section. Keep these opportunities visible even when they are symptoms of a larger structural finding.

Recommended categories:

- Safe / high-confidence direct removals
- Merge / consolidation opportunities
- Abstractions that may be collapsed
- State / mode reductions
- Special/fallback/compatibility paths that may be retired
- Engineering-surface reductions
- Removal candidates requiring confirmation

Do not automatically schedule every slimming item for cleanup. Prioritize by removal leverage, risk, and dependency order.
