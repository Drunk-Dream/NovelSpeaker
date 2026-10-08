# Codebase Audit Worklog

> Incremental working record for a comprehensive repository audit. This file may be long. It exists so batch results and evidence survive context-window limits. It is not the final cleanup plan.

## Audit Metadata

- Repository:
- Revision / branch:
- Started:
- Main audit report:

## Coverage Matrix

| Domain | Status | Tasks / batches | Coverage evidence | Follow-up |
|---|---|---|---|---|
| Core model / sources of truth | Not started | | | |
| Architecture / dependencies | Not started | | | |
| State ownership / lifecycle | Not started | | | |
| Async / concurrency / cancellation | Not started | | | |
| Hidden side effects / control flow | Not started | | | |
| Design consistency / API semantics | Not started | | | |
| Historical/dead paths | Not started | | | |
| Defensive/fallback/compatibility | Not started | | | |
| Duplication / abstraction balance | Not started | | | |
| Error/recovery semantics | Not started | | | |
| Configuration / flags / migrations | Not started | | | |
| Resource ownership | Not started | | | |
| Dependencies / reinvented wheels | Not started | | | |
| Build/tooling/repository hygiene | Not started | | | |
| Tests / verification debt | Not started | | | |
| Docs / rules / agent instructions | Not started | | | |
| Security / trust boundaries | Not started | | | |
| Observability / diagnostics | Not started | | | |
| Performance structural debt | Not started | | | |
| Git-history / churn hotspots | Not started | | | |
| Subtractive / codebase slimming | Not started | | | |

Use: `Covered`, `Partially Covered`, `Unverified`, `Not Applicable`, `Not started`.

## Batch Log

### Batch 1

#### Task A — <domain>

- Coverage:
- Candidate IDs:
- Raw evidence summary:
- Gaps:

#### Task B — <domain>

...

## Candidate Ledger

### Confirmed / Strong Findings

#### C001 — <title>
- Origin:
- Evidence:
- Related candidates:
- Main-agent validation status: Not reviewed | Confirmed | Merged | Downgraded | Rejected

### Technical-Debt Candidates

#### D001 — <title>
- Origin:
- Evidence:
- Follow-up:

### Investigation Candidates

#### I001 — <title>
- Origin:
- Trigger/evidence:
- Follow-up task:

## Slimming Opportunity Ledger

### S001 — <title>
- Class: Direct removal | Merge | Collapse abstraction | Reduce state/modes | Retire special path | Engineering surface
- Origin:
- Evidence:
- What disappears/collapses:
- Required behavior to preserve:
- Confidence / confirmation needed:
- Related finding IDs:

## Git / Historical Hotspots

| Area | History signal | Why inspect | Follow-up |
|---|---|---|---|

## Cross-Cutting Patterns Under Investigation

- <pattern> — evidence from <candidate IDs>

## Main-Agent Decisions

Record merges, rejections, priority changes, and why. This prevents later context loss from reviving discarded conclusions without new evidence.
