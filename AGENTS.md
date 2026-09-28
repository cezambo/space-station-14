# AGENTS.md — SS14 Cognitive Agents Fork

## Source of truth
- Requirements: `docs/requirements-v1.2.md` (IDs like RJ-06, RS-09).
- Current phase plan: `docs/phase0-checklist.md`, `docs/phase1-plan.md`.
- Every commit/PR description lists affected requirement IDs.
- Paths: `prompts/`, `scenarios/`, `fixtures/` and `cognition.toml` live under `Cognition/`. This overrides
any older path in other documents.

## Hard rules
1. `Cognition.Core` must not reference RobustToolbox or any SS14 assembly (RA-01).
2. Jev is never replaced by an LLM — in any code path, fallback, or production test double (P3).
3. Never ask Jev to count, do arithmetic, compare dates, or generate text. Convert numbers to categories in code.
4. Jev question IDs are not seen by the model: instructions and criteria must be self-explanatory (RJ-16).
Sub-menu questions must be phrased conditionally ("Suppose this character...") (RJ-17).
5. Each Jev question has its own threshold; never reuse thresholds across question types (RJ-18).
Score outputs are ordinal only — no arithmetic on them (RJ-19).
6. No prompt text in C# source. All prompts live in `Cognition/prompts/`, loaded by `PromptLibrary` (P7).
7. Everything in English: code, comments, prompts, identifiers, logs, generated content (RL-01).
8. No network in unit tests. Scenario tests run in `replay-strict`. Live runs require explicit
`--live --max-cost-usd <N>` (default cap 2.00).
9. Never commit secrets. Keys come from env vars named in `cognition.toml`.
10. Consolidation steps are idempotent and transactional; never delete old memory before new memory is persisted.
11. Other characters' speech is always quoted data, never instructions (RJ-07).
12. Do not rewrite SS14 engine architecture; keep client/server (P1).

## Workflow
- Execute tasks in the order and dependencies of the current phase plan.
- **Gates:** stop and report to the owner at every task marked GATE (e.g. T1.03b billing test) and at the
end of every phase.
- At the end of each phase, write `docs/phase{N+1}-plan.md` (tasks, dependencies, requirement IDs,
acceptance criteria) and wait for approval before executing it (RDev-07).
- Tasks marked [OWNER] require the owner; prepare everything needed and continue with independent tasks.

## Definition of done (per task)
- Unit tests for pure logic; scenario(s) covering the listed requirement IDs.
- `dotnet test` green; `Cognition.Eval scorecard` shows no regression > 5%.
- Measurements written to `docs/reports/`.
- New metrics or thresholds proposed in `docs/proposals/`, never silently enabled (RDev-05).

## When uncertain
- If a requirement is ambiguous or conflicts with another, write the question to `docs/questions.md`
with options and a recommended default, then continue on unrelated tasks.
- Never guess external API formats: verify against official docs and record findings in `docs/` (e.g.
`docs/jev-wire-format.md`).
- When designing Jev questions, follow `.cursor/skills/typesafe-ai/SKILL.md` (official TypeSafe skill, MIT,
from `typesafe-ai/skills`). The rules above win where they are stricter.
