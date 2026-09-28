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

## Q-5 — Seed characters and stubbornness (2026-09-28, T1.08, T1.09, RD-01, RD-05)
Implemented as below. Please confirm or correct.
- (a) A character with both `stubborn` and `fickle` (or any two tags in `stubbornness_by_tag`) is a load error
  rather than averaged. Unknown tags are free text and do not change stubbornness.
- (b) Seed files never contain `baseStubbornness`; it is always derived, so the tag and the number cannot disagree.
- (c) The 10 seeds (`fixtures/characters/README.md`) are my own: 4 stubborn, 3 fickle, 3 default, one SS14 job
  each. Replace or edit freely; tests only require 10 valid seeds covering all three stubbornness values.
- (d) Opinion stubbornness is stored as a decimal number because E-01 tests +0.5 increments; base stubbornness
  from tags stays an integer.
- (e) Characters start on personal day 1 with no acquaintances (RD-03: names are learned in play).
- Status: open (non-blocking)

## Q-6 — Sandbox world choices (2026-09-28, T1.11, RP-01/02, RS-01…06)
Implemented as below; all numbers are `SandboxOptions` or `cognition.toml` values. Please confirm or correct.
- (a) Vision defaults to 360° (SS14 is top-down with occlusion only); the cone is configurable (`FovAngleDeg`).
  Range 16 tiles, reduced by fatigue per RS-02.
- (b) Line of sight checks every cell the centre-to-centre segment crosses (stricter than plain Bresenham,
  which can see through diagonal wall gaps). Two walls touching at a corner block sight, as in SS14.
- (c) `full_sleep_minutes` (4) is sleep **in a bed**; on the floor it is slower by `bed_recovery_multiplier`
  (6 min). Idle fatigue reaches 80 at T_wake; walking ×1.25; each damage point adds 1 % to the rate.
- (d) Sleepers perceive nothing. A shout within its range wakes them, and so does a hit of ≥ 5 damage.
- (e) An involuntary doze (critical fatigue) lasts 20 s; a collapse (fatigue 100) sleeps until rested. The world
  reports raw facts (cause, fatigue at sleep start, seconds slept); Core decides whether the day ended (T1.22).
- (f) Hunger 0→100 in 90 game minutes, thirst in 60; eating/drinking removes 40. No starvation damage yet.
- (g) Contract additions to plan §2.2: `RawSound.AddresseeGuid` (for the "addressed" trigger, RJ-01) and
  concrete shapes for `Affordance`, `ActionIntent`, `WorldEvent`, `GameClock`, which the plan only names.
- (h) The mind is an immutable record (`Cognition.Core.Minds.Mind`) rather than the mutable `AgentMind` sketch, so
  consolidation can work on a copy and commit with a version check (RS-09).
- Status: open (non-blocking)

## Q-7 — Perception block choices (2026-09-28, T1.12, RP-03/04/07/08)
Implemented as below; weights and bands are in `cognition.toml`, words in `prompts/vocabulary.yaml`.
- (a) Salience = 1.0/(1 + distance) + 0.5 if new + 0.8 if a goal word matches the name or held items
  + 1.0 if a trait is in `danger_traits` (bleeding, badly injured, unconscious, on fire). Goal words: ≥ 4 letters,
  minus `goal_stopwords`. The 8-entity and 5-item caps apply after ranking.
- (b) Sounds: the 5 most recent are kept (not ranked). Environment: at most 5 sensations, hazards first.
- (c) BODY always shows hunger, thirst and fatigue; body temperature and breathing only when not `ok`.
  Oxygen severity = (1 − saturation) / 0.5 × 100; body temperature severity = |T − 310.15 K| / 10 K × 100.
- (d) Damage and pain bands: minor < 10 ≤ moderate < 30 ≤ serious < 60 ≤ severe. Damage type names are the
  adapter's in lower case (RL-05: in-game names), e.g. "moderate blunt damage".
- (e) Empty sections print "- nothing notable" rather than being dropped, so the layout is stable for Jev.
- (f) Known people: "Bob (known)" in SEEN, plain "Bob" in HEARD, as in the plan example.
- Status: open (non-blocking)

## Q-8 (T1.14) Context assembler choices

- (a) Trimming starts only when the whole context exceeds `context.target_tokens` (4000) and stops as soon as it
  fits. Per-block targets (§9.3) are logged as `over_block_target`, never trimmed on their own.
  Alternative: trim each block to its own target first.
- (b) Items are removed one at a time in RJ-08 order: the whole daily summary, then recent memories oldest first,
  then opinions from least relevant (the caller orders them), then the least salient SEEN line. Heard sounds,
  environment and body stay.
- (c) After all trims, a context above the target is sent and flagged `over_target`; above the hard cap (32k) it
  throws `ContextOverflowException` (the decision is skipped).
- (d) "Recalibrated per block type from real usage": providers bill only the whole call, so per-block ratios are
  fitted with normalized least mean squares (step `context.calibration_rate` = 0.1), bounded to 1–8 chars/token.
  A unit test shows it recovers 4 different per-block ratios within 5 % from totals alone.
- (e) The §9.5 acceptance (context mean ≤4.1k, p99 ≤8k tokens in all scenarios) is measured once the decision
  loop runs agent scenarios (T1.16).
- Status: open (non-blocking)

## Q-9 (T1.15) Decision menu choices

- (a) Contract additions (plan §2.2): `RawPerception.Held` (the agent's own hands, so menus can name held items)
  and `KnownDestination.IsBed` (feeds `sleep_where`). Both optional; the sandbox fills them.
- (b) Verb → category: interact = open, close, lock, unlock, wake; use_item = use, eat, drink; inventory =
  pickup, drop, put, take, give. A category is offered only when its sub-menu has an option (RJ-05).
- (c) A menu with a single possible option is not asked (Jev needs 2+); the interpreter takes that option.
  If only "keep doing the current action" is possible, no call is made.
- (d) `sleep` is offered only when fatigue is at least mild (the `sleep_where` include_when). Options: sleep here
  (or "in the bed right here"), then known beds by distance; the nearest is marked when there are several.
- (e) Option descriptions live in `vocabulary.yaml` `menu:`; the category and think descriptions that were YAML
  comments in `decision.yaml` moved there (P7).
- (f) RJ-04: over 255 options → one Score question per option ("how well would it fit"), 50 per call, same state
  as the decision; the 30 best (Score compared only, RJ-19; ties keep the earlier option) go to the Choice.
  The context budget counts oversized menus as 30 options.
- (g) Labels come from the full raw perception, not the capped SEEN block, so an affordance is never dropped
  because its target did not fit in SEEN.
- (h) In `reduced` fan-out a category can be chosen whose sub-menu was not asked; that needs a follow-up call
  (not implemented while `fanout = "full"`, D12).
- Status: open (non-blocking)

## Q-10 (T1.22) Consolidation pipeline choices

- (a) The personal day advances and awake time resets when the consolidation commits, so a kill before the
  commit leaves the stored mind exactly as it was (RS-09). Fatigue is left alone; the sandbox owns the body.
- (b) Steps [1]–[5] are deterministic placeholders. The daily summary joins that day's recent memories;
  T1.23–T1.26 replace the steps. [6] refills the thinking budget (RS-10).
- (c) `step_max_retries = 2` means two retries after the first failure, then the step is deferred and named on
  `sleep.deferredSteps`. `commit_attempts = 3`.
- (d) Memories appended during the sleep (they do not change the mind version) are retagged to the next day
  in the commit. A commit of the mind body during consolidation is overwritten by the consolidation commit;
  append memories instead of committing the mind while a sleep is consolidating.
- (e) Schema version 2 adds a `checkpoints` table. A version-1 database is migrated on open.
- Status: open (non-blocking)

## Q-11 (T1.20) Exponential emotion decay

The requirements give the linear formula and say exponential decay is optional, without a formula. Implemented
exponential is `I0 · exp(-(d − d0) / τ)` while `d < d0 + τ`, and 0 from the expiry onward, so both curves
expire at `d0 + τ`. At the halfway point the exponential value is `I0 / sqrt(e)`.
- Status: open (non-blocking)

# Phase 0 questions

- **Q-P0-01:** With `net.bindto = "127.0.0.1"` in `Cognition/config/server_local.toml`, `ss` still showed listen on `0.0.0.0:1212` / `[::]:1212` after Ready. Confirm whether Robust rewrites bind when IPv6 is present, or whether the TOML value needs the dual-stack form (`127.0.0.1,::1`). Hub advertising is already off.
  - **Resolved (2026-09-27):** `net.bindto` was working. The UDP game socket was on `127.0.0.1:1212`. The `0.0.0.0`/`[::]` TCP listener was the status HTTP server, controlled by `status.bind` (default `*:<port>`). We set `status.bind = "127.0.0.1:1212"`, and `ss` now shows only `127.0.0.1:1212` for UDP and TCP. The client still connects.
- **Q-P0-02:** Owner must run `gh auth login`, fork `space-wizards/space-station-14`, and `git remote add origin <fork-url>` before any push.
  - **Resolved (2026-09-28):** fork `cezambo/space-station-14` exists, `gh` is logged in, `origin` and `upstream` are set.
- **Q-P0-03:** Client GUI playtest blocked in this headless Codespace — needs WSLg or a machine with display/GPU.
  - **Resolved (2026-09-27):** playtest done on Xvfb + llvmpipe (`docs/reports/phase0-playtest.md`).
