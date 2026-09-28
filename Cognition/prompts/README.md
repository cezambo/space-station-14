# Prompt library

- `jev/*.yaml` — Jev question sets. Internal format; `JevHttpClient` maps it to the wire format
  documented in `docs/jev-wire-format.md`.
- `llm/*.md` — LLM templates with sections `## SYSTEM`, `## USER`, optional `## SCHEMA` (JSON Schema).
- `llm/_*.md` — fragments, not templates: `_json_reply.md` is appended to the SYSTEM section of every
  template with a SCHEMA; `_json_repair.md` is the user message of the schema repair round.
- Placeholders: `{{name}}`. A placeholder with no value at render time is an error (all missing names are
  reported). A value that no placeholder uses is a warning.
- Substitution is single-pass: `{{…}}` inside a value is never expanded. Inside a `<tag>…</tag>` region of a
  template, values cannot open or close that tag (other characters' speech is data, not instructions).
- Template hash (file content, plus fragments it uses) is part of the replay key (T1.05).
- Rules: English only; never ask Jev to count, compute or compare dates; question IDs are not seen by Jev,
  so `instructions`/`criteria` must be self-explanatory (RJ-16); sub-menus conditional (RJ-17).
- Describe every JSON field in prose too: schema `description`s are not reliably shown to the model
  (`docs/openai-compat-wire-format.md`, findings).

## Jev YAML fields
- `purpose` — telemetry/replay tag.
- `state_template` — shared state text.
- `questions.<id>.type` — `choice` | `score` | `noul`.
- `questions.<id>.instructions` — question text.
- `questions.<id>.criteria` — Choice: map option→description; Score: ordered list of level descriptions.
  Either may instead be a single placeholder (`"{{category_options}}"`) filled with an option set at render.
- `questions.<id>.when_true` / `when_false` — Noul only, optional, both or neither.
- `questions.<id>.include_when` — optional condition evaluated in code (used for reduced fan-out and
  periodic questions).
- `questions.<id>.threshold_key` — key under `[thresholds]` in `cognition.toml`.
- `repeat_per` — optional: generates one question per list element (e.g. per goal), IDs suffixed `_<i>`.
