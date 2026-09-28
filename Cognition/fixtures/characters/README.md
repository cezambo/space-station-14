# Seed characters (T1.09, RD-01, RD-02)

One `<id>.json` per character, loaded by `CharacterSeeds.LoadDirectory`. A seed holds only authored data:
`ss14Profile`, `personality` (Big Five 0-1, tags, likes, dislikes) and initial `medium`/`long` goals.

- `baseStubbornness` is **not** written here: it comes from the tags via `[opinion].stubbornness_by_tag`
  (`stubborn` = 8, `fickle` = 3, otherwise `stubbornness_default` = 5). Two such tags on one character is an error.
- Everything else starts at day 1: full thinking budget, fatigue 0, `neutral` emotion, no opinions, no
  acquaintances (names are learned in play, RD-03), no immediate goals.
- `stableGuid` values use the `5eed…` prefix so seed minds are easy to spot in the database.
- Unknown or missing fields fail the load; enum values are lowercase (`high`, `strong`, …).

| Id | Name | Job | Stubbornness |
|---|---|---|---|
| npc_01 | Ana Souza | Chef | 8 (stubborn) |
| npc_02 | Bob Hartley | Security Officer | 8 (stubborn) |
| npc_03 | Clara Mendes | Medical Doctor | 5 |
| npc_04 | Dmitri Volkov | Station Engineer | 8 (stubborn) |
| npc_05 | Ember Lark | Botanist | 3 (fickle) |
| npc_06 | Felix Ortega | Bartender | 3 (fickle) |
| npc_07 | Greta Lindqvist | Captain | 5 |
| npc_08 | Hiss-of-Embers | Atmospheric Technician | 8 (stubborn) |
| npc_09 | Iris Nakamura | Scientist | 3 (fickle) |
| npc_10 | Jonah Pike | Janitor | 5 |
