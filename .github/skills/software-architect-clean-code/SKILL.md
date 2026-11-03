---
name: software-architect-clean-code
description: Shape maintainable application architecture and implementation structure with clear boundaries, cohesive modules, and reusable code without premature abstraction. Use for architecture, refactoring, cross-layer changes, or recurring code-structure decisions.
---

# Software Architect and Clean Code

Create software that is easy to understand, change, validate, and extend. Prefer the simplest structure that preserves clear responsibilities and the product's required behavior.

## Architecture decisions

- Trace a change across the full path: input/source, parsing or acquisition, domain model, application service, presentation state, UI, and exports where relevant.
- Keep domain meaning independent of UI formatting. Put parsing and external I/O behind explicit interfaces and keep orchestration in application services.
- Give each module a cohesive responsibility and a small, explicit API. Keep dependencies flowing toward stable domain abstractions; avoid cycles and broad global state.
- Normalize data at boundaries into typed, immutable models where practical. Preserve source identifiers and evidence provenance when they affect user trust or debugging.
- Reuse a pattern when it solves a repeated real need. Avoid generic frameworks, speculative extension points, duplicate abstractions, and one-off helpers that obscure the flow.
- Prefer explicit names and straightforward control flow. Keep methods focused, make invalid states difficult to represent, and surface expected failures as typed results or domain errors.
- Add interfaces for meaningful boundaries (external systems, file access, time, or replaceable policies), not merely to wrap every class or method.
- Keep compatibility and migration behavior deliberate when changing persisted data, public contracts, or file interpretation.

## Implementation and review

- Before restructuring, inspect the existing solution boundaries, conventions, callers, tests, and working-tree changes. Preserve unrelated user work.
- Make the smallest coherent change across all affected layers; do not leave a UI workaround for a missing domain capability or duplicate business rules across views.
- Add tests at the layer where behavior is owned. Test observable outcomes and important invariants, including error and uncertainty cases.
- Review code for duplicated logic, accidental coupling, hidden I/O, unsafe assumptions, misleading names, and abstractions with only one caller.
- Explain material tradeoffs and report what was built and verified separately from what remains uncertain or out of scope.

## Repository context

In Cubase Project Explorer, follow `AGENTS.md` and the open tasks in `PLAN.md`. Use C#/.NET 10, WinUI 3, MVVM, and Microsoft DI. Treat project files as untrusted and preserve the distinction between confirmed parsed fields and candidate evidence. Do not infer native Cubase structure from heuristics.
