---
name: codebase-audit
description: "Perform a comprehensive, evidence-backed audit of an existing software repository for structural maintenance debt, especially debt accumulated through prolonged AI-assisted or vibe coding. Use before a cleanup/refactor to discover architecture, state, async, historical, compatibility, complexity, test, documentation, configuration, dependency, engineering-system, and subtractive/codebase-slimming problems without modifying implementation code."
---

# Codebase Audit

Audit an existing repository before a cleanup/refactor pass. The objective is **comprehensive technical-debt discovery with evidence**, not merely finding a few severe defects.

This skill is designed for a **main agent + a limited number of concurrent subagents in repeated batches**. The main agent owns repository understanding, scan planning, coverage tracking, cross-validation, synthesis, prioritization, and the durable report. Subagents are focused investigators.

The audit must favor **high recall during discovery** and **high precision during synthesis**:

- subagents should surface all meaningful candidate debt in their assigned scope, including confirmed problems, strong smells, historical residue, excessive complexity, unnecessary compatibility/protection, inconsistency, and investigation candidates;
- the main agent later merges duplicates, tests root-cause hypotheses, lowers or raises priority, and separates confirmed findings from weaker candidates;
- do not discard a meaningful issue merely because it is not P0/P1 or has not caused a user-visible bug yet.

## Non-goals

Do **not**:

- modify production code, tests, normal docs, project configuration, or repository history while auditing;
- turn the audit into an implementation/refactor task;
- recommend a rewrite merely because the code is old, unfamiliar, inconsistent, or AI-generated;
- fill the report with formatting, naming, style, or trivial local smells that do not affect maintainability;
- maximize abstraction, layering, test coverage, or architectural sophistication;
- assume a build or passing tests prove the design is maintainable;
- assume repository documentation defines what is architecturally correct.

**Audit artifacts are the exception to the no-modification rule.** The main agent may create and incrementally update the audit report and an optional audit worklog. Do not edit unrelated repository files and do not commit audit artifacts unless the user asks.

If the user asks for both audit and implementation, finish and preserve the audit first. Treat implementation as a separate phase.

## Core Principles

1. **Optimize for future comprehension and change cost.** Ask how many concepts, states, modules, branches, historical rules, and special cases must be understood to safely change behavior.
2. **Understand the system before judging local code.** Build a lightweight system map before deep scans.
3. **Scan for both root causes and accumulated local debt.** Do not prematurely collapse everything into only a few themes; preserve useful lower-level evidence until synthesis.
4. **Treat subtraction as a first-class audit objective.** For every meaningful problem and subsystem, ask what can disappear entirely. Default decision order: **delete > remove special case > reuse > merge/consolidate > reduce state/modes > simplify > refactor > add abstraction**.
5. **Consistency beats locally clever design.** Equivalent problems should normally have one dominant solution unless product constraints justify variation.
6. **Treat state, ownership, and lifecycle as first-class concerns.** Multiple sources of truth, temporal coupling, hidden writers, and unclear task/resource ownership are high-value findings.
7. **Verify rather than trust.** AI-generated code may compile and test successfully while still masking errors or carrying maintenance debt.
8. **Docs are evidence, not authority.** Use documentation to understand intended behavior, history, and conflicts. Code matching a document does **not** prove the design is simple, necessary, or healthy. Audit the documented design itself.
9. **Git history is a first-class optional evidence source.** When available, use it to identify churn hotspots, repeated fixes, partial migrations, superseded designs, and patch-on-patch evolution. Do not treat churn alone as a defect.
10. **Tests, docs, configuration, dependencies, build tooling, and agent rules are part of the maintainability surface.**
11. **Coverage must be demonstrated, not asserted.** Every scan task records what it inventoried, what it inspected, and what remains unverified.
12. **Do not stop because the first batch found little.** Continue batches until all applicable domains satisfy their coverage criteria or are explicitly marked unverified.
13. **Audit for accumulated addition without subtraction.** A mature AI-assisted repository should receive a dedicated slimming pass that looks for obsolete code, redundant concepts, historical paths, unnecessary abstractions, excess state/modes, and engineering assets that can be safely removed or collapsed.

Read [references/scan-domains.md](references/scan-domains.md), [references/subtractive-audit.md](references/subtractive-audit.md), [references/coverage-and-batching.md](references/coverage-and-batching.md), and [references/subagent-contract.md](references/subagent-contract.md) before assigning deep-scan work.

## Operating Rules

- Respect user constraints and repository operational instructions such as build commands and safety constraints.
- Treat architecture/design documents and `AGENTS.md`-style files as **context and declared intent**, not as the standard by which maintainability is judged.
- Work against a known repository state. Record branch/revision when available and note relevant uncommitted changes.
- Exclude vendor, generated, dependency-cache, and build-output code from design judgments unless project-owned behavior materially depends on it.
- Running normal build, test, static-analysis, dependency, graph, search, and repository-inspection commands is allowed when useful and non-destructive.
- Metrics such as churn, line count, complexity, coverage, dependency count, and lint findings are discovery signals only. Trace behavior before making a finding.
- Do not infer intent from names alone. Trace callers, state writers/readers, lifecycle, and data flow when needed.
- Do not call code dead based only on text search when reflection, DI, XAML/UI markup, serialization, plugin discovery, generated wiring, or external invocation may apply.
- Do not conclude that a whole domain is healthy from a few representative files. Record coverage evidence.
- Do not issue an overall "architecture is healthy" conclusion until all planned audit batches are complete.

## Durable Audit Artifacts

The audit is expected to produce a long report. **File output is the default.** Do not place the full report in chat unless the user explicitly asks.

Use a user-specified path when provided. Otherwise choose a sensible repository-local location, preferably:

- `CODEBASE_AUDIT_REPORT.md` for the durable report;
- `CODEBASE_AUDIT_WORKLOG.md` for optional intermediate evidence, batch notes, and candidate findings.

If repository conventions clearly provide a better audit/docs location, use that instead and state it.

The report may be written **incrementally across many batches**. After each batch:

1. persist the batch scope and coverage;
2. persist candidate findings/evidence before dismissing subagents;
3. merge obvious duplicates, but do not over-compress evidence yet;
4. update the coverage matrix;
5. continue with the next uncovered domains.

This file-first workflow is intentional: do not rely on the conversation context to retain the entire audit.

## Workflow

### Stage 1 — Establish scope and repository state

1. Read the user request and operational repository instructions.
2. Identify languages, frameworks, application type, package/build systems, main entry points, test projects, docs, generated/vendor areas, and major top-level directories.
3. Record repository revision/branch and working-tree state when available.
4. Determine whether Git history is available and useful.
5. If the repository is extremely large, establish explicit boundaries and record exclusions.
6. Create the audit report/worklog and record metadata. Do not write conclusions yet.

### Stage 2 — Build a lightweight system model

The main agent should establish enough global context to plan focused scans:

- major modules/subsystems and actual responsibilities;
- important domain/data models and identities;
- persistence, cache, filesystem, network, and external-service boundaries;
- important state owners/sources of truth;
- dependency direction;
- startup/shutdown/navigation/request/job/playback lifecycles as relevant;
- important async/event-driven flows;
- provider/plugin/extension boundaries;
- test, documentation, configuration, and build organization.

Treat docs as hints. Verify the map against code and project references.

Keep the map compact enough to reuse in subagent prompts.

### Stage 3 — Build a complete scan plan

Use [references/scan-domains.md](references/scan-domains.md) as the domain inventory and [references/coverage-and-batching.md](references/coverage-and-batching.md) for batching.

Important rules:

- plan for **all applicable domains**, not just the 3–4 most important-looking ones;
- split broad domains into focused tasks when necessary;
- avoid assigning one subagent giant combinations such as "async + lifecycle + migrations + history + error handling" unless the repository is genuinely tiny;
- use the system map and code inventory to identify high-risk/hotspot follow-up tasks;
- include a later cross-cutting/hotspot pass after domain scans;
- include at least one dedicated **Subtractive Audit / Codebase Slimming** task after enough system context exists. Split it into several focused tasks when one pass would be too broad.

### Stage 4 — Run limited parallel batches

**Maximum concurrent audit subagents: 3 by default.** Do not exceed 3 unless the user explicitly allows more.

There is **no small total-subagent limit**. When a subagent finishes:

1. collect and persist its result;
2. close/release it;
3. assign the next focused scan task;
4. continue until planned coverage is complete.

A medium or mature repository may reasonably use many subagent tasks over several batches while never running more than 2–3 at once.

Each subagent receives:

- the audit objective and non-goals;
- relevant system-map context;
- one focused domain/task;
- explicit coverage obligations;
- permission to cross module boundaries where needed;
- the shared return contract;
- instructions not to modify repository implementation files.

### Stage 5 — High-recall candidate discovery

Subagents must complete their assigned scan rather than stopping after finding a few issues.

They should:

- inventory relevant constructs first;
- inspect representative and high-risk instances;
- trace important examples across callers/writers/lifecycle boundaries;
- search for repeated patterns and counterexamples;
- use Git history when useful to identify historical residue or repeated patching;
- report meaningful P2/P3 debt as well as severe findings;
- preserve uncertain but credible **investigation candidates** instead of silently discarding them;
- record reviewed areas that appear healthy;
- apply the subtractive decision order to meaningful findings and record the best **Subtractive option**;
- record coverage evidence and unverified areas.

There is no fixed candidate count. A subagent may return 3 findings or 30 if the evidence warrants it. Quality matters, but **do not impose an 8–12 finding cap**.

### Stage 6 — Persist each batch before continuing

After each batch, the main agent must update the audit files before launching too much additional work.

At minimum persist:

- subagent/task scope;
- coverage evidence;
- candidate findings with file/symbol evidence;
- uncertainties/unverified areas;
- Git/history evidence if used;
- duplicate/related-candidate links.

This prevents context-window loss and allows the audit to continue over many batches.

### Stage 7 — Gap analysis and follow-up batches

After the initial domain batches, inspect the coverage matrix.

Launch additional focused subagents for:

- domains with weak coverage;
- hotspots discovered by multiple agents;
- suspicious large/coordinating components;
- heavy churn/repeated-fix areas from Git history;
- cross-cutting patterns such as multiple service abstractions, fallback chains, state flags, event ownership, provider inconsistency, or migration residue;
- candidate findings that need targeted confirmation.

Do not stop merely because every top-level domain has been touched once.

### Stage 8 — Main-agent cross-validation and synthesis

The main agent must not concatenate subagent reports.

For P0/P1 and important cross-cutting P2 findings:

1. inspect key evidence directly;
2. trace enough behavior to validate the candidate;
3. check constraints and counterevidence;
4. distinguish current requirement from historical accident;
5. decide whether docs describe a requirement or merely codify current complexity;
6. use Git history when it can clarify whether a path is legacy, repeatedly patched, or superseded;
7. apply the subtractive decision order before accepting redesign/new-abstraction recommendations;
8. preserve dedicated slimming opportunities when they are actionable, even if they overlap a larger structural finding;
9. merge candidates that share a root cause, but keep their symptoms/examples in evidence;
10. retain meaningful lower-priority debt in the inventory rather than deleting it from the report.

Use [references/prioritization.md](references/prioritization.md).

### Stage 9 — Complete the durable report

Use [templates/CODEBASE_AUDIT_REPORT.md](templates/CODEBASE_AUDIT_REPORT.md).

The report may be long. It should distinguish:

- confirmed high-leverage structural findings;
- broader technical-debt inventory;
- **codebase slimming opportunities**: direct removal, merge, abstraction collapse, state/mode reduction, special-path retirement, and engineering-surface reduction;
- investigation candidates / unresolved suspicions;
- cross-cutting patterns and likely root causes;
- recommended cleanup sequence;
- reviewed healthy areas;
- coverage matrix and known gaps.

Do not reduce the final report to only 3–5 findings. The executive summary can highlight 3–5 themes, while the body preserves the wider debt inventory.

### Stage 10 — Final coverage and quality check

Before declaring the audit complete, verify:

- every applicable scan domain is marked **Covered**, **Partially Covered**, **Not Applicable**, or **Unverified**;
- every "Covered" domain has concrete coverage evidence, not just a statement;
- important modules/hotspots were not skipped because they did not fit the first batch decomposition;
- Git history was considered when available, especially for legacy/churn/historical-debt questions;
- documentation was not used as proof that a design is healthy;
- no high-priority finding relies on unverified assumptions presented as facts;
- lower-priority but meaningful cleanup debt has not been filtered away merely for brevity;
- a dedicated subtractive/slimming pass was completed (or explicitly marked unverified), and ordinary findings were checked for subtractive options;
- candidate findings were consolidated without losing actionable evidence;
- the report file is self-contained enough for a later cleanup session;
- implementation files remain unchanged unless the user explicitly expanded scope.

## Stop Condition

The audit is complete only when one of these is true:

1. all applicable domains meet their coverage criteria and follow-up hotspots have been investigated; or
2. the audit is intentionally bounded by time/scope/tooling, and all uncovered areas are explicitly recorded in the report.

"Three subagents finished" or "no severe problems were found" is **not** a valid stop condition.

## Default Audit Heuristic

When deciding whether something deserves investigation, ask:

> Does this make future changes require understanding or coordinating more states, paths, modules, rules, exceptions, historical assumptions, or test/document constraints than the product behavior actually requires?

Then ask:

> If the repository were designed today for its current supported behavior, what here could disappear, merge, become derived, or become the single supported path?

If either question exposes unnecessary structure, preserve it as candidate debt or a slimming opportunity until enough evidence exists to classify it.

## Supporting Files

- [references/scan-domains.md](references/scan-domains.md) — detailed scanning dimensions.
- [references/coverage-and-batching.md](references/coverage-and-batching.md) — sequential batching, coverage evidence, and stop criteria.
- [references/subagent-contract.md](references/subagent-contract.md) — focused high-recall subagent contract.
- [references/prioritization.md](references/prioritization.md) — classification, priority, confidence, clustering, and cleanup ordering.
- [references/background.md](references/background.md) — non-normative rationale and source material.
- [templates/CODEBASE_AUDIT_REPORT.md](templates/CODEBASE_AUDIT_REPORT.md) — durable report template.
- [templates/CODEBASE_AUDIT_WORKLOG.md](templates/CODEBASE_AUDIT_WORKLOG.md) — optional incremental batch/candidate ledger.
