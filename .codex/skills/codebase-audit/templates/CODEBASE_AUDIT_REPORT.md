# Codebase Audit Report

> Comprehensive repository audit for later cleanup planning. The report records both high-leverage structural findings and the wider technical-debt inventory. It is not an implementation plan and does not imply that every item should be fixed.

## 1. Audit Metadata

- Repository: `<name/path>`
- Revision / branch: `<commit and branch if available>`
- Working tree state: `<clean / dirty / unknown>`
- Audit scope: `<included areas>`
- Explicit exclusions: `<generated/vendor/out-of-scope areas>`
- Audit method: `<main agent + limited concurrent subagents across N batches>`
- Git history used: `<yes/no; scope>`
- Worklog: `<path if used>`

## 2. Executive Summary

### Overall maintainability shape

<Short synthesis. Do not infer health merely from clean layering or documentation conformance.>

### Highest-leverage themes

1. **<theme>** — <impact>
2. **<theme>** — <impact>
3. **<theme>** — <impact>

Keep the executive list concise even when the report body is long.

### Recommended cleanup strategy

<Dependency/root-cause order.>

## 3. System Overview

### Major modules / subsystems

| Area | Actual responsibility | Important dependencies / state |
|---|---|---|
| `<area>` | `<responsibility>` | `<notes>` |

### Core models and sources of truth

<Only what is needed to interpret findings.>

### Important lifecycle / execution flows

<Startup, navigation, requests, playback, background work, persistence, provider switching, etc.>

## 4. Audit Coverage

| Domain | Status | Evidence of coverage | Remaining gaps |
|---|---|---|---|
| `<domain>` | Covered / Partially Covered / Unverified / N/A | `<inventory/search/traces/modules/history>` | `<gaps>` |

A clean result without coverage evidence is not sufficient.

## 5. Findings Overview

### 5.1 Confirmed structural findings

| ID | Priority | Confidence | Finding | Main scope | Direction |
|---|---|---|---|---|---|
| F001 | P1 | High | `<title>` | `<areas>` | `<delete/simplify/consolidate/refactor>` |

### 5.2 Wider technical-debt inventory

| ID | Priority | Confidence | Debt / smell | Scope | Why retain in cleanup inventory |
|---|---|---|---|---|---|
| D001 | P2 | Medium | `<title>` | `<areas>` | `<reason>` |

Do not omit meaningful P2/P3 debt solely to keep the report short.

### 5.3 Investigation candidates

| ID | Confidence | Candidate | Evidence trigger | What would confirm/refute it |
|---|---|---|---|---|
| I001 | Medium | `<title>` | `<evidence>` | `<follow-up>` |

## 6. Detailed Confirmed Findings

### F001 — <Finding title>

- **Priority:** P0 | P1 | P2 | P3
- **Confidence:** High | Medium | Low
- **Category:** `<audit domains>`
- **Scope:** `<modules/components>`

**Observation**

<What the repository currently does.>

**Evidence**

- `<path>` — `<symbol/region>`: <supporting evidence>
- `<git/history evidence if relevant>`

**Why this matters**

<Correctness/change/cognitive-load/coupling impact.>

**Likely root cause**

<Inference, clearly marked.>

**Cleanup direction**

<Desired simplification; not detailed implementation.>

**Subtractive option**

`Delete | Remove special path | Reuse existing path | Merge / consolidate | Reduce state / modes | Simplify | None / preserve | Needs confirmation` — <short rationale>

**Dependencies / sequencing**

<Before/after relationships.>

**Constraints / counterevidence**

<What bounds the conclusion.>

---

## 7. Detailed Technical-Debt Inventory

Use this section for meaningful debt that may not deserve a top-level structural finding but should be visible during cleanup.

### D001 — <Debt title>

- **Priority:** P2 | P3
- **Confidence:** High | Medium | Low
- **Scope:** ...
- **Evidence:** ...
- **Maintenance cost:** ...
- **Likely cleanup direction:** ...
- **Subtractive option:** ...
- **When to address:** dedicated phase | with related structural cleanup | opportunistically

---

## 8. Investigation Candidates / Unresolved Questions

### I001 — <candidate>

- **Evidence trigger:** ...
- **Why suspicious:** ...
- **Why not confirmed:** ...
- **Recommended follow-up:** ...


## 9. Codebase Slimming Opportunities

This section is intentionally separate from defect severity. It records where the repository can become smaller/simpler while preserving current supported behavior, correctness, security, resilience, and diagnosability.

### 9.1 Safe / high-confidence direct removals

| ID | Scope | What can disappear | Evidence | Dependency / risk |
|---|---|---|---|---|
| S001 | `<area>` | `<code/path/asset>` | `<why unused/obsolete>` | `<checks before delete>` |

### 9.2 Merge / consolidation opportunities

| ID | Scope | Concepts/paths to merge | Resulting simpler model | Evidence |
|---|---|---|---|---|
| Sxxx | | | | |

### 9.3 Abstractions that may be collapsed

| ID | Scope | Current abstraction | Why its current value may not justify its cost | Confirmation needed |
|---|---|---|---|---|
| Sxxx | | | | |

### 9.4 State / mode reductions

| ID | Scope | State/modes that may disappear | What becomes authoritative/derived | Evidence |
|---|---|---|---|---|
| Sxxx | | | | |

### 9.5 Special / fallback / compatibility paths that may be retired

| ID | Scope | Path | Why it may no longer be required | Compatibility/risk check |
|---|---|---|---|---|
| Sxxx | | | | |

### 9.6 Engineering-surface reductions

Tests, docs, rules, scripts, configuration, dependencies, CI/build tooling, and other non-production assets.

| ID | Asset | Why it may be removed/merged | Evidence | Risk |
|---|---|---|---|---|
| Sxxx | | | | |

### 9.7 Removal candidates requiring confirmation

Keep credible opportunities here when hidden invocation, compatibility, external usage, or product requirements prevent a safe direct-delete conclusion.

## 10. Cross-Cutting Patterns and Root Causes

Connect multiple findings/candidates without deleting their individual evidence. Examples:

- unclear ownership creating flags, retries, and stale-state protection;
- historical compatibility becoming permanent architecture;
- several equivalent service/lifecycle/error conventions;
- abstractions added faster than superseded code is removed;
- tests/docs locking current implementation shape;
- repeated patch-on-patch history in the same subsystem.

## 11. Git / Evolutionary Evidence

When Git is available, summarize material history evidence:

- high-churn hotspots and what the history shows;
- repeated fixes or reversions;
- partial migrations/refactors;
- superseded implementations that may still survive;
- areas where current complexity clearly reflects historical layering.

Do not equate churn with poor quality by itself.

## 12. Recommended Cleanup Order

This section describes phases/dependencies, not implementation tickets.

### Phase 1 — <root/safety issue>

- Addresses: `Fxxx`, `Dxxx`
- Goal:
- Why first:

### Phase 2 — <next simplification>

- Addresses:
- Goal:
- Depends on:

### Later / opportunistic cleanup

<List lower-leverage debt without deleting it from the audit record.>

## 13. Reviewed Areas That Appear Healthy

Record meaningful negative findings **with coverage evidence** so future agents do not reopen them without new reasons.

- **<area>** — <what was inventoried/inspected and what supported the conclusion>

## 14. Tests, Documentation, and Engineering-System Assessment

### Tests / verification debt

<Behavior protection, implementation coupling, missing critical verification, redundant/fragile tests.>

### Documentation / rules

<Conflicts, historical residue, excessive normative detail. Do not treat document conformance as proof of good design.>

### Build / dependencies / configuration

<Material findings and healthy areas.>

## 15. Limitations and Unverified Areas

- <runtime behavior not reproduced>
- <domain only partially covered>
- <intent not confirmed>
- <platform/environment dependency>

## 16. Batch / Subagent Record

| Batch | Task | Focus | Coverage status | Candidate IDs |
|---|---|---|---|---|
| 1 | A | `<domain>` | Covered | `C01, D02` |

This record demonstrates that low concurrency did not imply shallow total coverage.
