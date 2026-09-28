# Open questions (written by the agent)

Format per entry:
## Q-<n> — <short title> (<date>, <requirement IDs>)
- Context:
- Options: A) … B) …
- Recommended default:
- Status: open | answered (<answer>)

## Q-1 — Jev cost with full fan-out vs RC-04 (2026-09-28, RC-04, RC-05, §11, §9.3)
- Context: T1.03b showed billing is per call, so RC-05 keeps full fan-out. But question text is billed on
  every call (~2,020 tokens for the 13 decision questions). At 0.5 Hz × 15 characters, Jev alone costs
  ~$5.0/h with a ~2.4k-token state and ~$6.8/h at the 4k `context.target_tokens`, against a $6/h total
  target that must also cover the LLMs. Details: `docs/reports/T1.03b-billing.md`.
- Options: A) keep full fan-out; target ~2.5k-token decision state in T1.14; decide further cuts after
  T1.31 measures real cadence and LLM cost. B) switch to reduced fan-out now. C) lower mean cadence now
  (e.g. 0.35 Hz). D) raise the RC-04 target.
- Recommended default: A.
- Status: answered (2026-09-28): cost target set for 8 characters instead of 15, and mean cadence slightly
  lower (~0.4 Hz). Full fan-out stays. Recorded as D12 / RC-04. Jev estimate ≈ $2.15–2.91/h.
  Follow-up (2026-09-28): RNF-04 session target is also 8 characters; Phase 6 scale steps (10→15→20) unchanged.

## Q-2 — Score level numbering for `memory_importance_new_opinion = 4` (2026-09-28, RJ-19, §14, T1.21, T1.24)
- Context: the API numbers Score levels from 0 (`legend: {"0": …}`). `memory_filter.yaml` importance has
  5 levels: 0 Trivial, 1 Minor, 2 Moderate, 3 Major, 4 Critical. "importance ≥4" reads as "Major or
  above" if counted from 1, but only "Critical" as a 0-based index.
- Options: A) 1-based intent → store the 0-based index 3 (Major or above). B) 0-based → keep 4 (Critical only).
- Recommended default: A (a new opinion from "Major" events; "Critical only" would make new opinions rare).
  Not needed before T1.21.
- Status: open

## Q-3 — Prompt library choices made in T1.07 (2026-09-28, P7, RL-04, RA-03)
Implemented with these defaults; each is easy to change. Please confirm or correct.
- (a) "Missing placeholder = load-time error": values exist only at render time, so a missing value fails
  the **render** (all missing names reported, nothing sent). Load checks syntax, schemas, option/level counts
  and that every `threshold_key` exists in `[thresholds]`.
- (b) New fragment `llm/_json_reply.md`, appended to SYSTEM of every schema template with the schema inline,
  because schema descriptions do not reach the model (T1.04 finding; verified fixed live). New fragment
  `llm/_json_repair.md` for the repair round. The 11 spec templates are unchanged.
- (c) `repeat_per` question IDs are 0-based (`still_valid_0`, `still_valid_1`, …), matching list indexes.
- (d) Values cannot open/close a `<tag>` used by the template (e.g. `</heard>` in speech becomes `(heard)`).
- (e) The output-token limit per LLM template is not in the spec; the caller passes it at render. Proposal:
  a `[llm_output_tokens]` table in `cognition.toml` when the first consumer lands (T1.23).
- Status: open (non-blocking)

## Q-4 — Categorizer boundaries not in the spec (2026-09-28, T1.10, RP-06, RP-08, RS-07, RJ-09)
Implemented with these values in `cognition.toml`; all are config keys. Please confirm or correct.
- (a) Need bands: only fatigue is specified (RS-02: 60/80/95). Hunger, thirst, body temperature and oxygen use
  the same 60/80/95 on a 0-100 severity scale until the adapter (T1.14) shows the real SS14 ranges.
- (b) Day phase (RS-07 shows only "late"): early < 0.33, middle < 0.75, late < 1.0, overdue ≥ 1.0 of T_wake.
- (c) Budget band (RJ-09 says "band", no values): empty at 0, low < 30 %, some < 60 %, plenty ≥ 60 % of daily units.
- (d) Environment: pressure uses SS14's hazard/warning constants (20 / 50 / 385 / 550 kPa); temperature uses
  SS14 damage thresholds for the extremes (260 K / 360 K) and our own for "cold" (≤ 0 °C) and "hot" (≥ 50 °C).
  A gas is sensed at ≥ 0.5 kPa partial pressure, and only gases listed in `prompts/vocabulary.yaml`
  (oxygen and nitrogen are never sensed).
- (e) Edges: every boundary belongs to the closer / milder band for distance (1.5 is "within reach"), and to
  the more severe band for needs, phases and environment (60 fatigue is "mild"). A compass bearing exactly on
  a sector edge goes clockwise (22.5° is northeast). Intensity labels are cut at midpoints of `intensity_map`.
- Status: open (non-blocking)

# Phase 0 questions

- **Q-P0-01:** With `net.bindto = "127.0.0.1"` in `Cognition/config/server_local.toml`, `ss` still showed listen on `0.0.0.0:1212` / `[::]:1212` after Ready. Confirm whether Robust rewrites bind when IPv6 is present, or whether the TOML value needs the dual-stack form (`127.0.0.1,::1`). Hub advertising is already off.
  - **Resolved (2026-09-27):** `net.bindto` was working. The UDP game socket was on `127.0.0.1:1212`. The `0.0.0.0`/`[::]` TCP listener was the status HTTP server, controlled by `status.bind` (default `*:<port>`). We set `status.bind = "127.0.0.1:1212"`, and `ss` now shows only `127.0.0.1:1212` for UDP and TCP. The client still connects.
- **Q-P0-02:** Owner must run `gh auth login`, fork `space-wizards/space-station-14`, and `git remote add origin <fork-url>` before any push.
  - **Resolved (2026-09-28):** fork `cezambo/space-station-14` exists, `gh` is logged in, `origin` and `upstream` are set.
- **Q-P0-03:** Client GUI playtest blocked in this headless Codespace — needs WSLg or a machine with display/GPU.
  - **Resolved (2026-09-27):** playtest done on Xvfb + llvmpipe (`docs/reports/phase0-playtest.md`).
