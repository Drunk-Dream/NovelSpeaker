# Codebase Audit Skill

A Codex/Agent Skill for **comprehensive** codebase scanning before a cleanup/refactor pass, especially for repositories that have evolved through prolonged AI-assisted or vibe coding.

Version 3 keeps the limited-concurrency, multi-batch audit from v2 and adds **Subtractive Audit / Codebase Slimming** as a first-class objective:

- at most 3 audit subagents run concurrently by default;
- completed subagents are closed and replaced by later focused tasks;
- all applicable audit domains are tracked in a coverage matrix;
- subagents use high-recall discovery and may report lower-priority debt/candidates;
- the main agent performs later deduplication, validation, and root-cause synthesis;
- documentation is context, not the criterion for whether design is healthy;
- Git history is used when available to expose churn, partial migrations, and patch-on-patch evolution;
- the audit is file-first and can incrementally build a long report/worklog across many batches;
- at least one dedicated slimming pass asks what code, paths, states, abstractions, tests, docs, config, dependencies, and tooling can safely disappear or collapse;
- every ordinary finding is also checked for a subtractive option before redesign/new abstraction is accepted;
- the final report contains a dedicated `Codebase Slimming Opportunities` inventory.

## Contents

- `SKILL.md` — orchestration and complete audit workflow.
- `references/scan-domains.md` — detailed audit dimensions.
- `references/coverage-and-batching.md` — limited concurrency, sequential batches, coverage evidence, stop criteria.
- `references/subtractive-audit.md` — deletion/merge/state/path/abstraction/engineering-surface slimming rules.
- `references/subagent-contract.md` — high-recall subagent investigation contract.
- `references/prioritization.md` — priority/confidence and synthesis rules.
- `references/background.md` — rationale/source material.
- `templates/CODEBASE_AUDIT_REPORT.md` — comprehensive durable report template.
- `templates/CODEBASE_AUDIT_WORKLOG.md` — optional incremental batch/candidate ledger.

## Typical Codex placement

Repository-scoped:

```text
<repo>/.codex/skills/codebase-audit/
```

or place the skill under your reusable Codex skills directory.

Ask Codex to use `codebase-audit` to perform a comprehensive audit and write the report to a file. The scan should not stop after the first three subagents; it should continue in sequential batches until coverage criteria are met, including a dedicated subtractive/slimming pass.
