# Engineering instructions

- Work on `main` unless explicitly instructed otherwise.
- Read `PLAN.md` before changing code; keep it accurate after every meaningful batch.
- Treat all Cubase project data as untrusted. Do not modify originals. No native `.cpr` plugin disabling without Cubase round-trip validation.
- Use C#/.NET 10, Uno Platform Desktop/Skia, XAML, MVVM, Microsoft DI, immutable normalized models and explicit evidence provenance.
- Do not report CPR candidate markers as definitive plugin identities or reconstruct Cubase state from unverified heuristics.
- Run tests and production builds after each batch. If unable to run, say exactly why and leave status pending.
- Prefer a few logical commits. Do not include build artifacts, project files, private plugins or copyrighted test projects.
- Keep user-facing output honest about verified versus planned behavior.
- For any Uno navigation, layout, visuals, project browser, filtering, graph, or accessibility work, use the UX principles in `.github/skills/professional-winui-ux/SKILL.md` where platform-neutral and complete the Uno acceptance checks in `PLAN.md`; a successful build alone is not visual approval.

## Repository skills

- Use `.github/skills/professional-studio-producer/SKILL.md` when shaping features around producer, engineer, or studio-operator workflows.
- Use `.github/skills/professional-ui-ux-designer/SKILL.md` for product flows and interaction design. Use Uno's official implementation guidance and the platform-neutral acceptance principles in `.github/skills/professional-winui-ux/SKILL.md` for UI validation.
- Use `.github/skills/software-architect-clean-code/SKILL.md` for architecture, cross-layer changes, refactoring, and code-structure decisions.
