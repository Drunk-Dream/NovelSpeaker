# Scan Domains

Use this document as the **coverage inventory** for a comprehensive audit. It is intentionally broader than any one repository. Every applicable domain should be assigned a coverage status: Covered, Partially Covered, Unverified, or Not Applicable. Do not manufacture findings merely to fill categories, but do not skip a domain because early architecture inspection looks healthy.

Subagents should scan with high recall. Preserve meaningful local/historical debt and credible investigation candidates; the main agent later consolidates them into cross-cutting root causes. The most valuable final findings are often structural patterns supported by several examples, but the audit must not discard useful lower-level debt solely for brevity.

**Documentation rule:** documents and agent rules may describe intended behavior or current architecture, but conformance to them does not prove the design is healthy. Treat the documented design as auditable.

**Git rule:** when history is available, use it where useful to identify repeated fixes, churn hotspots, superseded paths, partial migrations, and historical layering. History is evidence, not an automatic defect signal.

## 1. Core model, data model, and sources of truth

Look for:

- one concept represented by several competing models or IDs;
- the same mutable state stored in database, cache, service, view model, and/or UI without a clear authoritative owner;
- persisted derived state that can drift from its source;
- models carrying historical fields whose semantics no longer match current behavior;
- state synchronization code that exists mainly because ownership is unclear;
- migration-era schemas or adapters retained indefinitely;
- domain concepts encoded indirectly through flags, null combinations, magic values, or string conventions.

Strong evidence:

- multiple writers for the same logical state;
- repeated synchronization/reconciliation code;
- comments or tests documenting drift cases;
- downstream callers needing to know storage/layout details they should not own.

Do not flag:

- intentional replicas/caches with a clear source of truth and invalidation model;
- DTO/domain separation where boundaries and transformations are clear.

## 2. Architecture, module boundaries, and dependency direction

Look for:

- modules whose responsibility cannot be stated simply;
- “god” services/view models/controllers/managers that own unrelated concerns;
- circular or back-channel dependencies;
- UI directly reaching into persistence/network internals without an intentional boundary;
- low-level modules depending on high-level feature concerns;
- modules that expose internal details because there is no stable boundary;
- cross-cutting behavior duplicated across features because no component clearly owns it;
- feature changes routinely requiring edits across many unrelated modules.

Strong evidence:

- dependency graph or import/reference patterns;
- repeated cross-layer access;
- central classes accumulating unrelated dependencies and responsibilities;
- change history showing unrelated features repeatedly touching the same hotspots, when available.

Do not assume more layers are better. A small application may be healthiest with few layers.

## 3. State ownership, lifecycle, and temporal coupling

Look for:

- unclear creator/owner/disposer of long-lived objects;
- state valid only if methods are called in a particular undocumented order;
- initialization flags proliferating to protect sequencing;
- event handlers or subscriptions whose lifecycle is unclear;
- stale state surviving navigation, reconnect, source change, logout, file change, etc.;
- caches whose invalidation ownership is diffuse;
- one feature relying on another feature having run first.

Typical signals:

- `_isInitialized`, `_isLoading`, `_isRefreshing`, `_ignoreNextX`, `_suppressX`, `_isNavigating`, or similar flag clusters;
- scattered `Dispose`, unsubscribe, reset, or reload logic;
- many “ensure initialized” checks at internal layers.

Flags are not inherently bad. Flag **interaction and unclear ownership** are the concern.

## 4. Async, concurrency, event flow, and cancellation

Look for:

- fire-and-forget work whose failures or lifetime are not owned;
- `async void` outside event boundaries;
- overlapping operations that can overwrite newer state with older results;
- cancellation tokens not propagated through a logical operation;
- locks/semaphores compensating for unclear ownership rather than protecting a clear shared resource;
- UI/background thread switching scattered across layers;
- reentrancy controlled by ad-hoc booleans;
- event-driven chains where the initiating action and final state transition are hard to trace;
- task lifetime extending past the object/page/session that launched it;
- retry and timeout behavior duplicated at several layers.

Prefer findings about the **operation lifecycle** rather than isolated syntax.

## 5. Hidden side effects and control-flow complexity

Look for:

- getters/helpers that perform I/O or mutate important state unexpectedly;
- methods whose name/contract hides persistence, network, navigation, cache invalidation, or global-state changes;
- deeply nested branching around modes, legacy states, flags, providers, or feature combinations;
- callbacks/events used as invisible control flow when a direct dependency or operation boundary would be clearer;
- behavior dependent on global/static mutable state;
- very long chains of “prepare → normalize → fallback → repair → retry → reconcile.”

A long method alone is not a finding. The concern is how many behavioral paths and side effects a maintainer must simulate mentally.

## 6. Design consistency and API semantics

Look for equivalent problems solved in several incompatible ways:

- multiple patterns for service ownership or dependency access;
- different error conventions for equivalent APIs;
- similar settings/features persisted through different mechanisms;
- several ways to update UI/domain state;
- different lifecycle/cancellation conventions for comparable operations;
- similar providers/plugins using incompatible contracts without a product reason;
- terminology that changes meaning across modules.

Do not force superficial uniformity. The approaches must solve sufficiently similar problems for inconsistency to create maintenance cost.

## 7. Historical code and dead paths

Look for:

- unreachable or unreferenced production code;
- retired feature implementations left next to replacements;
- old migration/compatibility code whose supported source version no longer exists;
- obsolete adapters, settings, schema fields, commands, routes, screens, providers, feature flags, or resource files;
- workaround comments for conditions that can no longer occur;
- branches that are permanently fixed by current configuration.

Verify before declaring code dead. Reflection, serialization, dependency injection, XAML/templates, plugin discovery, command routing, and generated wiring can make static references incomplete.

## 8. Over-defensive behavior, fallback chains, and compatibility burden

Look for:

- several layers each validating/correcting the same invariant;
- fallback to legacy behavior after an operation that should now have one supported path;
- automatic repair/rebuild/retry logic masking invalid state;
- catches that silently substitute defaults and continue;
- broad compatibility abstractions supporting no current caller;
- “just in case” branches based on hypothetical future needs;
- internal APIs accepting many invalid combinations and normalizing them everywhere.

Preferred direction:

- validate at boundaries;
- establish explicit invariants;
- let internal code rely on them;
- fail clearly when recovery is not genuinely supported.

Do not remove real resilience for expected transient failures such as networks merely to reduce lines of code.

## 9. Duplication, fragmentation, and abstraction balance

Look for both extremes.

### Under-abstraction / fragmentation

- same business rule copied in several places;
- repeated parsing, mapping, validation, state transitions, or provider logic;
- fixes repeatedly applied to several near-identical implementations.

### Over-abstraction

- interface/base/factory/registry/resolver/coordinator stacks for one or two stable implementations;
- wrapper layers that only forward calls and add no semantic boundary;
- generic frameworks built for hypothetical future features;
- configuration-driven indirection where direct code would be easier to understand;
- abstractions whose callers still need to understand all implementation-specific details.

The desired outcome is not minimum file count. It is the minimum number of concepts needed to represent actual variation in the product.

## 10. Error handling, failure semantics, and recovery

Look for:

- broad exception swallowing;
- logging-and-continuing when state may already be invalid;
- multiple layers wrapping the same error until actionable context is lost;
- inconsistent user-facing vs internal failure behavior;
- cleanup/rollback scattered across catch blocks;
- code that cannot distinguish expected cancellation, transient failure, invalid input, corruption, and programmer error;
- fallback behavior that turns a visible failure into silent incorrect behavior.

A finding should explain what state or contract becomes uncertain, not merely count `catch` blocks.

## 11. Configuration, feature flags, migrations, and environment assumptions

Look for:

- the same setting having defaults in several locations;
- stale feature flags or experimental switches;
- flags whose combinations create many behavioral modes;
- configuration migration code for versions that no longer need support;
- environment-specific paths or behavior scattered through feature code;
- settings that mix user preference, runtime state, compatibility state, and feature rollout state;
- “temporary” migration fields that became permanent.

Prefer one clear canonical representation and one boundary for migration/normalization where practical.

## 12. Resource ownership and cleanup

Relevant for desktop, mobile, server, native, and long-running processes.

Look for:

- event subscriptions not clearly released;
- streams/files/handles/timers/watchers/processes/HTTP clients with unclear lifetime;
- caches growing without ownership or bounds;
- disposable objects created by one layer and disposed by another without contract;
- background loops without a shutdown path;
- temporary files or directories with unclear cleanup semantics.

Avoid speculative leak claims without lifecycle evidence.

## 13. External dependencies and “reinvented wheels”

Look for:

- custom implementations of standard platform/library capabilities that materially increase maintenance burden;
- multiple libraries solving the same problem;
- abandoned packages retained behind wrappers;
- dependency abstractions whose only purpose is to hide a package that is not realistically replaceable;
- pinned/duplicated/transitive dependencies causing incompatible behavior;
- package choices that force excessive glue code.

Do not recommend dependency churn merely to use something newer. The replacement must reduce real complexity or risk.

## 14. Build, scripts, repository structure, and developer workflow

Look for:

- several competing ways to build/test/package the same product;
- stale scripts that encode obsolete workflows;
- generated artifacts committed or manually synchronized unnecessarily;
- fragile relative-path assumptions;
- duplicated configuration across CI/local/package scripts;
- manual steps that exist because project structure is unclear;
- large “misc/common/utils” areas acting as dumping grounds.

Only elevate these when they affect reproducibility or maintenance, not because the repository layout is unconventional.

## 15. Tests and verification debt

Evaluate whether tests protect **important behavior** rather than implementation shape.

Look for:

- tests tightly coupled to private/internal call structure;
- harmless refactors causing broad test rewrites;
- repeated tests asserting the same behavior through slightly different mocks;
- snapshot/golden tests covering unstable or low-value detail;
- trivial getter/setter/framework tests;
- temporary regression tests that no longer protect a meaningful invariant;
- heavy mocking that recreates implementation rather than exercising behavior;
- important core workflows with no effective verification;
- tests that pass while meaningful failure paths are masked by fakes.

A good audit distinguishes **too many low-value tests** from **too little protection for core behavior**. Do not optimize for coverage percentage.

## 16. Documentation, rules, and agent instructions

Look for:

- several documents claiming to be authoritative for the same rule;
- outdated architecture diagrams or decisions still presented as current;
- contradictory instructions across `AGENTS.md`, developer guides, task files, READMEs, and architecture docs;
- historical discussion mixed with current normative rules;
- completed migration plans still treated as active constraints;
- rules so detailed that normal changes require continual documentation edits;
- documentation that describes implementation details rather than durable contracts.

Preferred direction is a small number of clear sources of truth, not exhaustive prose.

## 17. Security and trust boundaries

This skill is not a full security audit, but obvious trust-boundary debt must not be ignored.

Look for:

- untrusted paths, URLs, command arguments, serialized data, plugin/provider input, or network content crossing into privileged operations without a clear boundary;
- secrets committed or logged;
- shell/process invocation with weak argument handling;
- unsafe deserialization or dynamic loading patterns;
- filesystem trust assumptions around links/reparse points/permissions where relevant;
- auth/authorization checks duplicated inconsistently;
- “fallback to success” or permissive defaults at security boundaries.

High-impact security findings are P0 candidates and should be clearly separated from ordinary maintainability findings.

## 18. Observability and diagnostics complexity

Look for:

- core operations impossible to diagnose because state transitions have no traceable boundary;
- logging scattered as ad-hoc behavior rather than reflecting meaningful operations;
- exceptions swallowed because logs are treated as a substitute for failure semantics;
- diagnostic code changing production behavior or adding significant coupling;
- multiple telemetry/logging systems with overlapping responsibilities.

Do not recommend more logging by default. First decide whether the underlying operation and state model are understandable.

## 19. Performance-related structural debt

Only include performance when supported by evidence or obvious architecture, such as:

- repeated full scans/reloads caused by missing ownership/invalidation boundaries;
- accidental N×M work across central paths;
- excessive serialization/copying due to fragmented models;
- expensive work triggered repeatedly by UI/property/event churn;
- caches compensating for an unnecessarily expensive architecture.

Do not micro-optimize or report speculative hot paths without evidence.

## 20. Subtractive audit / codebase slimming

This is a required cross-cutting domain for mature repositories, especially those developed through long-running AI-assisted/vibe coding. Read [subtractive-audit.md](subtractive-audit.md).

Look for opportunities to preserve current supported behavior while reducing the number of durable concepts and paths:

- code, resources, settings, schemas, adapters, providers, feature flags, scripts, tests, docs, dependencies, or build assets that can disappear;
- multiple equivalent implementations that can use one existing path;
- interfaces/factories/registries/resolvers/wrappers/coordinators whose current semantic value is smaller than their maintenance cost;
- redundant mutable/derived state and boolean-mode combinations;
- old/new, fallback, repair, migration, compatibility, or provider-specific paths that can be retired;
- engineering scaffolding retained after the need that created it ended;
- whole concepts or subsystems that exist mainly because previous iterations kept adding around them rather than removing/replacing them.

Strong evidence:

- no current production/test/framework/plugin consumer after hidden invocation mechanisms are checked;
- Git history showing a replacement/migration landed while the old path survived;
- multiple layers whose behavior collapses to forwarding/delegation with no meaningful policy boundary;
- state/branch inventory showing modes that are historical rather than product requirements;
- several assets encoding the same rule where one can be authoritative.

Do not equate fewer lines/files with better design. Preserve real security, cancellation, validation, diagnostics, resilience, resource ownership, compatibility, and domain distinctions when they serve current requirements.

## Cross-cutting questions

Across all domains, repeatedly ask:

- What inventory/search demonstrates that this domain was actually covered?
- Which important files/flows/hotspots remain uninspected?

- How many authoritative representations of this concept exist?
- Who owns creation, mutation, persistence, invalidation, cancellation, and disposal?
- How many special paths exist because of history rather than current product requirements?
- Does a caller need to know internal details it should not care about?
- Is this abstraction representing real variation or imagined future variation?
- Is defensive logic protecting an expected boundary or masking an invalid invariant?
- If this feature changes, how many unrelated modules and tests must change with it?
- Can a meaningful branch, state, layer, or compatibility path be deleted instead of generalized?
