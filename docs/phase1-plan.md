# Phase 1 — Cognitive Core & Sandbox

**Goal:** a fully testable cognitive core running in a grid sandbox, without SS14.
**Requirements:** §§2–15 (sandbox scope), RA-01…06, RM-02…07, RC-01…05, RDev-01…08.
**Location:** `Cognition/` (own solution `Cognition.sln`).

## 1. Solution layout
```
Cognition/
├─ Cognition.sln
├─ cognition.toml
├─ prompts/{jev,llm}/
├─ fixtures/{replay,labels,characters}/
├─ scenarios/
├─ src/
│ ├─ Cognition.Core/ Model/ Perception/ Decision/ Consolidation/ Providers/
│ │ Scheduling/ Persistence/ Prompts/ Telemetry/
│ ├─ Cognition.Sandbox/ grid world implementing IWorldAdapter
│ └─ Cognition.Eval/ metrics, judges, scorecard, experiments, labeling CLI
└─ tests/
├─ Cognition.Core.Tests/ unit, no network
└─ Cognition.Scenario.Tests/ sandbox scenarios, replay-strict
```
Boundary rules: Core has no RobustToolbox/SS14 reference (RA-01). Adapters supply raw facts; categorization
and formatting live in Core (RA-06).

## 2. Core contracts (C#)

### 2.1 Providers
```csharp
namespace Cognition.Core.Providers;

public abstract record JevQuestion(string Instructions);
public sealed record ChoiceQuestion(string Instructions,
IReadOnlyDictionary<string, string> Criteria) : JevQuestion(Instructions); // ≤255
public sealed record ScoreQuestion(string Instructions,
IReadOnlyList<string> Levels) : JevQuestion(Instructions); // 2..10, ordered
public sealed record NoulQuestion(string Instructions,
string? WhenTrue = null, string? WhenFalse = null) : JevQuestion(Instructions);

public sealed record JevRequest(string Model, string State,
IReadOnlyDictionary<string, JevQuestion> Questions, string PurposeTag);

public abstract record JevAnswer;
public sealed record ChoiceAnswer(string Choice, double Confidence,
IReadOnlyDictionary<string, double> Probabilities) : JevAnswer;
public sealed record ScoreAnswer(double Score, string Legend, double Confidence,
IReadOnlyList<double> Probabilities) : JevAnswer; // ordinal use only (RJ-19)
public sealed record NoulAnswer(double PYes) : JevAnswer;

public sealed record UsageInfo(int InputTokens, int OutputTokens, decimal CostUsd);
public sealed record JevResponse(string RequestId, TimeSpan Latency,
IReadOnlyDictionary<string, JevAnswer> Answers, UsageInfo Usage);

public interface IJevClient { Task<JevResponse> EvaluateAsync(JevRequest r, CancellationToken ct); }

public enum LlmRole { Light, Heavy }
public sealed record LlmRequest(LlmRole Role, string SystemPrompt, string UserPrompt,
string? JsonSchema, int MaxOutputTokens, string PurposeTag);
public sealed record LlmResponse(string Text, UsageInfo Usage, string ModelId);
public interface ILlmClient { Task<LlmResponse> CompleteAsync(LlmRequest r, CancellationToken ct); }
```

### 2.2 World adapter
```csharp
namespace Cognition.Core.Perception;

public sealed record Vec2(float X, float Y);
public enum SpeechVolume { Whisper, Normal, Shout }

public sealed record RawPerceivedEntity(string EntityRef, string DisplayName, bool IsPerson,
string? StableGuid, Vec2 Position, IReadOnlyList<string> VisibleTraits,
IReadOnlyList<string> HeldItems, bool IsNovel);
public sealed record RawSound(string Kind, Vec2 Origin, float Loudness, int WallsBetween,
string? SpeakerGuid, string? SpeakerDescription, string? Text, SpeechVolume? Volume);
public sealed record RawEnvironment(float PressureKPa, float TemperatureK,
IReadOnlyDictionary<string, float> GasFractions, IReadOnlyList<string> Hazards);
public sealed record RawBiophysics(IReadOnlyDictionary<string, float> DamageByType,
float Pain, float Bleeding, float Hunger, float Thirst, float Fatigue,
float BodyTempK, float OxygenSaturation, bool Conscious);
public sealed record RawPerception(string AgentGuid, Vec2 Self, float FacingRad,
IReadOnlyList<RawPerceivedEntity> Seen, IReadOnlyList<RawSound> Heard,
RawEnvironment Env, RawBiophysics Body);

public interface IWorldAdapter
{
RawPerception GetPerception(string agentGuid); // FOV/occlusion applied (RP-01/02)
ActionAffordances GetAffordances(string agentGuid); // only possible actions (RJ-05)
void Submit(string agentGuid, ActionIntent intent); // deterministic execution (P2)
IObservable<WorldEvent> Events { get; }
GameClock Clock { get; }
}
```

### 2.3 Mind aggregate
```csharp
namespace Cognition.Core.Model;
public sealed class AgentMind
{
public required string StableGuid { get; init; }
public required Profile Profile { get; init; }
public required Personality Personality { get; set; }
public GoalSet Goals { get; } = new();
public MemoryStore Memory { get; } = new();
public OpinionStore Opinions { get; } = new();
public EmotionState Emotion { get; } = new();
public ThinkingBudget Budget { get; } = new();
public SleepState Sleep { get; } = new();
public Acquaintances Acquaintances { get; } = new();
public ControlMode Control { get; set; } = ControlMode.Ai;
public long Version { get; set; } // optimistic concurrency (RS-09)
}
```

## 3. Tasks
Size: S ≤1 agent-day · M 2–3 · L ≥4. **GATE** = stop and report. **[OWNER]** = needs owner.

| ID | Task | Depends | Reqs | Size |
|---|---|---|---|---|
| **A. Foundation** |||||
| T1.01 | Solution, CI, formatting, tests default to replay-strict | — | RA-01, RDev-04 | S |
| T1.02 | `cognition.toml` loader + validator | T1.01 | RM-04, RM-05 | S |
| T1.03 | `JevHttpClient` (types, local validation, 429/retry-after, timeouts); `docs/jev-wire-format.md` | T1.02 | RM-07, RNF-05 | M |
| **T1.03b** | **GATE — billing test (per call vs per question)** | T1.03 | RC-05 | S |
| T1.04 | `OpenAiCompatClient` (OpenRouter/local, reasoning_effort, JSON schema validation + 1 retry) | T1.02 | RM-03, RM-04 | M |
| T1.05 | Record/Replay (live/record/replay/replay-strict) | T1.03, T1.04 | RA-03 | M |
| T1.06 | Telemetry JSONL: tokens, USD, latency, decision, confidence | T1.05 | RM-06, RNF-06 | S |
| T1.07 | `PromptLibrary`: load `prompts/`, placeholders, template hash → replay key | T1.01 | P7, RL-04 | S |
| **B. Mind model** |||||
| T1.08 | Data model, serialization, transactional `SqliteMindStore` | T1.01 | RD-01…05, RMe-04 | M |
| T1.09 | 10 seed character profiles (`fixtures/characters/`) + base stubbornness derivation | T1.08 | RD-01, RD-02 | S |
| T1.10 | Categorizers (distance, direction, bands, day phase, budget, intensity, env sensation) | T1.01 | RP-05/06/08, RJ-09 | S |
| **C. Sandbox** |||||
| T1.11 | Grid world: tiles, doors, items, containers, needs, fatigue, sleep, speech, FOV, sound | T1.08 | P4, RS-01…08 | L |
| T1.12 | `PerceptionFormatter`: salience, limits, identity resolution | T1.10, T1.11 | RP-01…08, RD-03 | M |
| T1.13 | Scenario DSL (`scenarios/README.md`), runner, assertions | T1.11 | RDev-01 | M |
| **D. Decision** |||||
| T1.14 | `ContextAssembler`: token budgets, priority trimming, size logging | T1.07, T1.12 | RJ-08, §9.3 | M |
| T1.15 | `DecisionCallBuilder`: fan-out per gate result, affordance filtering, two-stage >255 | T1.03b, T1.14 | RJ-04/05/16/17/20 | M |
| T1.16 | `DecisionInterpreter`: per-question thresholds, `ActionIntent` | T1.15 | RJ-06, RJ-18 | S |
| T1.17 | `Scheduler`: triggers, intervals, global token bucket, priority, saturation behavior | T1.16 | RJ-01, RC-01/02 | M |
| T1.18 | `SpeechService`: light LLM, rate limit, stale check | T1.16 | RJ-10…12, RL-02 | M |
| T1.19 | `DeepThinkingService` + `ThinkingBudget` | T1.16 | RG-02/03, RJ-09 | M |
| **E. Inner state** |||||
| T1.20 | `EmotionSystem`: every-5th check, modifiers, decay, inertia | T1.15 | RE-01…04 | M |
| T1.21 | Recent memory: templates, aggregation, Jev filter, hard cap | T1.11, T1.03b | RMe-01, RS-11 | M |
| T1.22 | `SleepConsolidationPipeline`: state machine, checkpoints, atomic commit, merge | T1.08 | RS-09/10/12 | M |
| T1.23 | Step [1] daily summary + step [4] medium-goal reassessment | T1.22, T1.04 | RMe-02, §15.4 | M |
| T1.24 | Step [2] `OpinionSystem` + timelessness validation + opinion creation | T1.22 | §14, ROp-01/02 | L |
| T1.25 | Step [3] rupture → rewrite → goal relevance → reassessment | T1.24 | RG-04…06 | M |
| T1.26 | Step [5] fortnightly compaction + emotion/likes/goal/personality ops | T1.23, T1.20 | RMe-03, RE-05…08 | L |
| T1.26b | Control-mode support in Core (`player` mode logging, return-to-AI flow) | T1.19, T1.20 | RCt-02…05 | S |
| **F. Evaluation** |||||
| T1.27 | Eval harness, `scorecard.json/.md`, regression gate | T1.13 | RDev-02/03 | M |
| T1.28 | **[OWNER]** Labeling CLI + datasets | T1.24 | RDev-02 | M |
| T1.29 | Jev judge + LLM judge + calibration report (Spearman ≥0.6) | T1.27, T1.28 | RDev-02/03 | M |
| T1.30 | Experiment runners E-01, E-05 + reports | T1.24, T1.20, T1.27 | §18 | M |
| T1.31 | Cost measurement: real cost per agent-hour, projection to 8 agents (D12) | T1.17…T1.26 | RC-04 | S |

### Sprint order
```
S1 T1.01 → T1.02 → T1.03 → T1.03b (GATE) ; parallel: T1.07, T1.10, T1.04 → T1.05 → T1.06
S2 T1.08 → T1.09 ; T1.11 → T1.12 → T1.13
S3 T1.14 → T1.15 → T1.16 → {T1.17, T1.18, T1.19}
S4 T1.20, T1.21, T1.22 → T1.23 → T1.24 → T1.25 → T1.26 → T1.26b
S5 T1.27 → T1.28 [OWNER] → T1.29 → T1.30 → T1.31 → Phase 1 exit report (GATE)
```

## 4. Task cards (acceptance)

**T1.03 JevHttpClient.** `POST {base_url}/systemone`, Bearer `TYPESAFE_API_KEY`. Local validation rejects:
Choice >255 options, Score outside 2–10 levels, duplicate question IDs, empty instructions. 429 → honor
`retry-after` + jitter, ≤3 retries; 5xx → exponential backoff; timeout 2 s. Verify the exact wire format of
Choice/Score/Noul criteria against official docs and record it in `docs/jev-wire-format.md` (never guess).
*Accept:* serialization/validation unit tests; one manual live call (<US$ 0.05).

**T1.03b Billing gate (GATE).** Same ~3k-token state with 1, 5, 10 questions; compare billed usage from
response and dashboard. Report `docs/reports/T1.03b-billing.md`:
- per call → full fan-out (`prompts/jev/decision.yaml` all questions);
- per question → reduced fan-out using `include_when` rules in `decision.yaml`; memory filter uses
one call per event or filtering moves to sleep; revised §11 estimate for owner approval.
Also measure latency p50/p95 for 1/5/10 questions.

**T1.05 Replay.** Key `SHA256(canonical_json(request) + template_hash + model_id)`; files
`fixtures/replay/<purpose>/<hash>.json` (human-readable request + response). `replay-strict` fails on miss.
*Accept:* scenario run twice in replay-strict → byte-identical logs, zero cost.

**T1.10 Categorizers.** Pure functions, table-driven tests incl. boundaries (1.5, 5, 12 tiles; 22.5° sectors).
Directions: north = −Y in sandbox grid (screen up). Bands per requirements; values configurable.

**T1.11 Sandbox.** Tiles `floor/wall/door(open|closed|locked)/bed/table`; items (food, drink, extinguisher,
medkit, generic tool, key); containers (locker, fridge); needs (hunger, thirst, fatigue), simple damage,
bleeding; sleep per §8; FOV = configurable cone + Bresenham raycast (walls, closed doors occlude); sound range by
volume (whisper 2, normal 10, shout 20 tiles), −60% loudness per wall; actions `move, pickup, drop, use,
open, close, lock, unlock, put, take, eat, drink, sleep, wake, speak, give`; hazard `gas_leak` (area, causes
`thin air` + oxygen drop); fixed tick + time-scale factor; single seed for all randomness.
Out of scope: detailed atmos, chemistry, power. *Accept:* 20 agents at 100 ticks/s without AI <1 ms/tick;
0 FOV leaks across 50 generated layouts.

**T1.12 PerceptionFormatter.** Salience = weighted proximity + novelty + goal keyword match + danger; per-category
caps (RP-04); names only for acquaintances (RD-03). Output format:
```
SEEN:
- Bob (known) — near, northeast — holding a fire extinguisher — looks injured
- sandwich — within reach, south — on a table
HEARD:
- Bob, near, northeast, shouting: "There's a fire in the kitchen!"
ENVIRONMENT: air normal; slightly warm
BODY: hunger mild; thirst ok; fatigue strong; minor bruise on left arm
```
*Accept:* §7 metrics.

**T1.14 ContextAssembler.** Blocks/budgets per §9.3; token estimate chars/4 recalibrated per block type from
real usage; trim order per RJ-08. *Accept:* p99 ≤8k tokens in all scenarios; never-trim blocks always present.

**T1.15/T1.16 Decision.** Build questions from affordances only; option keys short/unique with descriptive
criteria; >255 → Score shortlist in batches → top 30 → Choice; add `emotion` when
`decisionsSinceLastCheck == 4`. Interpreter uses only the sub-menu matching `action_category`; confidence below
per-question threshold → `nothing` + `low_confidence` log. *Accept:* 0 invalid actions; §9.5 in sandbox.

**T1.17 Scheduler.** Priority per RC-01; global bucket 10 req/s Jev; per-role LLM buckets; sleeping/player
agents excluded; saturation → keep current action (HTN fallback stub). *Accept:* SC-LOAD-20.

**T1.18 Speech.** `prompts/llm/speech.md`; 1 line / 6 s / agent; >10 s → `speech_stale` Noul.
Other speech wrapped in `<heard>` tags. *Accept:* p95 ≤3 s live; SC-ADVERSARIAL-SPEECH passes.

**T1.19 Deep thinking.** `think_mode` light → light LLM, deep → heavy LLM, same template `deep_think.md`.
Budget costs from config; unaffordable options removed; thought stored as memory; goals validated JSON.
*Accept:* SC-BLOCKED-GOAL; 0 overspend.

**T1.20 Emotion.** Exact RE-03 formulas; fractional day RS-08. *Accept:* property tests (sum=1, I∈[0,1],
expiry at d0+τ), 100% unit tests.

**T1.21 Recent memory.** Code templates per event type; aggregate identical events in 10 s windows; Jev
`memory_filter.yaml`; own speech/thoughts always kept; hard cap drops lowest importance.
*Accept:* 30–150 items/day standard scenario; ≥95% of scenario-flagged events kept.

**T1.22 Consolidation.** State machine steps [1]–[6] (§8.3) on a copy; checkpoint per step; idempotent;
commit with version check; events during sleep → next day; failing step retried ≤2 then deferred.
*Accept:* SC-CONSOLIDATION-KILL; p95 ≤120 s live.

**T1.23** `daily_summary.md` (full + short); `medium_goals_daily.md`. *Accept:* 120–300 words; retention ≥90%.

**T1.24 Opinions.** `impressions.md` → candidates (same target + `opinion_tags.yaml`) → `opinion_classify.yaml`
→ apply increments (all §14.3 params configurable) → create new if importance ≥4 (`opinion_create.md`) →
timelessness (regex + `temporal_check.yaml`, ≤2 regenerations). *Accept:* §14 metrics.

**T1.25 Rupture.** `opinion_rewrite.md` (heavy) → `goal_relevance.yaml` per goal (threshold 0.6) →
`goal_reassess.md` (heavy). *Accept:* property test for rupture condition; relevance P/R ≥80%.

**T1.26 Fortnightly.** `fortnightly.md` (heavy); categories → numbers in code; each emotion op validated
(`emotion_op_validate.yaml`), deduped (`modifier_dedupe.yaml`); personality change gated by
`personality_update.yaml`. *Accept:* SC-TRAUMA; 0 duplicate causes; retention ≥75%.

**T1.26b Control.** In `player` mode: log actions/speech as own, no decisions; on return: `goal_valid.yaml`
per immediate goal → if any invalid, `reorient.md` (free, light) → emotion check. *Accept:* §10 metrics in
sandbox (simulated player script).

**T1.27 Scorecard.** Per requirement ID: measured, target, status, delta vs previous run; CI fails on >5%
regression.

**T1.28 [OWNER] Labeling.** `dotnet run --project src/Cognition.Eval -- label <dataset>`; one item per
screen, key-press answers. Datasets (agent generates candidates from sandbox):
| Dataset | Items | Owner time |
|---|---|---|
| impression × opinion relation | 120 | ~15 min |
| goal relevance to opinion change | 80 | ~10 min |
| judge calibration (log → 1–10 per requirement) | 40 | ~20 min |
| event → lasting emotional modifier? | 60 | ~8 min |

**T1.29 Judges.** `judge_requirement.yaml` (Jev) and `judge_llm.md` (heavy). Spearman vs labels ≥0.6 or
the judge does not gate that requirement.

**T1.30 Experiments.** E-01 grid (3 scenarios × 3 personalities × §14.3 variants) with simulated
classification; real rewrite only for the winner. E-05 sweep β∈{1,2,3}, α∈{0.4,0.6,0.8}, linear/exponential.
Reports in `docs/reports/E-01.md`, `E-05.md` → owner approval.

**T1.31 Cost.** Live 30-minute run, 5 agents, standard scenario mix; USD per agent-hour by role; projection
for 8 agents vs RC-04 (D12).

## 5. Phase 1 exit criteria (GATE)
| # | Criterion |
|---|---|
| S-1 | Scorecard meets §§9, 12, 13, 14, 15 metrics in sandbox |
| S-2 | 30 accelerated days, 5 agents, no crash, no corrupted state |
| S-3 | CI green in replay-strict, zero cost |
| S-4 | E-01 and E-05 approved by owner |
| S-5 | Projected cost for 8 agents ≤ US$ 6/h (D12), or revised §11 approved |
| S-6 | `docs/phase2-plan.md` written (RDev-07) |

## 6. Phase-specific risks
| Risk | Signal | Response |
|---|---|---|
| Per-question billing | decision cost ≥3× estimate | reduced fan-out; revise §11 |
| Low Jev confidence | `low_confidence` >20% | 2.5k context target, clearer criteria, two-stage decisions |
| Sandbox ≠ SS14 | metrics drop in Phase 2 | formatting in Core; same scenarios as SS14 test maps |
| Replay hides prompt changes | tests pass after prompt edits | template hash in replay key |
| Experiment cost | E-01 too expensive | simulated classification |
