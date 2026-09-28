# Phase 1 status

Updated by the agent after each task. "Difficulty" guides which model should take it.

| Task | State | Difficulty | Notes |
|---|---|---|---|
| T1.01–T1.12 | done | | see git log |
| T1.13 Scenario DSL | done | | 5 scripted scenarios in CI, 3 agent scenarios waiting for T1.16 |
| T1.14 ContextAssembler | done | | p99 measured with T1.16; choices in Q-8 |
| T1.15 DecisionCallBuilder | done | | property test over 50 layouts: 0 invalid options; choices in Q-9 |
| T1.16 DecisionInterpreter | done | | category gates the submenu; low confidence keeps the current action (Q-12) |
| T1.17 Scheduler | done | | sliding window, 20 agents grant 10/s, damage first (Q-13); full SC-LOAD-20 YAML waits for a driver |
| T1.18 SpeechService | done | | 6 s gap, stale Noul, heard text stays quoted (Q-14); live p95 waits for the decision loop |
| T1.19 DeepThinkingService | next | medium | |
| T1.20 EmotionSystem | done | | linear formula as specified; exponential cutoff in Q-11 |
| T1.21 Recent memory | todo | medium | |
| T1.22 Consolidation pipeline | done | | kill/resume covered; steps [1]–[5] are placeholders (Q-10) |
| T1.23 Daily summary | todo | low | |
| T1.24 OpinionSystem | todo | **high** | largest task (L) |
| T1.25 Rupture | todo | medium | |
| T1.26 Fortnightly | todo | **high** | (L) |
| T1.26b Control mode | todo | low | |
| T1.27 Scorecard | todo | medium | |
| T1.28 Labeling | **OWNER** | | stop here and report |
| T1.29–T1.31 | todo | medium | after T1.28 |
