# Requirements & Specification — SS14 Cognitive Agents Fork (Jev + LLM)

**Version:** 1.2 · **Date:** 2026-09-25 · **Status:** Baseline for development
**Owner:** project owner (user) · **Implementer:** agentic IDE (see `AGENTS.md`)

---

## 0. Document control

### 0.1 How to read this document
- Every requirement has a stable ID (e.g. `RJ-06`). Commits, PRs, tests and reports must reference IDs.
- "MUST" = mandatory; "SHOULD" = default, may be changed with owner approval; "MAY" = optional.
- Numeric values marked *(calibrate: E-xx)* are initial defaults to be tuned by the named experiment (§18).
- Only Phases 0–1 are planned in detail (`docs/phase0-checklist.md`, `docs/phase1-plan.md`). Later phases are
planned at the end of the preceding phase (RDev-07).

### 0.2 Decision log
| # | Decision | Section |
|---|---|---|
| D1 | "GLM 5.3 max" = GLM 5.3 with maximum reasoning effort | §4 |
| D2 | Opinion rupture re-evaluates **all relevant goals** (all horizons); relevance decided by Jev | §15.3 |
| D3 | **Sleep** defines the end of a day | §8 |
| D4 | Fortnightly compaction both creates new emotional modifiers and revises duration/intensity of existing ones | §12.3 |
| D5 | Fortnightly compaction uses the heavy model | §13 |
| D6 | Player control is switchable per character | §10 |
| D7 | **English everywhere**, internal and external | §5 |
| D8 | Stubbornness cap/decay decided by experiment | §14.3, E-01 |
| D9 | World persists; implemented late if hard | §16 |
| D10 | C# throughout | §3 |
| D11 | Keep all SS14 systems | §1.3, §9.6 |
| D12 | (2026-09-28, after T1.03b) Cost target is set for **8 characters**, not 15; mean decision cadence slightly lower (~0.4 Hz instead of 0.5) | §11 RC-04 |

### 0.3 Changes in v1.2 (from Jev API review)
| # | Change | IDs |
|---|---|---|
| C1 | No official C# SDK → custom HTTP client with 429 `retry-after` handling | RM-07 |
| C2 | Question IDs are not seen by the model → questions must be self-explanatory | RJ-16 |
| C3 | Parallel questions do not see each other's answers → sub-menus phrased conditionally | RJ-17 |
| C4 | Noul ≈ 0.5 means uncertainty, not medium intensity → goal relevance threshold 0.5 → **0.6** | RG-04 |
| C5 | Noul and Choice thresholds are not interchangeable → per-question calibrated thresholds | RJ-18 |
| C6 | Fractional Score values are not magnitudes → Scores used only as thresholds | RJ-19 |
| C7 | `/v1/models` may omit pinned version → model selection must not depend on listing | RM-02 |
| C8 | Billing of parallel questions unknown → **billing gate** before fan-out design is final | RC-05 |

---

## 1. Overview

### 1.1 Goal
Fork Space Station 14 (MIT code) into a **local** game with **10–15 autonomous characters** (hard limit **20**).
Each character has bounded perception, three-level memory, personality, dynamic opinions, emotions, goals in
three horizons, and a sleep cycle. Actions are decided by **Jev** (a "System 1" model). Dialogue and deliberate
reasoning use **LLMs** ("System 2").

### 1.2 Principles
| # | Principle |
|---|---|
| P1 | Keep SS14's client/server architecture; run both locally. Do not rewrite the engine. |
| P2 | Jev chooses intent; deterministic code executes (pathfinding, interaction, arithmetic). |
| P3 | Jev is never replaced by an LLM. Model selectors apply only to LLM roles. |
| P4 | The cognitive core (`Cognition.Core`) runs and is testable without SS14. |
| P5 | Every requirement has a metric: numeric when possible, otherwise Jev or LLM judge score. |
| P6 | No AI call blocks the server tick. |
| P7 | Prompt text lives in template files, never in source code. Single language: English. |

### 1.3 Scope
- **Kept:** all SS14 systems (atmos, fluids, chemistry, power, cargo, economy, game modes, antagonists).
- **Local-play adaptations:** authentication disabled via config; round auto-start (lobby optional);
launcher script that starts server + client (two windows acceptable).
- **Default mode in development/tests:** no antagonists. Other modes remain available; integration in Phase 8.
- **Out of scope:** localization/translation of any kind; online multiplayer.

---

## 2. Jev technical constraints (apply everywhere)

| Constraint | Consequence |
|---|---|
| Returns only **Choice** (≤255 options), **Score** (2–10 levels), **Noul** (P(yes)). No text generation. | Summaries, rewrites, dialogue → LLM or code templates. Jev filters, classifies, decides. |
| Weak at numbers, counting, dates. | Distances, time, budgets are given as text categories. All math in code. |
| Rate limit 1,200 requests/min per account. | Global scheduler targets ≤50% (§11). |
| Strongest in English. | System is English-only (§5). |
| Multiple questions over the same `state` are answered in parallel at negligible extra latency. | Menus + sub-menus in one call (§9.2), subject to billing gate (RC-05). |
| Question IDs are not sent to the model; parallel questions are independent. | RJ-16, RJ-17. |
| Early access: pricing, limits and API may change. | Pinned version, isolated client, Record/Replay. |

---

## 3. Architecture

```
┌──────────────────────── SS14 Server (local) ───────────────────────────┐
│ Game systems (all kept) + FatigueSystem │
│ ┌──────── Content.Server.Cognition (adapter) ──────────┐ │
│ │ PerceptionSystem · ActionMenuBuilder · ActionExecutor │ │
│ │ EventLogger · ControlSwitchSystem │ │
│ └──────────▲────────────────────────────┬──────────────┘ │
└────────────┼─── thread-safe queue ──────┼──────────────────────────────┘
│ ▼
┌─────────┴──────── Cognition.Core (C#, no RobustToolbox dependency) ─────────┐
│ AgentMind · Scheduler · SleepConsolidationPipeline │
│ Providers: JevClient | LlmClient (OpenAI-compatible) | Record/Replay │
│ PromptLibrary · Categorizers · Persistence (SQLite) · Telemetry │
└──────────────────────────────────────────────────────────────────────────────┘
Cognition.Sandbox (grid world for tests) · Cognition.Eval (metrics/scorecard/judges)
```

- **RA-01** `Cognition.Core` is a .NET library with no reference to RobustToolbox/SS14. It talks to the game
only via `IWorldAdapter` (perception, affordances, action submission, events, clock).
- **RA-02** Network calls are async; results are applied on the server main thread via a queue.
- **RA-03** All AI calls support Record/Replay (modes: `live`, `record`, `replay`, `replay-strict`).
Replay key = hash(canonical request + prompt template hash + model id).
- **RA-04** Launcher script starts server + client locally.
- **RA-05** C# throughout. `Cognition.Core` targets the same .NET version as the fork's RobustToolbox.
- **RA-06** Adapters provide **raw facts** (positions, numeric values, occlusion already applied).
**Categorization and text formatting live in Core**, so sandbox-tested formatting is reused unchanged in SS14.

---

## 4. AI models

| Role | Default | Swappable |
|---|---|---|
| System 1: decisions, emotion, filtering, classification, judging | **Jev** `jev-1.13.0` (pinned) | Only by another Jev version |
| Light LLM: speech, daily summary, impression extraction, light thought | **GLM 5.3 Flash** (`reasoning_effort=low`) | Yes |
| Heavy LLM: deep thought, opinion rewrite, post-rupture goal reassessment, fortnightly compaction | **GLM 5.3** (`reasoning_effort=max`) | Yes |
| Development (IDE) | Claude Opus 5.5 | n/a |

- **RM-01** In-game menu lists OpenRouter models (price, context) and allows selection per LLM role.
- **RM-02** System 1 role is shown locked, with an explanation. It uses the configured version and MUST NOT
depend on `GET /v1/models` listing it.
- **RM-03** Any OpenAI-compatible endpoint is supported for LLM roles (OpenRouter, llama.cpp, Ollama, tinyllm).
- **RM-04** Models pinned in `cognition.toml`; `reasoning_effort` configurable per role.
- **RM-05** API keys only via environment variables (`TYPESAFE_API_KEY`, `OPENROUTER_API_KEY`); never committed.
- **RM-06** Cost panel: tokens and USD per role, per character, per hour.
- **RM-07** Jev client: custom HTTP (`POST {base}/systemone`, Bearer auth); honor `retry-after` on 429 with
jitter (≤3 retries); exponential backoff on 5xx; default timeout 2 s; local validation of request limits.

---

## 5. Language

- **RL-01** Everything is English: prompts, memories, opinions, goals, perception, character speech,
new UI (inspector, AI menus, cost panel), logs, code, identifiers, comments.
- **RL-02** Speech LLM returns plain English text; the same text is displayed and delivered to listeners.
- **RL-03** The player speaks English when controlling a character. Non-English input is unsupported/untested.
- **RL-04** Prompt templates live in `prompts/`; no language layer exists.
- **RL-05** Proper names, items and locations keep their in-game names.

| Metric | Target |
|---|---|
| Generated speech detected as English | 100% |

*Future note (out of scope):* another output language can be supported by instructing only the speech LLM.

---

## 6. Character data model

```json
{
"id": "npc_07",
"stableGuid": "7b1c…",
"ss14Profile": { "name": "Ana Souza", "species": "Human", "age": 34, "job": "Chef", "flavorText": "..." },
"personality": {
"traits": { "openness": 0.6, "conscientiousness": 0.8, "extraversion": 0.4, "agreeableness": 0.7, "neuroticism": 0.3 },
"tags": ["stubborn", "protective"],
"baseStubbornness": 6,
"likes": [{ "text": "cooking for others", "strength": "strong" }],
"dislikes": [{ "text": "wasting food", "strength": "moderate" }]
},
"goals": { "immediate": [], "medium": [], "long": [] },
"memory": { "recent": [], "daily": [], "fortnightly": [] },
"opinions": { "general": [], "social": [] },
"emotion": { "lastDistribution": {}, "modifiers": [], "decisionsSinceLastCheck": 0 },
"thinkingBudget": { "dailyUnits": 20, "remaining": 20 },
"sleep": { "personalDay": 12, "awakeSeconds": 1430, "fatigue": 41 },
"control": { "mode": "ai" },
"acquaintances": { "npc_03": { "knownName": "Bob", "firstMetDay": 2 } },
"version": 0
}
```

- **RD-01** Personality = Big Five (0–1) + trait tags + likes/dislikes. Base stubbornness derived from tags:
`stubborn`=8, `fickle`=3, default=5 *(calibrate: E-01)*.
- **RD-02** SS14 profile data (name, species, age, job, flavor text) imported at creation.
- **RD-03** Acquaintance registry: a person's name appears in perception only after introduction/identification;
otherwise a visible description (e.g. `man in a lab coat`).
- **RD-04** Persistence in SQLite, keyed by `stableGuid` independent of SS14 entity IDs.
- **RD-05** Each goal has: `id`, `text`, `horizon`, `status` (active/done/dropped), `priority` (high/medium/low),
optional `successCheck` (observable condition).

---

## 7. Perception

### 7.1 Environmental
- **RP-01 Vision:** only entities/tiles inside FOV and not occluded (raycast). FOV angle and range from
character stats, injuries, items (glasses, flashlight) and lighting.
- **RP-02 Hearing:** speech/sounds within acoustic range (normal speech ~10 tiles, whisper ~2, shout more),
attenuated by walls, with estimated direction and distance.
- **RP-03** Dialogue includes speaker (name or description per RD-03), content, volume.
- **RP-04 Salience:** max per category — 8 entities, 5 items, 5 environmental conditions, 5 sounds — ranked by
code heuristic (proximity, novelty, goal relevance, danger).
- **RP-05 Categorical format:** distance `within reach` (≤1.5 tiles) / `near` (≤5) / `medium` (≤12) / `far`;
8 compass directions. Example: `Bob (known) — near, northeast — holding a fire extinguisher — looks injured`.
- **RP-06** Pressure, temperature, gases translated to sensations (`thin air`, `smell of plasma`, `freezing cold`);
visible puddles and fire included.

### 7.2 Biophysical
- **RP-07** Health in bands: damage by type/body area, pain, bleeding, consciousness.
- **RP-08** Needs in bands `ok` / `mild` / `strong` / `critical`: hunger, thirst, fatigue, body temperature, oxygen.

| Metric | Target |
|---|---|
| Information leakage (anything outside FOV/hearing range in snapshot) | **0%** |
| Direction (8 sectors) / distance band accuracy | ≥98% / ≥98% |
| Mean perception block size | ≤900 tokens |
| Snapshot cost on main thread | ≤0.3 ms per character |

---

## 8. Sleep, fatigue and personal day

### 8.1 Fatigue
- **RS-01** New `FatigueComponent`, 0–100. Rises with time awake; faster with exertion and injury.
- **RS-02** Bands and effects:

| Fatigue | Band | Effect |
|---|---|---|
| 0–59 | `ok` | none |
| 60–79 | `mild` | `sleep` option emphasized in menu |
| 80–94 | `strong` | −15% speed, −20% perception range |
| 95–99 | `critical` | −30% speed, −40% perception, chance of involuntary doze |
| 100 | — | collapse: falls asleep in place |

- **RS-03** Uses SS14's existing sleeping state (sleep action, beds). Sleeping in a bed recovers faster.
- **RS-04** Defaults: awake period ${T}_{wake}$ ≈ 40 real minutes; full sleep ≈ 4 real minutes; accelerated mode
for tests *(calibrate: E-02)*.

### 8.2 Day definition
- **RS-05** A **consolidated sleep** (duration ≥ `sleep.min_consolidated_seconds`, default 90 s, **and** starting
fatigue ≥ 40) ends the character's personal day. Only consolidated sleep ends a day.
- **RS-06** Naps, fainting, critical state and sedation do not end the day; they partially reduce fatigue.
- **RS-07** Each character has its own personal-day counter; a fortnight = 15 personal days. Context shows
station time and personal day (e.g. `station time 14:20; your day 12, late (tired)`).
- **RS-08** Fractional day for continuous calculations:

$d={n}_{consolidatedsleeps}+\min\limits_{}\left({1,\frac{{\ t}_{awake}}{{T}_{wake}}}\right)$

### 8.3 Consolidation pipeline (runs during consolidated sleep)
```
Falls asleep ─► [1] recent → daily summary (light LLM)
─► [2] opinions: impression extraction (light) → classification (Jev) → buffers/thresholds
─► [3] on rupture: rewrite (heavy) → goal relevance (Jev) → reassessment (heavy)
─► [4] daily medium-goal reassessment (light; merged with [3] where overlapping)
─► [5] if 20 dailies: fortnightly compaction (heavy) — §13, §12.3
─► [6] refill thinking budget
─► atomic commit
```
- **RS-09** Consolidation is async and transactional, operating on a copy of the mind. Each step is idempotent
and checkpointed. Commit uses optimistic versioning; events that occurred during sleep are merged into the next day.
If the character wakes before completion, it acts with the prior state until commit.
- **RS-10** Consolidation does not consume thinking budget.
- **RS-11** Safety cap `memory.recent_hard_cap` (default 400); lowest-importance recent memories dropped beyond it.
- **RS-12** A failing step is retried ≤2 times; persistent failure defers that step to the next sleep and is logged.

| Metric | Target |
|---|---|
| Voluntary sleep before collapse (bed available) | ≥80% |
| Mean personal-day duration / ${T}_{wake}$ | 0.8–1.3 |
| Consolidation duration p95 (real models) | ≤120 s real |
| Corrupted/lost state after interruption at any step | 0 |

---

## 9. Actions and decisions (Jev)

### 9.1 Decision cycle
- **RJ-01** Event-driven decisions. Triggers: action completed/failed, addressed by someone, damage taken,
new salient entity, need band change, goal completed/blocked. Min interval 1 s; max without trigger 8 s
(20 s when idle) *(calibrate: E-03)*.
- **RJ-02** Between decisions, `ActionExecutor` sustains the current action using SS14 pathfinding/steering
(reusing NPC/HTN infrastructure).
- **RJ-03** Movement extent options: `one step` / `short` (~3 tiles) / `medium` (~8) / `until end or obstacle`.
Actual speed from biology, items and fatigue; paths respect barriers.

### 9.2 Menus (single call, parallel questions)
| Question | Type | Options |
|---|---|---|
| `action_category` | Choice | nothing, move, interact, use item, inventory, speak, think, sleep |
| `move_target` / `move_direction` / `move_extent` | Choice | known destinations / 8 directions / extents |
| `interact_target` | Choice | reachable entities × available verbs |
| `use_item` | Choice | held/carried items × uses |
| `speak_target` / `speak_intent` | Choice | listeners (+ everyone) / intents |
| `think_mode` | Choice | light, deep (only affordable options) |
| `sleep_where` | Choice | here, nearest known bed, known beds |
| `goal_blocked` | Noul | does the first immediate goal seem impossible? |
| `emotion` (every 5th decision) | Choice | emotions (§12) |

- **RJ-04** Menus >255 options: two stages (Score for shortlist in batches → Choice over top 30).
- **RJ-05** Impossible actions are never offered (filtered by reach, free hands, preconditions).
- **RJ-06** If winning option confidence < threshold (default 0.35 for `action_category`) → `nothing`, logged
as `low_confidence` *(calibrate: E-04)*.
- **RJ-07** Other characters' speech is always quoted data, never instructions.
- **RJ-16** Question IDs are not seen by Jev: each question's `instructions` and option `criteria` MUST be
self-explanatory.
- **RJ-17** Sub-menu questions MUST be phrased conditionally (e.g. "Suppose this character decides to walk
somewhere now. Which destination…?"). The interpreter uses only the sub-menu matching `action_category`.
- **RJ-18** Each question has its own calibrated threshold; thresholds are never reused across question types.
- **RJ-19** Score outputs are used only as thresholds/ordinal bands, never as continuous magnitudes in arithmetic.
- **RJ-20** Option keys are short and unique; descriptions go in `criteria`
(e.g. `eat_sandwich_1: "Eat the sandwich on the table (within reach, south)"`).

### 9.3 Context sent to Jev (hard cap 32k tokens, target <4k)
| Block | Target tokens |
|---|---|
| Instructions + thinking budget explanation | 500 |
| General context + time (station time, personal day, phase) | 100 |
| Immediate + medium goals | 250 |
| Personality (summary + top-5 likes/dislikes) | 250 |
| Emotion + active modifiers | 120 |
| Current action + inventory | 200 |
| Perception | 900 |
| Recent memory (relevant window) | 900 |
| Most recent daily memory (short version only) | 350 |
| Opinions about present targets (≤3, one sentence each) | 150 |
| Menus | 400 |
| **Total** | **~4,100** |

- **RJ-08** Context assembler trims by priority and logs per-call size. Trim order: daily → oldest recent
memories → opinions → least salient perception items. Never trimmed: instructions, immediate goals, body, menus.
- **RJ-09** Thinking budget shown as a band with relative costs (light ≈ 1 unit, deep ≈ 6); unaffordable
options removed.

### 9.4 Speech
- **RJ-10** When `speak` is chosen, the light LLM generates ≤2 sentences of English from target, intent,
personality, emotion, recent memory, relevant opinion and last lines heard.
- **RJ-11** Speech does not consume thinking budget; rate limit 1 line per 6 s per character.
- **RJ-12** If a line is ready >10 s after the request, a Noul checks it is still pertinent before speaking.

### 9.5 Acceptance
| Metric | Target |
|---|---|
| Decision latency p95 (Jev) | ≤600 ms |
| Speech latency p95 (decision → displayed) | ≤3 s |
| Invalid actions executed | 0 |
| `nothing` rate with critical need and visible resource | ≤10% |
| Time to address critical need (food visible) | ≤30 s game time |
| Context mean / p99 | ≤4.1k / ≤8k tokens |

### 9.6 Action space with all systems kept
- **RJ-13 Layer 1 (Phases 3–4):** generic verbs (Verb System), hand interactions (use item on target, pick up,
drop, put in container), doors, beds, food/drink, extinguishers.
- **RJ-14 Layer 2 (Phase 8):** machines with custom UIs (consoles, dispensers, cargo) via per-machine adapters
translating UI into Choice options, in owner-defined priority order.
- **RJ-15** Entities without an adapter appear in perception but are not interactable; no errors.

---

## 10. Switchable control

- **RCt-01** Per-character modes: `ai`, `player`, `observer`. A command/hotkey takes or returns control.
At most one character under player control at a time.
- **RCt-02** In `player` mode: no Jev decision calls and no emotion checks; perception and event logging continue.
Player actions/speech enter memory as the character's own (flag `playerControlled` visible only in debug).
- **RCt-03** Consolidated sleep still works in `player` mode.
- **RCt-04** On return to AI: Noul validity check per immediate goal; if any invalid, one free light
reorientation thought (no budget cost); then an emotion check.
- **RCt-05** Switch completes in ≤1 s with no state loss.

| Metric | Target |
|---|---|
| Salient events from `player` period present in recent memory | ≥95% |
| Behavioral coherence after return (LLM judge) | ≥80% |

---

## 11. Call budget and cost

- **RC-01** Global scheduler caps Jev at ≤10 req/s, prioritizing urgent triggers:

$prio={w}_{u}\cdot urgency+{w}_{t}\cdot \frac{{t}_{since\ last}}{{t}_{max}}+{w}_{v}\cdot visibletoplayer$
Sleeping and `player`-mode characters make no decision calls.
- **RC-02** Under saturation, lowest-priority characters keep current action or fall back to SS14 HTN.
Jev is never replaced by an LLM.
- **RC-03** Offline: deterministic HTN behavior; speech via local LLM if configured.
- **RC-04** Target: **≤ US$ 6/h with 8 characters** (D12; was 15), measured via RM-06. Mean decision cadence
target ~0.4 Hz per character (was 0.5), tuned in E-03.
- **RC-05 Billing gate:** before finalizing fan-out, measure whether parallel questions are billed per call or
per question (same ~3k-token state with 1, 5, 10 questions). If per question: switch to reduced heuristic
fan-out (`action_category` + 2–3 likely sub-menus; others in a second call on demand) and revise §11 for
owner approval.

*Estimate (assuming per-call billing, ~3.8k tokens/decision, 0.5 Hz mean cadence, Jev input $0.042/M):*
$$15 \times 0.5 \times 3800 \times 3600 \approx 103\text{M tokens/h} \Rightarrow \approx \$4.3/\text{h}$$
Sleep reduces this ~10%. Speech ≈ $0.4–0.9/h. Consolidations: cents per personal day per character.
Prices as of 2026-09-25 (early access).

*Revised after T1.03b (billing confirmed per call; the 13 decision questions add ~2,020 tokens per call,
see `docs/reports/T1.03b-billing.md`), with D12 (8 characters, 0.4 Hz):*
$$8 \times 0.4 \times 4438 \times 3600 \approx 51\text{M tokens/h} \Rightarrow \approx \$2.15/\text{h}$$
With a 4k-token state (~6,020 tokens/decision): ≈ $2.91/h. At 0.5 Hz: $2.68/h and $3.64/h.

---

## 12. Emotional state

### 12.1 Distribution
- **RE-01** Emotions: Plutchik's 8 + neutral (`joy`, `trust`, `fear`, `surprise`, `sadness`, `disgust`, `anger`,
`anticipation`, `neutral`). Configurable list.
- **RE-02** Every 5th decision includes an `emotion` Choice in the same call and same context.
Output is a probability distribution ${p}_{i}$.

### 12.2 Modifiers
- **RE-03** Each modifier $k$: emotion ${e}_{k}$, intensity ${I}_{k}\in [0,1]$, reason (≤15 words, timeless), reference
day ${d}_{0,k}$, duration ${\tau }_{k}$ in personal days.

${I}_{k}(d)={I}_{k}^{0}\cdot \max\limits_{}\left({0,\ 1-\frac{d-{d}_{0,k}}{{\tau }_{k}}}\right)$

$p{'}_{i}=\frac{{p}_{i}\cdot \exp \left({\beta \sum\limits_{k}^{}{I}_{k}(d)[{e}_{k}=i]}\right)}{\sum\limits_{j}^{}{p}_{j}\cdot \exp \left({\beta \sum\limits_{k}^{}{I}_{k}(d)[{e}_{k}=j]}\right)}$
Optional inertia: ${p}^{final}=\alpha p'+(1-\alpha ){p}^{prev}$. Defaults $\beta =2$, $\alpha =0.6$,
linear decay (exponential optional) *(calibrate: E-05)*.
- **RE-04** Jev context includes dominant emotion, secondary if $p>0.25$, and active modifiers as
"reason + intensity band".

### 12.3 Fortnightly revision
During fortnightly compaction (heavy LLM), decide:
1. **Creation:** whether any memory of the 15 days causes a lasting emotional effect — which emotion, intensity,
duration → new modifier.
2. **Revision:** whether any existing modifier's duration or intensity should change (reinforced, relieved, resolved).

- **RE-05** LLM outputs operations `create` / `adjust` / `remove` with short justification. Intensity and
duration are returned as **categories** and converted by code
(e.g. `faint`=0.15, `mild`=0.3, `moderate`=0.5, `strong`=0.7, `overwhelming`=0.9; `about a week`=7 days).
- **RE-06** Jev validates each operation with a Noul; rejected operations are discarded.
- **RE-07** Before `create`, a Jev Choice checks for an existing modifier with the same cause; if found, becomes `adjust`.
- **RE-08** `adjust` sets ${I}^{0}$, ${d}_{0}\leftarrow$ now, new $\tau$, updated reason. Limits: $I\leq 1$,
$\tau \leq 60$ days, ≤6 active modifiers (lowest remaining intensity evicted).

| Metric | Target |
|---|---|
| Emotion × event coherence (labeled scenarios) | ≥80% |
| Correct expiry/adjust (unit tests) | 100% |
| Duplicate-cause modifiers | 0 |
| Traumatic test event creates modifier; trivial does not | ≥85% |
| Characters "neutral" >90% of time in eventful simulation | 0 |

---

## 13. Memory

| Level | Produced by | Retention |
|---|---|---|
| Recent | Code templates + Jev filter (Noul `keep`, Score importance 1–5) | Until next consolidated sleep |
| Daily | Light LLM: 2–3 paragraphs (120–300 words) + one-sentence short version | Buffer of 5–20 |
| Fortnightly | Heavy LLM: 2–3 paragraphs covering 15 days | Permanent |

- **RMe-01** Raw events → sentences via code templates; near-duplicates aggregated in a 10 s window before the
Jev filter. Own speech and thoughts always kept.
- **RMe-02** Day ends at consolidated sleep; recent → daily.
- **RMe-03** At 20 dailies: the 15 oldest → 1 fortnightly and deleted; 5 remain as buffer. Same step: emotional
modifiers (§12.3), likes/dislikes add/modify/remove, medium+long goal reassessment, personality change if a
Jev Noul indicates it is justified.
- **RMe-04** Transactional: new memory persisted before old is deleted.

| Metric | Target |
|---|---|
| Key-fact retention after daily / fortnightly compaction (LLM judge, probe questions) | ≥90% / ≥75% |
| Daily summary length | 120–300 words |
| Recent memories kept per day (standard scenario) | 30–150 |
| Scenario-flagged important events present in recent memory | ≥95% |

---

## 14. Dynamic opinions (dissonance, synergy, rupture)

### 14.1 Structure
```json
{
"target": "npc_03 | concept:leadership",
"kind": "social | general",
"nuanceDescription": "I feel deep gratitude and trust toward Bob for his constant care for my survival.",
"valence": "positive | negative | mixed",
"dissonanceBuffer": [{ "impression": "Bob refused to help while I was bleeding", "day": 12 }],
"stubbornnessBase": 6,
"stubbornness": 9,
"createdDay": 2,
"lastRuptureDay": null
}
```
- `nuanceDescription`: 1–3 sentences, first person, **strictly timeless**.
- Wrong: *"I like Bob because he gave me food yesterday."*
- Right: *"I feel deep gratitude and trust toward Bob for his constant care for my survival."*

### 14.2 Cycle (consolidation step [2])
1. Light LLM extracts the day's impressions (target, kind, sentence, importance).
2. Code selects candidate opinions (same target, or related general-opinion tags assigned by Jev).
One impression MAY affect several opinions (propagation).
3. Jev classifies each (impression, opinion) pair: `contradicts` / `strongly_agrees` / `agrees` / `irrelevant`.
4. Code applies: `contradicts` → add to buffer; `agrees` → stubbornness +1; `strongly_agrees` → +2.
5. **Rupture** if $|buffer|>stubbornness$: heavy LLM rewrites the opinion using old opinion,
buffer and personality (tone of change fits personality); buffer cleared; stubbornness reset to base;
**goal re-evaluation triggered** (§15.3).
6. No existing opinion on target and impression importance ≥4 → light LLM creates one.

- **ROp-01 Timelessness:** English regex (`yesterday`, `last week`, `recently`, `today`, `this morning`,
`on day \d+`, `ago`, …) + Jev Noul; on failure regenerate (≤2 attempts).
- **ROp-02** Decision context includes ≤3 opinions, only about targets present in perception or goals.

### 14.3 Parameters decided by experiment (E-01)
| Parameter | Variants |
|---|---|
| `opinion.stubbornness_cap` | none / 2× base / 3× base |
| `opinion.buffer_decay_days` | none / −1 item per N days without new contradiction (N=5, 10) |
| `opinion.synergy_increment` | {+1, +2} / {+0.5, +1} |
| `opinion.stubbornness_decay_days` | none / −1 per 10 days toward base |

| Metric | Target |
|---|---|
| Classification accuracy vs. human labels (≥100 pairs) | ≥85% |
| Opinions containing temporal markers after validation | 0% |
| Rupture exactly when $\lvert buffer \rvert > stubbornness$ (property test) | 100% |
| `stubborn` needs more evidence to rupture than `fickle` | always |

---

## 15. Goals and deep thinking

- **RG-01** Horizons: immediate (minutes–hours), medium (days), long (weeks).
- **RG-02 Deep thinking:** Jev MAY choose `think` when goals seem impossible (aided by `goal_blocked`).
The LLM re-evaluates and generates immediate goals from: medium/long goals, personality, all three memory
levels, social/general opinions, perception, emotion. Output: validated JSON with a first-person `thought`
(stored as memory) and ≤3 immediate goals, each with `successCheck`.
- **RG-03 Thinking budget:** 20 units per personal day (light ≈ 1, deep ≈ 6) *(calibrate: E-06)*; refilled at
consolidated sleep; code prevents overspend.

### 15.3 Post-rupture re-evaluation
- **RG-04** On rupture, for **every goal in all three horizons**, a Jev Noul: *"Is this goal affected by the
changed opinion about X?"*. Goals with $P\geq 0.6$ are relevant *(calibrate: E-04)*.
- **RG-05** Heavy LLM reassesses relevant goals given old opinion, new opinion, buffer and full profile;
per goal: `keep` / `modify` / `remove`; MAY add new goals.
- **RG-06** If no goal passes the threshold, nothing is reassessed; this is valid and logged.

### 15.4 Trigger matrix
| Trigger | Immediate | Medium | Long |
|---|---|---|---|
| Deep thinking | ✔ | — | — |
| Opinion rupture | ✔ if relevant | ✔ if relevant | ✔ if relevant |
| Consolidated sleep (daily) | — | ✔ | — |
| Fortnightly compaction | — | ✔ | ✔ |
| Control returned to AI | ✔ (validity Noul) | — | — |

- **RG-07** Goal completion checked in code when possible (e.g. item in inventory); otherwise Jev Noul.

| Metric | Target |
|---|---|
| Generated goals executable with existing actions (judge) | ≥85% |
| Goal relevance precision / recall (labeled) | ≥80% / ≥80% |
| Budget overspend | 0 |
| After artificial block, character re-plans within 60 s game time | ≥80% |

---

## 16. World persistence (Phase 7)

- **RW-01 MVP (from Phase 1):** persist minds only (SQLite); characters respawn in a new round by `stableGuid`.
- **RW-02 Full:** save/load grids (tiles, entities, atmosphere, solutions, power), mobs (health, inventory,
needs, fatigue) and cognitive DB, linked by `stableGuid`.
- **RW-03** Starts with a ≤1-week spike on what SS14 already serializes → feasibility report + gap list.
- **RW-04** Autosave every N minutes and on shutdown; keep last 5 versioned saves.

| Metric (save→load round trip) | Target |
|---|---|
| Entities with position/container preserved | ≥99% |
| Gas per tile | error ≤1% |
| Character health, inventory, needs | 100% |
| Save time (20 characters, medium map) | ≤5 s |

---

## 17. Non-functional requirements

| ID | Requirement | Target |
|---|---|---|
| RNF-01 | Server tick with 20 characters | p99 ≤33 ms (30 TPS) |
| RNF-02 | Cognitive overhead on main thread | ≤2 ms/tick |
| RNF-03 | Extra RAM per character | ≤5 MB |
| RNF-04 | Continuous session | ≥4 h with 15 characters |
| RNF-05 | API errors (timeout, 429, 5xx) | retry/backoff; no hangs or crashes |
| RNF-06 | Observability | structured JSONL log per call (role, model, tokens, latency, cost, decision, confidence) + per-character inspector |
| RNF-07 | Licensing | code MIT; most assets CC-BY-SA 3.0, some CC-BY-NC-SA — audit before any distribution |

---

## 18. Calibration experiments

Run in `Cognition.Sandbox`, accelerated days, Replay where possible. Each produces `docs/reports/E-xx.md` with a
recommendation; owner approves before a variant becomes default (RDev-05).

### E-01 — Stubbornness dynamics
Scenarios (30 personal days each, personalities `stubborn` / `default` / `fickle`):
- **A. Consistent betrayal:** one strong contradiction per day.
- **B. Noise:** random 10% contradictions, 30% reinforcements.
- **C. Long reinforcement then reversal:** 15 days reinforcement, then 15 days contradiction.

| Metric | Target |
|---|---|
| A: days to rupture (`default`) | 5–15 |
| A: order `fickle` < `default` < `stubborn` | always |
| B: ruptures per opinion in 30 days | ≤1 |
| C: rupture occurs in reversal phase | yes, within ≤25 days |
| "Frozen" opinions (unbreakable within horizon) | ≤5% |
| Psychological plausibility (LLM judge) | ≥7/10 |

Winner = most targets met; tie → simplest variant. Classification MAY be simulated (scenario pre-labels
impressions) to control cost; real LLM rewrite only for the winning variant.

| ID | Experiment | Calibrates |
|---|---|---|
| E-02 | Sleep rhythm | fatigue rate, ${T}_{wake}$, min consolidated sleep |
| E-03 | Decision cadence | intervals, triggers, cost/hour |
| E-04 | Jev thresholds | per-question thresholds (RJ-06, RJ-18, RG-04, memory keep, temporal check) |
| E-05 | Emotional dynamics | $\beta \in {1,2,3}$, $\alpha \in {0.4,0.6,0.8}$, decay shape |
| E-06 | Thinking budget | daily units, light/deep cost |

---

## 19. Development process

### 19.1 Rules for the agentic IDE
- **RDev-01** Every task/PR references affected requirement IDs and runs corresponding metrics.
- **RDev-02** Three evaluation layers: (1) parametric tests; (2) Jev judge (Score 1–10 with rubric), calibrated
against ≥30 human-labeled examples; (3) LLM judge (heavy model, fixed rubric) for text quality.
- **RDev-03** Requirement scorecard (`scorecard.json` + `.md`) on every suite run: ID → measured → target →
status → delta. Regression >5% blocks merge. A judge with Spearman correlation <0.6 vs. human labels does not
count toward merge gating for that requirement.
- **RDev-04** CI runs in `replay-strict` (zero cost). Live runs on demand with a cost cap (default US$ 2/run).
- **RDev-05** New metrics/thresholds proposed by the agent require owner approval (`docs/proposals/`).
- **RDev-06** Code, comments, prompts, test names, logs in English.
- **RDev-07** At the end of each phase the agent writes `docs/phaseN-plan.md` for the next phase (tasks,
dependencies, requirement IDs, acceptance), and stops for owner approval before executing it.
- **RDev-08** Test maps for SS14 phases start with few characters in small, purpose-built spaces, one per test.

### 19.2 Phases
| Phase | Deliverables | Exit criteria |
|---|---|---|
| 0. Fork | Local build, launcher, auth off, no-antag default mode | Game runs locally; existing SS14 tests pass |
| 1. Core + Sandbox | Data model, Jev/LLM clients, Replay, grid sandbox, prompt library, mind persistence | §§9, 12–15 metrics in sandbox; E-01, E-05 approved; cost ≤ RC-04 or revised |
| 2. Perception | `PerceptionSystem` + test maps | §7 metrics |
| 3. Actions L1 | Menus, executor, speech | §9.5 with 1–3 characters |
| 4. Sleep & long-term | `FatigueSystem`, consolidation in-game, 30 accelerated days | §8; E-02 |
| 5. Switchable control | `ControlSwitchSystem` | §10 |
| 6. Scale & UI | 10→15→20 characters, OpenRouter menu, inspector, cost panel | RNF-01/02, RC-04, RM-01…06; E-03 |
| 7. World persistence | Spike + implementation | §16 |
| 8. Layer 2 & antagonists | Machine UI adapters, antagonist objectives → long goals | Per owner priority |

**Dedicated test maps:** hunger with visible food · food behind door · gas leak · item negotiation · injured
asking for help · occlusion · identity (known vs. unknown speaker) · distant bed with high fatigue ·
control switch mid-task · adversarial speech.

---

## 20. Key risks

| Risk | Mitigation |
|---|---|
| Jev early access (price, limits, API changes) | Pinned version, isolated client, Replay |
| Per-question billing inflates cost | Billing gate RC-05; reduced heuristic fan-out |
| Low Jev confidence on long states | Smaller context (2.5k target), clearer option descriptions, two-stage decisions |
| Action space too large (all systems kept) | Reach/precondition filters, two-stage menus, incremental adapters |
| Sandbox diverges from SS14 | Formatting in Core (RA-06); same scenarios reproduced as SS14 test maps |
| Replay hides prompt changes | Template hash in replay key |
| Full world persistence infeasible | Spike first; minds-only MVP suffices |
| Sleep rhythm feels artificial | E-02 + config |
| Future need for another language | Prompts externalized; only speech output would change |

---

## 21. Open items (non-blocking for Phases 0–3)

| # | Item | Default until decided |
|---|---|---|
| A1 | Time scale: is ~40 min awake / 4 min sleep right for play style? | 40 / 4 |
| A2 | Which custom-UI machines first in Layer 2? | Food/drink dispensers → chemistry → consoles |
| A3 | Antagonist objectives replace or add to long goals? | Add, high priority |

---

## Appendix A — Glossary
| Term | Meaning |
|---|---|
| Jev | TypeSafe "System 1" model returning typed Choice/Score/Noul answers with probabilities |
| Choice / Score / Noul | Pick one option (≤255) / ordinal rating (2–10 levels) / probability of "yes" |
| Fan-out | Several Jev questions over one shared `state` in one call |
| Personal day | Period between consolidated sleeps of a given character |
| Consolidation | Pipeline run during consolidated sleep (§8.3) |
| Rupture (burst) | Opinion collapse when dissonance buffer exceeds stubbornness |
| Modifier | Temporary lasting emotional status reweighting Jev's emotion distribution |
| Affordances | Set of actions currently possible for a character |
| Replay | Recorded AI calls reused deterministically |
