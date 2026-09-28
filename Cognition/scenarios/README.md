# Scenario DSL (T1.13, RDev-01)

One YAML file per scenario. Loaded by `ScenarioLoader` (strict: unknown keys are errors) and run by
`ScenarioRunner` in `Cognition.Sandbox/Scenarios/`. All problems in a file are reported together, prefixed
with the scenario id.

- `driver: scripted` scenarios run in CI (`ScenarioDslTests.ScriptedScenarioPasses`); only the `script` acts.
- `driver: agent` scenarios need an `IScenarioDriver` (the cognitive loop, from T1.16); CI only checks
  that they load and build. They run in `replay-strict` by default.

## Top level

| key | meaning |
|---|---|
| `id` | unique; used in reports |
| `description` | what happens and what a good mind should do |
| `requirements` | requirement IDs covered (required) |
| `driver` | `scripted` (default) or `agent` |
| `seed` | sandbox seed (all randomness) |
| `duration_s` | game seconds to run |
| `options` | `SandboxOptions` overrides by snake_case name, e.g. `fov_angle_deg: 120` |
| `map` | text grid, see legend |
| `characters`, `items`, `puddles`, `script`, `expect` | below |

Map legend: `#` or space wall, `.` floor, `T` table, `D`/`O`/`L` closed/open/locked door, `B` bed,
`F` fridge, `K` locker. Refs are numbered in reading order: `door1`, `bed1`, `fridge1`, `locker1`.
Coordinates are `[x, y]`, origin top-left.

## Characters

```yaml
- seed: npc_01                  # id from fixtures/characters
  description: woman in a chef's uniform   # what strangers see (RD-03)
  at: [1, 1]
  facing: north                 # optional
  needs: { hunger: 70, thirst: 0, fatigue: 85 }
  holding: [ <item> ]           # up to two
  knows: [npc_02]               # acquaintances: known by name
  asleep: true                  # submits sleep at the start (in bed if standing on one)
```

## Items and puddles

```yaml
items:
  - id: sandwich                # alias for script/expect; optional
    kind: food                  # food | drink | extinguisher | medkit | tool | key
    name: sandwich
    at: [6, 1]                  # or  in: fridge1
    opens: door1                # keys only
puddles:
  - { at: [3, 1], reagent: water }
```

## Script (timed steps)

Each step has `at_s` and exactly one of:

- `do: { who, verb, target?, item?, destination?, direction?, extent? }`: an `ActionIntent`.
  Verbs: `move pickup drop use open close lock unlock put take eat drink sleep wake speak give`.
  `extent`: `one_step short medium until_obstacle`.
- `say: { who, text, volume: whisper|normal|shout, to? }`
- `damage: { who, type, amount, bleeding? }`
- `door: { ref, state: open|closed|locked }`
- `leak: { id?, at, radius, gas }` and `stop_leak: <id>`

Steps run at the start of the first tick whose clock is at or past `at_s`.

## Expectations

Each has exactly one timing and exactly one check.

Timings: `within_s: N` or `never: true` (event and hears only), `at_s: N` (state when the clock passes N),
`at_end: true`.

| check | passes when |
|---|---|
| `event: { who?, kind, verb?, target?, item?, detail? }` | a matching world event is logged (`kind`: `action_completed action_failed spoke damaged fell_asleep woke collapsed lost_consciousness`) |
| `hears: { who, text, value? }` | the listener received speech containing `text` |
| `need: { who, need, below?, above? }` | hunger/thirst/fatigue is in range |
| `holding: { who, item, value? }` | holding the item (alias, ref or name) |
| `near: { who, of? \| at?, within }` | within N tiles of a ref/character or a cell |
| `asleep: { who, value? }` | sleep state |
| `sees: { who, what, value? }` | line of sight and field of view |
| `door: { ref, state }` | door state |
| `exists: { item, value? }` | item still exists (not eaten or drunk) |
| `damage: { who, below?, above? }` | total damage in range |

`value` defaults to `true`; `value: false` inverts the check.
