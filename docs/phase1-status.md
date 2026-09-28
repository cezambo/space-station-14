# Phase 1 status

Updated by the agent after each task. "Difficulty" guides which model should take it.

| Task | State | Difficulty | Notes |
|---|---|---|---|
| T1.01–T1.12 | done | | see git log |
| T1.13 Scenario DSL | done | | 5 scripted scenarios in CI, 3 agent scenarios waiting for T1.16 |
| T1.14 ContextAssembler | done | | p99 measured with T1.16; choices in Q-8 |
| T1.15 DecisionCallBuilder | next | **high** | Jev question design (RJ-16/17/20), two-stage >255 |
| T1.16 DecisionInterpreter | todo | medium | per-question thresholds; closes the loop for agent scenarios |
| T1.17 Scheduler | todo | medium | |
| T1.18 SpeechService | todo | medium | |
| T1.19 DeepThinkingService | todo | medium | |
| T1.20 EmotionSystem | todo | **high** | exact RE-03 formulas, property tests |
| T1.21 Recent memory | todo | medium | |
| T1.22 Consolidation pipeline | todo | **high** | idempotent, checkpointed, atomic commit |
| T1.23 Daily summary | todo | low | |
| T1.24 OpinionSystem | todo | **high** | largest task (L) |
| T1.25 Rupture | todo | medium | |
| T1.26 Fortnightly | todo | **high** | (L) |
| T1.26b Control mode | todo | low | |
| T1.27 Scorecard | todo | medium | |
| T1.28 Labeling | **OWNER** | | stop here and report |
| T1.29–T1.31 | todo | medium | after T1.28 |
