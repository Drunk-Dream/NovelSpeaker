# Background and Rationale

This file is non-normative. The rules in `SKILL.md` and the other references are the operative instructions.

The audit approach combines general software-maintenance practice with recurring concerns raised around long-running AI-assisted / “vibe coding” workflows:

- **Martin Fowler — “Vibe Coding”**: distinguishes exploratory generation from code that must become understandable and maintainable when it enters a durable codebase.
  - https://martinfowler.com/bliki/VibeCoding.html
- **Thoughtworks Technology Radar — “codebase cognitive debt”**: highlights the maintenance burden created when code volume and structural complexity outpace a team’s understanding of the system.
  - https://www.thoughtworks.com/en-us/radar/techniques/codebase-cognitive-debt
- **GitClear — AI code quality / maintainability analysis**: discusses signals such as duplication, churn, and reduced refactoring that can indicate accumulating maintenance debt in AI-assisted codebases.
  - https://www.gitclear.com/the_ai_code_quality_maintainability_gap
- **Sonar — AI code verification debt**: emphasizes that generated code still requires verification for correctness, security, and maintainability rather than being trusted because it compiles.
  - https://www.sonarsource.com/resources/library/ai-code-verification-debt/
- **OWASP Secure Coding with AI Cheat Sheet**: recommends independent review of AI-generated code, dependencies, and security-sensitive boundaries.
  - https://cheatsheetseries.owasp.org/cheatsheets/Secure_Coding_with_AI_Cheat_Sheet.html
- **OpenAI Skills documentation**: defines Skills as a `SKILL.md` plus supporting resources and encourages reusable, focused workflows.
  - https://developers.openai.com/api/docs/guides/tools-skills
  - https://developers.openai.com/plugins/build/skills

The skill intentionally does **not** treat AI authorship as evidence of poor quality. It uses the repository itself as evidence and focuses on structural characteristics that make future reasoning and change harder.

Additional rationale for the v3 subtractive audit:

- **Martin Fowler — “Yagni”**: unnecessary anticipated capability carries real build, delay, repair, and carry costs even when each individual addition looks small.
  - https://martinfowler.com/bliki/Yagni.html
- **Thoughtworks — “Refactoring with AI”**: discusses the tendency for generated development to favor producing new code and the continued importance of understanding/refactoring existing systems.
  - https://www.thoughtworks.com/en-sg/insights/podcasts/technology-podcasts/refactoring-with-ai
- **Google — Building Secure and Reliable Systems, Ch. 12**: includes eliminating dead code, unnecessary dependencies, and needless complexity as maintainability/reliability practices.
  - https://google.github.io/building-secure-and-reliable-systems/raw/ch12.html

The subtractive pass therefore asks not only whether current code is defective, but whether historical additions still earn their long-term cognitive and maintenance cost.
