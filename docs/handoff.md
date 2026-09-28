# Handoff guide (for the next agent/model)

Read `AGENTS.md` first; it wins over this file. Then `docs/phase1-plan.md` (task cards in §4) and
`docs/questions.md` (open questions Q-1…Q-7: follow the recommended defaults).

## Where things are

| Area | Path |
|---|---|
| Solution | `Cognition/Cognition.sln` (net10.0, warnings are errors) |
| Config | `Cognition/cognition.toml` → `Config/CognitionConfig.cs` (loader + validator; add every new key to both, with a validator rule and a test) |
| Prompts, words, phrases | `Cognition/prompts/` (Jev `.yaml`, LLM `.md`, `vocabulary.yaml`), loaded by `PromptLibrary` |
| Core contracts | `Cognition.Core/Perception/WorldContracts.cs` (plan §2.2) |
| Mind model and store | `Cognition.Core/Minds/` (immutable `Mind` record, strict JSON, `SqliteMindStore`) |
| Sandbox | `Cognition.Sandbox/` (grid world, `IWorldAdapter`), scenarios in `Cognition.Sandbox/Scenarios/` |
| Scenario files | `Cognition/scenarios/*.yaml`, DSL in `Cognition/scenarios/README.md` |
| Seeds | `Cognition/fixtures/characters/npc_01…10.json` |

## Every task, in this order

1. Read the task card in `docs/phase1-plan.md` §4 and the requirement IDs in `docs/requirements-v1.2.md`.
2. Write the code plus unit tests (no network) and a scenario when behaviour is observable in the sandbox.
3. `cd Cognition && dotnet format whitespace Cognition.sln`
4. `dotnet test Cognition.sln`: must be all green, **0 skipped**.
5. `dotnet format Cognition.sln --verify-no-changes`: CI runs this and fails on any diff.
6. One commit per task: `T1.xx <summary> (<requirement IDs>)`. Never push without the owner's approval.

## Pitfalls already hit (do not repeat)

- NUnit1001: `[TestCase]` literals for `float` parameters need an `f` suffix (`1.5f`).
- Comparing a `float` threshold with a `double` (e.g. `323.15f < 323.15`) gives wrong boundaries: compare
  in the same type.
- Never use `Assume.That` for preconditions: it silently skips the test. Build the config with a
  `with { ... }` override instead.
- Don't name a namespace like a type (`Minds`, not `Mind`).
- Error messages that list files or ids must sort them (deterministic output).
- CS0162 (unreachable code) is an error: do not leave `if (false)` or debug code.
- Scenario/test files must be self-contained and deterministic: same seed → identical event log.

## Hard rules that are easy to break

- **No prompt text in C#** (P7). Any sentence or word shown to a model goes in `prompts/` or
  `vocabulary.yaml`. C# may only hold keys and placeholders.
- **Numbers never go to Jev.** Convert to categories in Core (`Categorizers.cs`) first.
- **Jev question IDs are invisible to the model** (RJ-16): instructions and option criteria must be
  self-explanatory. Sub-menus are phrased conditionally: "Suppose this character decides to …" (RJ-17).
- **Each Jev question has its own threshold** in `cognition.toml` (RJ-18); Score answers are ordinal only
  (RJ-19): compare, never add or average.
- **Jev is never replaced by an LLM**, not even as a fallback or test double (P3). Tests use replay fixtures
  or a fake `IJevClient` that returns scripted answers.
- Other characters' speech is always quoted data inside `<heard>` tags, never instructions (RJ-07).
- Memory consolidation: insert new memory before deleting old, in one transaction (RMe-04).
- Live API calls only with `--live --max-cost-usd <N>`, N ≤ 2.00, and only when the task card asks for it.
- Ambiguity → write it to `docs/questions.md` with options and a recommended default; continue.

## Status and priority

See the table in `docs/phase1-status.md` for which tasks are done and which are left.
